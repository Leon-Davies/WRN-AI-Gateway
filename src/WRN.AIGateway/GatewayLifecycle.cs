using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace WRN.AIGateway
{
    internal sealed class GatewayRuntimeResult
    {
        public bool Healthy { get; set; }
        public bool Started { get; set; }
        public int ProcessId { get; set; }
        public int Port { get; set; }
        public int CatalogueRelease { get; set; }
        public string Status { get; set; }
    }

    internal sealed class GatewayPidRecord
    {
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; }
        public string StartedAtUtc { get; set; }
    }

    internal static class GatewayLifecycle
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer
            {
                MaxJsonLength = 1024 * 1024,
                RecursionLimit = 64
            };

        public static GatewayRuntimeResult EnsureHealthy(
            string baseDir,
            string stateRoot,
            int expectedCatalogueRelease,
            int timeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                throw new ArgumentNullException("baseDir");
            if (string.IsNullOrWhiteSpace(stateRoot))
                throw new ArgumentNullException("stateRoot");
            if (expectedCatalogueRelease < 1)
                throw new ArgumentOutOfRangeException(
                    "expectedCatalogueRelease");
            if (timeoutMilliseconds < 250)
                throw new ArgumentOutOfRangeException(
                    "timeoutMilliseconds");

            ModeGatewayConfig config;
            string configError;
            ModeCoordinator.ProbeGatewayConfig(
                stateRoot,
                out config,
                out configError);

            if (config == null)
            {
                return Fail(
                    "GATEWAY_CONFIG_INVALID:"
                    + (configError ?? "unknown"));
            }

            if (ModeCoordinator.ProbeGatewayHealth(
                config.Port,
                expectedCatalogueRelease))
            {
                return new GatewayRuntimeResult
                {
                    Healthy = true,
                    Started = false,
                    ProcessId = 0,
                    Port = config.Port,
                    CatalogueRelease =
                        expectedCatalogueRelease,
                    Status = "GATEWAY_ALREADY_HEALTHY"
                };
            }

            var gatewayExe = Path.GetFullPath(
                Path.Combine(
                    baseDir,
                    "WRN-AI-Gateway-Gateway.exe"));
            if (!File.Exists(gatewayExe))
                return Fail("GATEWAY_BINARY_MISSING");

            var existingRecord =
                ReadPidRecord(stateRoot);
            if (existingRecord != null
                && IsOwnedGatewayProcess(
                    existingRecord,
                    gatewayExe))
            {
                return Fail(
                    "OWNED_GATEWAY_RUNNING_BUT_UNHEALTHY");
            }

            RemovePidRecord(stateRoot);

            Process process = null;
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = gatewayExe,
                    Arguments =
                        "--state-root "
                        + QuoteArgument(stateRoot),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = baseDir
                };

                process = Process.Start(info);
                if (process == null)
                    return Fail(
                        "GATEWAY_PROCESS_START_FAILED");

                WritePidRecord(
                    stateRoot,
                    new GatewayPidRecord
                    {
                        ProcessId = process.Id,
                        ExecutablePath = gatewayExe,
                        StartedAtUtc =
                            DateTimeOffset.UtcNow.ToString("o")
                    });

                var deadline =
                    DateTime.UtcNow.AddMilliseconds(
                        timeoutMilliseconds);
                while (DateTime.UtcNow < deadline)
                {
                    process.Refresh();
                    if (process.HasExited)
                        break;

                    if (ModeCoordinator.ProbeGatewayHealth(
                        config.Port,
                        expectedCatalogueRelease))
                    {
                        return new GatewayRuntimeResult
                        {
                            Healthy = true,
                            Started = true,
                            ProcessId = process.Id,
                            Port = config.Port,
                            CatalogueRelease =
                                expectedCatalogueRelease,
                            Status = "GATEWAY_STARTED"
                        };
                    }

                    Thread.Sleep(100);
                }

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch
                {
                }

                RemovePidRecord(stateRoot);
                return Fail(
                    process.HasExited
                        ? "GATEWAY_EXITED_BEFORE_HEALTHY"
                        : "GATEWAY_HEALTH_TIMEOUT");
            }
            catch
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill();
                    }
                    catch
                    {
                    }
                }

                RemovePidRecord(stateRoot);
                return Fail(
                    "GATEWAY_PROCESS_START_FAILED");
            }
            finally
            {
                if (process != null)
                    process.Dispose();
            }
        }

        public static bool StopOwned(
            string baseDir,
            string stateRoot)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                throw new ArgumentNullException("baseDir");
            if (string.IsNullOrWhiteSpace(stateRoot))
                throw new ArgumentNullException("stateRoot");

            var record = ReadPidRecord(stateRoot);
            if (record == null)
                return true;

            var expectedExe = Path.GetFullPath(
                Path.Combine(
                    baseDir,
                    "WRN-AI-Gateway-Gateway.exe"));

            if (!string.Equals(
                Path.GetFullPath(record.ExecutablePath ?? ""),
                expectedExe,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "GATEWAY_PID_OWNERSHIP_MISMATCH");
            }

            Process process;
            try
            {
                process = Process.GetProcessById(
                    record.ProcessId);
            }
            catch (ArgumentException)
            {
                RemovePidRecord(stateRoot);
                return true;
            }

            using (process)
            {
                process.Refresh();
                if (process.HasExited)
                {
                    RemovePidRecord(stateRoot);
                    return true;
                }

                string actualPath;
                try
                {
                    actualPath =
                        Path.GetFullPath(
                            process.MainModule.FileName);
                }
                catch
                {
                    throw new InvalidOperationException(
                        "GATEWAY_PROCESS_IDENTITY_UNREADABLE");
                }

                if (!string.Equals(
                    actualPath,
                    expectedExe,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "GATEWAY_PID_OWNERSHIP_MISMATCH");
                }

                process.Kill();
                if (!process.WaitForExit(5000))
                    throw new InvalidOperationException(
                        "GATEWAY_STOP_TIMEOUT");
            }

            RemovePidRecord(stateRoot);
            return true;
        }

        private static bool IsOwnedGatewayProcess(
            GatewayPidRecord record,
            string expectedExe)
        {
            if (record == null
                || record.ProcessId <= 0
                || string.IsNullOrWhiteSpace(
                    record.ExecutablePath)
                || !string.Equals(
                    Path.GetFullPath(
                        record.ExecutablePath),
                    expectedExe,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var process =
                    Process.GetProcessById(
                        record.ProcessId))
                {
                    process.Refresh();
                    if (process.HasExited)
                        return false;

                    return string.Equals(
                        Path.GetFullPath(
                            process.MainModule.FileName),
                        expectedExe,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static GatewayPidRecord ReadPidRecord(
            string stateRoot)
        {
            var path = PidPath(stateRoot);
            if (!File.Exists(path))
                return null;

            try
            {
                var record =
                    Json.Deserialize<GatewayPidRecord>(
                        File.ReadAllText(
                            path,
                            Encoding.UTF8));
                return record != null
                    && record.ProcessId > 0
                    ? record
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static void WritePidRecord(
            string stateRoot,
            GatewayPidRecord record)
        {
            var gatewayRoot =
                Path.Combine(stateRoot, "gateway");
            Directory.CreateDirectory(gatewayRoot);
            var path = PidPath(stateRoot);
            var temp =
                path + ".tmp-"
                + Guid.NewGuid().ToString("N");

            File.WriteAllText(
                temp,
                Json.Serialize(record),
                new UTF8Encoding(false));

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        private static void RemovePidRecord(
            string stateRoot)
        {
            try
            {
                var path = PidPath(stateRoot);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static string PidPath(
            string stateRoot)
        {
            return Path.Combine(
                stateRoot,
                "gateway",
                "gateway.pid.json");
        }

        private static string QuoteArgument(
            string value)
        {
            return "\""
                + (value ?? string.Empty)
                    .Replace("\"", "\\\"")
                + "\"";
        }

        private static GatewayRuntimeResult Fail(
            string status)
        {
            return new GatewayRuntimeResult
            {
                Healthy = false,
                Started = false,
                ProcessId = 0,
                Port = 0,
                CatalogueRelease = 0,
                Status = status
            };
        }
    }
}
