using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WRN.AIGateway
{
    internal sealed class AppUpdateRemoteResult
    {
        public bool Success { get; set; }
        public bool InstalledContext { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool Staged { get; set; }
        public string Status { get; set; }
        public int CurrentRelease { get; set; }
        public string CurrentVersion { get; set; }
        public int CandidateRelease { get; set; }
        public string CandidateVersion { get; set; }
        public string Title { get; set; }
        public string Notes { get; set; }
        public string CandidateAppRoot { get; set; }
        public string StageRoot { get; set; }
    }

    internal sealed class AppUpdateActivationStartResult
    {
        public bool Started { get; set; }
        public string Status { get; set; }
        public string UpdaterPath { get; set; }
    }

    internal static class AppUpdateRuntime
    {
        private sealed class VerifiedRemoteManifest
        {
            public byte[] Bytes;
            public string SignatureText;
            public AppReleaseManifest Manifest;
        }

        public static bool TryGetInstalledContext(
            string baseDir,
            out string installRoot)
        {
            installRoot = null;
            if (string.IsNullOrWhiteSpace(baseDir))
                return false;

            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "WRN-AI-Gateway"));

            var expectedCurrent = Path.GetFullPath(
                Path.Combine(expectedRoot, "current"))
                .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var actual = Path.GetFullPath(baseDir)
                .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!string.Equals(
                actual,
                expectedCurrent,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            installRoot = expectedRoot;
            return true;
        }

        public static AppUpdateRemoteResult CheckRemote(
            string baseDir)
        {
            string installRoot;
            if (!TryGetInstalledContext(
                baseDir,
                out installRoot))
            {
                return new AppUpdateRemoteResult
                {
                    Success = true,
                    InstalledContext = false,
                    UpdateAvailable = false,
                    Status = "UPDATE_NOT_INSTALLED_CONTEXT"
                };
            }

            var current =
                ReadCurrentIdentity(baseDir);

            try
            {
                var remote =
                    FetchVerifiedManifest();

                if (remote.Manifest.release
                    < current.release)
                {
                    return Failure(
                        "UPDATE_REMOTE_ROLLBACK_REJECTED",
                        current);
                }

                return new AppUpdateRemoteResult
                {
                    Success = true,
                    InstalledContext = true,
                    UpdateAvailable =
                        remote.Manifest.release
                        > current.release,
                    Staged = false,
                    Status =
                        remote.Manifest.release
                        > current.release
                            ? "UPDATE_AVAILABLE"
                            : "UPDATE_ALREADY_CURRENT",
                    CurrentRelease = current.release,
                    CurrentVersion = current.version,
                    CandidateRelease =
                        remote.Manifest.release,
                    CandidateVersion =
                        remote.Manifest.version,
                    Title =
                        remote.Manifest.title,
                    Notes =
                        remote.Manifest.notes
                };
            }
            catch (WebException)
            {
                return Failure(
                    "UPDATE_NETWORK_UNAVAILABLE",
                    current);
            }
            catch (InvalidDataException ex)
            {
                return Failure(
                    SafeStatus(ex.Message),
                    current);
            }
            catch
            {
                return Failure(
                    "UPDATE_CHECK_FAILED",
                    current);
            }
        }

        public static AppUpdateRemoteResult DownloadAndStage(
            string baseDir)
        {
            string installRoot;
            if (!TryGetInstalledContext(
                baseDir,
                out installRoot))
            {
                return new AppUpdateRemoteResult
                {
                    Success = false,
                    InstalledContext = false,
                    Status = "UPDATE_NOT_INSTALLED_CONTEXT"
                };
            }

            var current =
                ReadCurrentIdentity(baseDir);

            try
            {
                var remote =
                    FetchVerifiedManifest();

                if (remote.Manifest.release
                    < current.release)
                {
                    return Failure(
                        "UPDATE_REMOTE_ROLLBACK_REJECTED",
                        current);
                }

                if (remote.Manifest.release
                    == current.release)
                {
                    return new AppUpdateRemoteResult
                    {
                        Success = true,
                        InstalledContext = true,
                        UpdateAvailable = false,
                        Staged = false,
                        Status = "UPDATE_ALREADY_CURRENT",
                        CurrentRelease =
                            current.release,
                        CurrentVersion =
                            current.version,
                        CandidateRelease =
                            remote.Manifest.release,
                        CandidateVersion =
                            remote.Manifest.version
                    };
                }

                var artifact =
                    DownloadBytesWithRetry(
                        remote.Manifest.artifactUrl,
                        AppUpdateTrust.MaximumArtifactBytes,
                        120000,
                        false);

                var stage =
                    new AppUpdateStore(installRoot)
                        .StageCandidate(
                            remote.Bytes,
                            remote.SignatureText,
                            artifact);

                if (!stage.Success)
                {
                    return new AppUpdateRemoteResult
                    {
                        Success = false,
                        InstalledContext = true,
                        Status = stage.Status,
                        CurrentRelease =
                            stage.CurrentRelease,
                        CurrentVersion =
                            current.version,
                        CandidateRelease =
                            stage.CandidateRelease,
                        CandidateVersion =
                            remote.Manifest.version
                    };
                }

                return new AppUpdateRemoteResult
                {
                    Success = true,
                    InstalledContext = true,
                    UpdateAvailable =
                        stage.Changed,
                    Staged =
                        stage.Changed,
                    Status =
                        stage.Status,
                    CurrentRelease =
                        stage.CurrentRelease,
                    CurrentVersion =
                        current.version,
                    CandidateRelease =
                        stage.CandidateRelease,
                    CandidateVersion =
                        stage.CandidateVersion,
                    Title =
                        remote.Manifest.title,
                    Notes =
                        remote.Manifest.notes,
                    CandidateAppRoot =
                        stage.CandidateAppRoot,
                    StageRoot =
                        stage.StageRoot
                };
            }
            catch (WebException)
            {
                return Failure(
                    "UPDATE_NETWORK_UNAVAILABLE",
                    current);
            }
            catch (InvalidDataException ex)
            {
                return Failure(
                    SafeStatus(ex.Message),
                    current);
            }
            catch
            {
                return Failure(
                    "UPDATE_DOWNLOAD_FAILED",
                    current);
            }
        }

        public static AppUpdateActivationStartResult
            StartActivation(
                string baseDir,
                string candidateAppRoot)
        {
            string installRoot;
            if (!TryGetInstalledContext(
                baseDir,
                out installRoot))
            {
                return ActivationFailure(
                    "UPDATE_NOT_INSTALLED_CONTEXT");
            }

            if (string.IsNullOrWhiteSpace(
                candidateAppRoot)
                || !Directory.Exists(
                    candidateAppRoot))
            {
                return ActivationFailure(
                    "UPDATE_CANDIDATE_MISSING");
            }

            if (Process.GetProcessesByName(
                "claude").Length > 0)
            {
                return ActivationFailure(
                    "UPDATE_DEFERRED_CLAUDE_RUNNING");
            }

            if (Directory.Exists(
                Path.Combine(
                    installRoot,
                    "transition",
                    "pending")))
            {
                return ActivationFailure(
                    "UPDATE_DEFERRED_RECOVERY_PENDING");
            }

            var stagedRoot =
                Path.GetFullPath(
                    Path.Combine(
                        installRoot,
                        "updates",
                        "staged"))
                    .TrimEnd(
                        Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            var candidate =
                Path.GetFullPath(
                    candidateAppRoot)
                    .TrimEnd(
                        Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(
                stagedRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                return ActivationFailure(
                    "UPDATE_CANDIDATE_OUTSIDE_STAGING");
            }

            string updater;
            try
            {
                updater =
                    PrepareUpdaterHelper(
                        baseDir,
                        installRoot);
            }
            catch
            {
                return ActivationFailure(
                    "UPDATE_HELPER_PREPARE_FAILED");
            }

            try
            {
                var process =
                    Process.Start(
                        new ProcessStartInfo
                        {
                            FileName = updater,
                            Arguments =
                                "--install-root "
                                + Quote(installRoot)
                                + " --candidate-app-root "
                                + Quote(candidateAppRoot)
                                + " --parent-pid "
                                + Process.GetCurrentProcess().Id
                                + " --launch",
                            WorkingDirectory =
                                Path.GetDirectoryName(
                                    updater),
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });

                if (process == null)
                {
                    return ActivationFailure(
                        "UPDATE_HELPER_START_FAILED");
                }

                process.Dispose();

                return new AppUpdateActivationStartResult
                {
                    Started = true,
                    Status =
                        "UPDATE_ACTIVATION_STARTED",
                    UpdaterPath =
                        updater
                };
            }
            catch
            {
                return ActivationFailure(
                    "UPDATE_HELPER_START_FAILED");
            }
        }

        internal static string PrepareUpdaterHelper(
            string baseDir,
            string installRoot)
        {
            var source =
                Path.Combine(
                    Path.GetFullPath(baseDir),
                    "WRN-AI-Gateway-Updater.exe");

            if (!File.Exists(source))
                throw new FileNotFoundException(
                    "Updater helper missing.",
                    source);

            var destinationRoot =
                Path.Combine(
                    Path.GetFullPath(installRoot),
                    "updater");

            Directory.CreateDirectory(
                destinationRoot);

            var destination =
                Path.Combine(
                    destinationRoot,
                    "WRN-AI-Gateway-Updater.exe");

            var temp =
                destination
                + ".tmp-"
                + Guid.NewGuid().ToString("N");

            File.Copy(
                source,
                temp,
                false);

            try
            {
                var sourceHash =
                    Sha256File(source);
                var tempHash =
                    Sha256File(temp);

                if (!string.Equals(
                    sourceHash,
                    tempHash,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "UPDATE_HELPER_HASH_MISMATCH");
                }

                if (File.Exists(destination))
                    File.Delete(destination);

                File.Move(
                    temp,
                    destination);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            return destination;
        }

        private static VerifiedRemoteManifest
            FetchVerifiedManifest()
        {
            string lastError = null;

            for (var attempt = 0;
                attempt < 2;
                attempt++)
            {
                var nonce =
                    DateTime.UtcNow.Ticks
                    .ToString();

                try
                {
                    var manifestBytes =
                        DownloadBytesWithRetry(
                            AddCacheBuster(
                                AppUpdateTrust
                                    .RemoteManifestUrl,
                                nonce),
                            1024 * 1024,
                            15000,
                            false);

                    var signatureBytes =
                        DownloadBytesWithRetry(
                            AddCacheBuster(
                                AppUpdateTrust
                                    .RemoteSignatureUrl,
                                nonce),
                            64 * 1024,
                            15000,
                            false);

                    var signatureText =
                        Encoding.ASCII.GetString(
                            signatureBytes)
                            .Trim();

                    AppReleaseManifest manifest;
                    string error;
                    if (AppUpdateVerifier
                        .TryVerifyManifest(
                            manifestBytes,
                            signatureText,
                            out manifest,
                            out error))
                    {
                        return new VerifiedRemoteManifest
                        {
                            Bytes =
                                manifestBytes,
                            SignatureText =
                                signatureText,
                            Manifest =
                                manifest
                        };
                    }

                    lastError =
                        error
                        ?? "UPDATE_MANIFEST_INVALID";
                }
                catch (WebException)
                {
                    if (attempt > 0)
                        throw;
                    Thread.Sleep(250);
                    continue;
                }

                if (attempt == 0)
                {
                    Thread.Sleep(250);
                    continue;
                }
            }

            throw new InvalidDataException(
                lastError
                ?? "UPDATE_MANIFEST_INVALID");
        }

        internal static byte[]
            DownloadBytesWithRetry(
                string url,
                int maximumBytes,
                int timeoutMilliseconds,
                bool cacheBust)
        {
            Exception last = null;

            for (var attempt = 0;
                attempt < 2;
                attempt++)
            {
                try
                {
                    return DownloadBytesOnce(
                        cacheBust
                            ? AddCacheBuster(
                                url,
                                DateTime.UtcNow.Ticks
                                    .ToString())
                            : url,
                        maximumBytes,
                        timeoutMilliseconds);
                }
                catch (WebException ex)
                {
                    last = ex;
                    if (!IsTransient(ex)
                        || attempt > 0)
                    {
                        throw;
                    }

                    Thread.Sleep(250);
                }
            }

            throw last
                ?? new WebException(
                    "Update download failed.");
        }

        private static byte[] DownloadBytesOnce(
            string url,
            int maximumBytes,
            int timeoutMilliseconds)
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue =
                false;

            var request =
                (HttpWebRequest)WebRequest.Create(
                    url);

            request.Method = "GET";
            request.Timeout =
                timeoutMilliseconds;
            request.ReadWriteTimeout =
                timeoutMilliseconds;
            request.UserAgent =
                "WRN-AI-Gateway/0.5";
            request.AutomaticDecompression =
                DecompressionMethods.GZip
                | DecompressionMethods.Deflate;

            using (var response =
                (HttpWebResponse)
                    request.GetResponse())
            {
                if (response.StatusCode
                    != HttpStatusCode.OK)
                {
                    throw new WebException(
                        "Update endpoint returned "
                        + (int)response.StatusCode);
                }

                if (response.ContentLength
                    > maximumBytes)
                {
                    throw new InvalidDataException(
                        "UPDATE_DOWNLOAD_TOO_LARGE");
                }

                using (var input =
                    response.GetResponseStream())
                using (var output =
                    new MemoryStream())
                {
                    var buffer =
                        new byte[16384];
                    int total = 0;
                    int count;

                    while ((count =
                        input.Read(
                            buffer,
                            0,
                            buffer.Length)) > 0)
                    {
                        total += count;
                        if (total > maximumBytes)
                        {
                            throw new InvalidDataException(
                                "UPDATE_DOWNLOAD_TOO_LARGE");
                        }

                        output.Write(
                            buffer,
                            0,
                            count);
                    }

                    return output.ToArray();
                }
            }
        }

        private static AppReleaseIdentity
            ReadCurrentIdentity(
                string baseDir)
        {
            AppReleaseIdentity identity;
            string error;

            if (AppReleaseIdentityStore.TryRead(
                baseDir,
                out identity,
                out error))
            {
                return identity;
            }

            return new AppReleaseIdentity
            {
                schemaVersion = 1,
                release = 0,
                version = "legacy"
            };
        }

        private static bool IsTransient(
            WebException ex)
        {
            var response =
                ex.Response as HttpWebResponse;

            if (response == null)
                return true;

            var code =
                (int)response.StatusCode;

            return code == 408
                || code == 500
                || code == 502
                || code == 503
                || code == 504;
        }

        private static string AddCacheBuster(
            string url,
            string value)
        {
            return url
                + (url.Contains("?")
                    ? "&"
                    : "?")
                + "wrn_cache="
                + Uri.EscapeDataString(
                    value ?? string.Empty);
        }

        private static string Sha256File(
            string path)
        {
            using (var input =
                File.OpenRead(path))
            using (var sha =
                SHA256.Create())
            {
                return BitConverter.ToString(
                    sha.ComputeHash(input))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string Quote(
            string value)
        {
            return """
                + (value ?? string.Empty)
                    .Replace(""", "\"")
                + """;
        }

        private static string SafeStatus(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "UPDATE_FAILED";

            var safe =
                value.Replace("\r", " ")
                    .Replace("\n", " ");

            return safe.Length > 96
                ? safe.Substring(0, 96)
                : safe;
        }

        private static AppUpdateRemoteResult Failure(
            string status,
            AppReleaseIdentity current)
        {
            return new AppUpdateRemoteResult
            {
                Success = false,
                InstalledContext = true,
                UpdateAvailable = false,
                Staged = false,
                Status =
                    string.IsNullOrWhiteSpace(
                        status)
                        ? "UPDATE_FAILED"
                        : status,
                CurrentRelease =
                    current == null
                        ? 0
                        : current.release,
                CurrentVersion =
                    current == null
                        ? null
                        : current.version
            };
        }

        private static AppUpdateActivationStartResult
            ActivationFailure(
                string status)
        {
            return new AppUpdateActivationStartResult
            {
                Started = false,
                Status = status
            };
        }
    }
}
