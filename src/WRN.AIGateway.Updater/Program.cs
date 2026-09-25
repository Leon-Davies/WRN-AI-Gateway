using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WRN.AIGateway.Updater
{
    internal sealed class ActivationAudit
    {
        public string completedAtUtc { get; set; }
        public string status { get; set; }
        public bool success { get; set; }
        public bool rolledBack { get; set; }
        public string candidateVersion { get; set; }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                string installRoot;
                string candidateAppRoot;
                int parentPid;
                bool launch;

                if (!TryParse(
                    args,
                    out installRoot,
                    out candidateAppRoot,
                    out parentPid,
                    out launch))
                {
                    Environment.ExitCode = 2;
                    return;
                }

                if (!WaitForParent(
                    parentPid,
                    120000))
                {
                    WriteAudit(
                        installRoot,
                        "UPDATE_DEFERRED_LAUNCHER_STILL_RUNNING",
                        false,
                        false,
                        candidateAppRoot);
                    Environment.ExitCode = 20;
                    return;
                }

                if (Process.GetProcessesByName(
                    "WRN-AI-Gateway").Length > 0)
                {
                    WriteAudit(
                        installRoot,
                        "UPDATE_DEFERRED_LAUNCHER_RUNNING",
                        false,
                        false,
                        candidateAppRoot);
                    Environment.ExitCode = 20;
                    return;
                }

                if (Process.GetProcessesByName(
                    "claude").Length > 0)
                {
                    WriteAudit(
                        installRoot,
                        "UPDATE_DEFERRED_CLAUDE_RUNNING",
                        false,
                        false,
                        candidateAppRoot);
                    Environment.ExitCode = 20;
                    return;
                }

                if (!RunSelfCheck(
                    candidateAppRoot,
                    30000))
                {
                    WriteAudit(
                        installRoot,
                        "UPDATE_CANDIDATE_HEALTH_FAILED",
                        false,
                        false,
                        candidateAppRoot);
                    Environment.ExitCode = 21;
                    return;
                }

                var result =
                    UpdateActivator.Activate(
                        installRoot,
                        candidateAppRoot,
                        delegate(string activeRoot)
                        {
                            return RunSelfCheck(
                                activeRoot,
                                30000);
                        });

                WriteAudit(
                    installRoot,
                    result.Status,
                    result.Success,
                    result.RolledBack,
                    result.ActiveRoot);

                if (launch
                    && (result.Success
                        || result.RolledBack))
                {
                    TryLaunch(
                        Path.Combine(
                            installRoot,
                            "current",
                            "WRN-AI-Gateway.exe"));
                }

                Environment.ExitCode =
                    result.Success
                        ? 0
                        : result.RolledBack
                            ? 22
                            : 23;
            }
            catch
            {
                Environment.ExitCode = 24;
            }
        }

        private static bool TryParse(
            string[] args,
            out string installRoot,
            out string candidateAppRoot,
            out int parentPid,
            out bool launch)
        {
            installRoot = null;
            candidateAppRoot = null;
            parentPid = 0;
            launch = false;

            for (var i = 0;
                args != null
                && i < args.Length;
                i++)
            {
                if (string.Equals(
                    args[i],
                    "--install-root",
                    StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length)
                {
                    installRoot =
                        Path.GetFullPath(
                            args[++i]);
                }
                else if (string.Equals(
                    args[i],
                    "--candidate-app-root",
                    StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length)
                {
                    candidateAppRoot =
                        Path.GetFullPath(
                            args[++i]);
                }
                else if (string.Equals(
                    args[i],
                    "--parent-pid",
                    StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length)
                {
                    int.TryParse(
                        args[++i],
                        out parentPid);
                }
                else if (string.Equals(
                    args[i],
                    "--launch",
                    StringComparison.OrdinalIgnoreCase))
                {
                    launch = true;
                }
            }

            return !string.IsNullOrWhiteSpace(
                    installRoot)
                && !string.IsNullOrWhiteSpace(
                    candidateAppRoot);
        }

        private static bool WaitForParent(
            int processId,
            int timeoutMilliseconds)
        {
            if (processId <= 0)
                return true;

            try
            {
                using (var process =
                    Process.GetProcessById(
                        processId))
                {
                    return process.WaitForExit(
                        timeoutMilliseconds);
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
        }

        private static bool RunSelfCheck(
            string appRoot,
            int timeoutMilliseconds)
        {
            var exe =
                Path.Combine(
                    appRoot,
                    "WRN-AI-Gateway.exe");

            if (!File.Exists(exe))
                return false;

            Process process = null;
            try
            {
                process =
                    Process.Start(
                        new ProcessStartInfo
                        {
                            FileName = exe,
                            Arguments =
                                "--self-check",
                            WorkingDirectory =
                                appRoot,
                            UseShellExecute =
                                false,
                            CreateNoWindow = true
                        });

                if (process == null)
                    return false;

                if (!process.WaitForExit(
                    timeoutMilliseconds))
                {
                    try { process.Kill(); }
                    catch { }
                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (process != null)
                    process.Dispose();
            }
        }

        private static void TryLaunch(
            string exe)
        {
            try
            {
                if (!File.Exists(exe))
                    return;

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = exe,
                        WorkingDirectory =
                            Path.GetDirectoryName(
                                exe),
                        UseShellExecute = true
                    });
            }
            catch
            {
            }
        }

        private static void WriteAudit(
            string installRoot,
            string status,
            bool success,
            bool rolledBack,
            string candidateRoot)
        {
            try
            {
                var updates =
                    Path.Combine(
                        installRoot,
                        "updates");
                Directory.CreateDirectory(
                    updates);

                string version = null;
                try
                {
                    var identityPath =
                        Path.Combine(
                            candidateRoot
                                ?? string.Empty,
                            "app-release.json");
                    if (File.Exists(identityPath))
                    {
                        var serializer =
                            new JavaScriptSerializer();
                        var identity =
                            serializer.Deserialize<
                                Dictionary<string, object>>(
                                File.ReadAllText(
                                    identityPath,
                                    Encoding.UTF8));
                        object value;
                        if (identity != null
                            && identity.TryGetValue(
                                "version",
                                out value))
                        {
                            version =
                                Convert.ToString(
                                    value);
                        }
                    }
                }
                catch
                {
                }

                var audit =
                    new ActivationAudit
                    {
                        completedAtUtc =
                            DateTimeOffset.UtcNow
                                .ToString("o"),
                        status = status,
                        success = success,
                        rolledBack = rolledBack,
                        candidateVersion =
                            version
                    };

                var json =
                    new JavaScriptSerializer()
                        .Serialize(audit);

                File.WriteAllText(
                    Path.Combine(
                        updates,
                        "last-activation.json"),
                    json,
                    new UTF8Encoding(false));
            }
            catch
            {
            }
        }
    }
}
