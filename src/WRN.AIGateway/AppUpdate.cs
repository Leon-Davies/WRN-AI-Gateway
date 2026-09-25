using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;

namespace WRN.AIGateway
{
    internal sealed class AppReleaseManifest
    {
        public int schemaVersion { get; set; }
        public int release { get; set; }
        public string version { get; set; }
        public string publishedAt { get; set; }
        public string artifactUrl { get; set; }
        public string artifactSha256 { get; set; }
        public long artifactSize { get; set; }
        public string title { get; set; }
        public string notes { get; set; }
    }

    internal sealed class AppReleaseIdentity
    {
        public int schemaVersion { get; set; }
        public int release { get; set; }
        public string version { get; set; }
    }

    internal sealed class AppUpdateStageResult
    {
        public bool Success { get; set; }
        public bool Changed { get; set; }
        public string Status { get; set; }
        public int CurrentRelease { get; set; }
        public int CandidateRelease { get; set; }
        public string CandidateVersion { get; set; }
        public string StageRoot { get; set; }
        public string CandidateAppRoot { get; set; }
        public string ManifestSha256 { get; set; }
    }

    internal static class AppUpdateTrust
    {
        public const int SupportedSchemaVersion = 1;
        public const int MaximumArtifactBytes =
            100 * 1024 * 1024;

        // Dedicated application-release key. The matching private key is
        // CurrentUser-DPAPI protected in the maintainer environment only.
        public const string PublicKeyXml =
            "<RSAKeyValue><Modulus>4+Fv/wiwHvcXKtbkCG78G+MRjrv+fKuJDQsUwcI4Bg2iUmXh2i/EVLk4Yc7dHweaD9NH6LU2ab9jRGODvgFnhJawhwlnG9+IRH4S/y+zCKQ+iX4GdPFNLsNmYX5dvjXGx4kmDo57tRwJmF9aAFq9y3DzAccCFrbDoXY0kzykJyoNg4zujiPXnRF2Tfg6xodAaAjvq+Y6yudfRmCCCEjl34xsWkoY2RHtbZwB+Q3RpEoY0mN1hR1q+2NOIAhT0f2TEp7DpbRNwufi5o9lN2C8NXhswTlZY/8O8IOEi4Sv77XLyMeXNoRpPRTA/Q5wGwt1anfh751cZlPWrwoRxgQT219V+0QutPWYtC42KhZ6csUH8mNosEF+Peoc3UbzMP+UN9JDoibrmyH8vNCrURPKEp0YPKUJLcYtpp3tridGPP2uke/5mS82H4MQw1yJyeNP2jh7qtLwIlluSXhcCEgw0zYuhKLdpQ+AeTUKY6nrVl9xyQVJs4pwng+8+5TPmqPh</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        // Beta transport. Signature verification remains authoritative;
        // update semantics do not depend on GitHub.
        public const string RemoteManifestUrl =
            "https://raw.githubusercontent.com/Leon-Davies/WRN-AI-Gateway/app-update-beta/updates/release.json";
        public const string RemoteSignatureUrl =
            "https://raw.githubusercontent.com/Leon-Davies/WRN-AI-Gateway/app-update-beta/updates/release.sig";
    }

    internal static class AppUpdateVerifier
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer
            {
                MaxJsonLength = 4 * 1024 * 1024,
                RecursionLimit = 128
            };

        public static bool TryVerifyManifest(
            byte[] bytes,
            string signatureText,
            out AppReleaseManifest manifest,
            out string error)
        {
            return TryVerifyManifest(
                bytes,
                signatureText,
                AppUpdateTrust.PublicKeyXml,
                out manifest,
                out error);
        }

        internal static bool TryVerifyManifest(
            byte[] bytes,
            string signatureText,
            string publicKeyXml,
            out AppReleaseManifest manifest,
            out string error)
        {
            manifest = null;
            error = null;

            if (bytes == null
                || bytes.Length == 0
                || bytes.Length > 1024 * 1024)
            {
                error = "UPDATE_MANIFEST_EMPTY_OR_TOO_LARGE";
                return false;
            }

            byte[] signature = null;
            try
            {
                signature =
                    Convert.FromBase64String(
                        (signatureText
                            ?? string.Empty).Trim());
            }
            catch
            {
                error =
                    "UPDATE_MANIFEST_SIGNATURE_FORMAT_INVALID";
                return false;
            }

            try
            {
                using (var rsa =
                    new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(publicKeyXml);
                    if (!rsa.VerifyData(
                        bytes,
                        CryptoConfig.MapNameToOID(
                            "SHA256"),
                        signature))
                    {
                        error =
                            "UPDATE_MANIFEST_SIGNATURE_INVALID";
                        return false;
                    }
                }
            }
            catch
            {
                error =
                    "UPDATE_MANIFEST_SIGNATURE_CHECK_FAILED";
                return false;
            }
            finally
            {
                if (signature != null)
                    Array.Clear(
                        signature,
                        0,
                        signature.Length);
            }

            try
            {
                manifest =
                    Json.Deserialize<AppReleaseManifest>(
                        Encoding.UTF8.GetString(bytes));
            }
            catch
            {
                error = "UPDATE_MANIFEST_JSON_INVALID";
                return false;
            }

            return ValidateManifest(
                manifest,
                out error);
        }

        public static bool ValidateManifest(
            AppReleaseManifest manifest,
            out string error)
        {
            error = null;

            if (manifest == null)
                return Fail(
                    "UPDATE_MANIFEST_NULL",
                    out error);

            if (manifest.schemaVersion
                != AppUpdateTrust.SupportedSchemaVersion)
            {
                return Fail(
                    "UPDATE_MANIFEST_SCHEMA_UNSUPPORTED",
                    out error);
            }

            if (manifest.release < 1)
                return Fail(
                    "UPDATE_RELEASE_INVALID",
                    out error);

            if (string.IsNullOrWhiteSpace(
                manifest.version)
                || manifest.version.Length > 64)
            {
                return Fail(
                    "UPDATE_VERSION_INVALID",
                    out error);
            }

            DateTimeOffset published;
            if (string.IsNullOrWhiteSpace(
                manifest.publishedAt)
                || !DateTimeOffset.TryParse(
                    manifest.publishedAt,
                    out published))
            {
                return Fail(
                    "UPDATE_PUBLISHED_AT_INVALID",
                    out error);
            }

            Uri artifact;
            if (string.IsNullOrWhiteSpace(
                manifest.artifactUrl)
                || !Uri.TryCreate(
                    manifest.artifactUrl,
                    UriKind.Absolute,
                    out artifact)
                || artifact.Scheme != Uri.UriSchemeHttps)
            {
                return Fail(
                    "UPDATE_ARTIFACT_URL_INVALID",
                    out error);
            }

            if (!IsSha256(
                manifest.artifactSha256))
            {
                return Fail(
                    "UPDATE_ARTIFACT_HASH_INVALID",
                    out error);
            }

            if (manifest.artifactSize < 1
                || manifest.artifactSize
                    > AppUpdateTrust.MaximumArtifactBytes)
            {
                return Fail(
                    "UPDATE_ARTIFACT_SIZE_INVALID",
                    out error);
            }

            if ((manifest.title ?? string.Empty).Length
                    > 160
                || (manifest.notes ?? string.Empty).Length
                    > 4000)
            {
                return Fail(
                    "UPDATE_CHANGELOG_TOO_LARGE",
                    out error);
            }

            return true;
        }

        public static string Sha256Hex(
            byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return string.Concat(
                    sha.ComputeHash(
                        bytes
                            ?? new byte[0])
                        .Select(
                            delegate(byte value)
                            {
                                return value.ToString("x2");
                            }));
            }
        }

        private static bool IsSha256(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length != 64)
                return false;

            foreach (var ch in value)
            {
                if (!((ch >= '0' && ch <= '9')
                    || (ch >= 'a' && ch <= 'f')
                    || (ch >= 'A' && ch <= 'F')))
                    return false;
            }

            return true;
        }

        private static bool Fail(
            string value,
            out string error)
        {
            error = value;
            return false;
        }
    }

    internal static class AppReleaseIdentityStore
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer();

        public static bool TryRead(
            string appRoot,
            out AppReleaseIdentity identity,
            out string error)
        {
            identity = null;
            error = null;

            var path = Path.Combine(
                appRoot,
                "app-release.json");

            if (!File.Exists(path))
            {
                error =
                    "APP_RELEASE_IDENTITY_MISSING";
                return false;
            }

            try
            {
                identity =
                    Json.Deserialize<AppReleaseIdentity>(
                        File.ReadAllText(
                            path,
                            Encoding.UTF8));
            }
            catch
            {
                error =
                    "APP_RELEASE_IDENTITY_INVALID";
                return false;
            }

            if (identity == null
                || identity.schemaVersion != 1
                || identity.release < 0
                || string.IsNullOrWhiteSpace(
                    identity.version)
                || identity.version.Length > 64)
            {
                identity = null;
                error =
                    "APP_RELEASE_IDENTITY_INVALID";
                return false;
            }

            return true;
        }

        public static int ReadReleaseOrLegacy(
            string appRoot)
        {
            AppReleaseIdentity identity;
            string error;
            return TryRead(
                appRoot,
                out identity,
                out error)
                ? identity.release
                : 0;
        }
    }

    internal static class AppSelfCheck
    {
        public static bool ValidateDirectory(
            string appRoot,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(appRoot)
                || !Directory.Exists(appRoot))
            {
                error = "SELF_CHECK_APP_ROOT_MISSING";
                return false;
            }

            foreach (var relative in new[]
            {
                "WRN-AI-Gateway.exe",
                "WRN-AI-Gateway-Gateway.exe",
                "WRN-AI-Gateway-Updater.exe",
                @"ui\MainWindow.xaml",
                @"catalogue\catalogue.json",
                @"catalogue\catalogue.sig",
                "app-release.json"
            })
            {
                var path = Path.Combine(
                    appRoot,
                    relative);

                if (!File.Exists(path)
                    || new FileInfo(path).Length < 1)
                {
                    error =
                        "SELF_CHECK_REQUIRED_FILE_MISSING:"
                        + relative;
                    return false;
                }
            }

            AppReleaseIdentity identity;
            if (!AppReleaseIdentityStore.TryRead(
                appRoot,
                out identity,
                out error))
            {
                return false;
            }

            try
            {
                var document = new XmlDocument();
                document.Load(
                    Path.Combine(
                        appRoot,
                        "ui",
                        "MainWindow.xaml"));
            }
            catch
            {
                error =
                    "SELF_CHECK_XAML_INVALID";
                return false;
            }

            try
            {
                CatalogueDocument catalogue;
                string catalogueError;
                var catalogueBytes =
                    File.ReadAllBytes(
                        Path.Combine(
                            appRoot,
                            "catalogue",
                            "catalogue.json"));
                var signature =
                    File.ReadAllText(
                        Path.Combine(
                            appRoot,
                            "catalogue",
                            "catalogue.sig"),
                        Encoding.ASCII);

                if (!CatalogueVerifier.TryVerifyAndParse(
                    catalogueBytes,
                    signature,
                    out catalogue,
                    out catalogueError))
                {
                    error =
                        "SELF_CHECK_CATALOGUE_INVALID:"
                        + catalogueError;
                    return false;
                }
            }
            catch
            {
                error =
                    "SELF_CHECK_CATALOGUE_FAILED";
                return false;
            }

            return true;
        }
    }

    internal sealed class AppUpdateStore
    {
        private readonly string _installRoot;

        public AppUpdateStore(
            string installRoot)
        {
            if (string.IsNullOrWhiteSpace(
                installRoot))
            {
                throw new ArgumentNullException(
                    "installRoot");
            }

            _installRoot =
                Path.GetFullPath(installRoot);
        }

        public AppUpdateStageResult StageCandidate(
            byte[] manifestBytes,
            string signatureText,
            byte[] artifactBytes)
        {
            return StageCandidate(
                manifestBytes,
                signatureText,
                artifactBytes,
                AppUpdateTrust.PublicKeyXml);
        }

        internal AppUpdateStageResult StageCandidate(
            byte[] manifestBytes,
            string signatureText,
            byte[] artifactBytes,
            string publicKeyXml)
        {
            AppReleaseManifest manifest;
            string error;

            if (!AppUpdateVerifier.TryVerifyManifest(
                manifestBytes,
                signatureText,
                publicKeyXml,
                out manifest,
                out error))
            {
                return Fail(
                    error,
                    0,
                    0);
            }

            var currentRoot =
                Path.Combine(
                    _installRoot,
                    "current");
            var currentRelease =
                AppReleaseIdentityStore
                    .ReadReleaseOrLegacy(
                        currentRoot);

            if (manifest.release
                < currentRelease)
            {
                return Fail(
                    "UPDATE_ROLLBACK_REJECTED",
                    currentRelease,
                    manifest.release);
            }

            if (manifest.release
                == currentRelease)
            {
                return new AppUpdateStageResult
                {
                    Success = true,
                    Changed = false,
                    Status =
                        "UPDATE_ALREADY_CURRENT",
                    CurrentRelease =
                        currentRelease,
                    CandidateRelease =
                        manifest.release,
                    CandidateVersion =
                        manifest.version,
                    ManifestSha256 =
                        AppUpdateVerifier
                            .Sha256Hex(
                                manifestBytes)
                };
            }

            if (artifactBytes == null
                || artifactBytes.LongLength
                    != manifest.artifactSize)
            {
                return Fail(
                    "UPDATE_ARTIFACT_SIZE_MISMATCH",
                    currentRelease,
                    manifest.release);
            }

            var artifactHash =
                AppUpdateVerifier.Sha256Hex(
                    artifactBytes);

            if (!string.Equals(
                artifactHash,
                manifest.artifactSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    "UPDATE_ARTIFACT_HASH_MISMATCH",
                    currentRelease,
                    manifest.release);
            }

            var updateRoot =
                Path.Combine(
                    _installRoot,
                    "updates");

            var prepare =
                Path.Combine(
                    updateRoot,
                    "prepare-"
                    + Guid.NewGuid()
                        .ToString("N"));

            var appRoot =
                Path.Combine(
                    prepare,
                    "app");

            Directory.CreateDirectory(
                appRoot);

            try
            {
                SafeExtractZip(
                    artifactBytes,
                    appRoot);

                AppReleaseIdentity identity;
                if (!AppReleaseIdentityStore.TryRead(
                    appRoot,
                    out identity,
                    out error))
                {
                    return FailAndCleanup(
                        error,
                        currentRelease,
                        manifest.release,
                        prepare);
                }

                if (identity.release
                    != manifest.release
                    || !string.Equals(
                        identity.version,
                        manifest.version,
                        StringComparison.Ordinal))
                {
                    return FailAndCleanup(
                        "UPDATE_IDENTITY_MISMATCH",
                        currentRelease,
                        manifest.release,
                        prepare);
                }

                if (!AppSelfCheck.ValidateDirectory(
                    appRoot,
                    out error))
                {
                    return FailAndCleanup(
                        error,
                        currentRelease,
                        manifest.release,
                        prepare);
                }

                File.WriteAllBytes(
                    Path.Combine(
                        prepare,
                        "release.json"),
                    manifestBytes);
                File.WriteAllText(
                    Path.Combine(
                        prepare,
                        "release.sig"),
                    (signatureText
                        ?? string.Empty).Trim(),
                    Encoding.ASCII);

                var manifestHash =
                    AppUpdateVerifier
                        .Sha256Hex(
                            manifestBytes);

                var staged =
                    Path.Combine(
                        updateRoot,
                        "staged",
                        "release-"
                        + manifest.release
                            .ToString("D8")
                        + "-"
                        + manifestHash
                            .Substring(0, 12));

                Directory.CreateDirectory(
                    Path.GetDirectoryName(
                        staged));

                if (Directory.Exists(staged))
                    Directory.Delete(
                        staged,
                        true);

                Directory.Move(
                    prepare,
                    staged);

                return new AppUpdateStageResult
                {
                    Success = true,
                    Changed = true,
                    Status = "UPDATE_STAGED",
                    CurrentRelease =
                        currentRelease,
                    CandidateRelease =
                        manifest.release,
                    CandidateVersion =
                        manifest.version,
                    StageRoot =
                        staged,
                    CandidateAppRoot =
                        Path.Combine(
                            staged,
                            "app"),
                    ManifestSha256 =
                        manifestHash
                };
            }
            catch (InvalidDataException)
            {
                return FailAndCleanup(
                    "UPDATE_ARCHIVE_INVALID",
                    currentRelease,
                    manifest.release,
                    prepare);
            }
            catch (IOException)
            {
                return FailAndCleanup(
                    "UPDATE_STAGE_IO_FAILURE",
                    currentRelease,
                    manifest.release,
                    prepare);
            }
            catch
            {
                return FailAndCleanup(
                    "UPDATE_STAGE_FAILED",
                    currentRelease,
                    manifest.release,
                    prepare);
            }
        }

        private static void SafeExtractZip(
            byte[] bytes,
            string destination)
        {
            var root =
                Path.GetFullPath(
                    destination
                        .TrimEnd(
                            Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar);

            using (var memory =
                new MemoryStream(
                    bytes,
                    false))
            using (var archive =
                new ZipArchive(
                    memory,
                    ZipArchiveMode.Read,
                    false))
            {
                if (archive.Entries.Count < 1
                    || archive.Entries.Count > 500)
                {
                    throw new InvalidDataException(
                        "Archive entry count invalid.");
                }

                foreach (var entry
                    in archive.Entries)
                {
                    var relative =
                        (entry.FullName
                            ?? string.Empty)
                            .Replace(
                                '/',
                                Path.DirectorySeparatorChar);

                    if (string.IsNullOrWhiteSpace(
                        relative))
                        continue;

                    var target =
                        Path.GetFullPath(
                            Path.Combine(
                                root,
                                relative));

                    if (!target.StartsWith(
                        root,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "Archive path traversal rejected.");
                    }

                    if (string.IsNullOrEmpty(
                        entry.Name))
                    {
                        Directory.CreateDirectory(
                            target);
                        continue;
                    }

                    var parent =
                        Path.GetDirectoryName(
                            target);
                    if (!string.IsNullOrWhiteSpace(
                        parent))
                    {
                        Directory.CreateDirectory(
                            parent);
                    }

                    using (var input =
                        entry.Open())
                    using (var output =
                        new FileStream(
                            target,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None))
                    {
                        input.CopyTo(output);
                        output.Flush(true);
                    }
                }
            }
        }

        private static AppUpdateStageResult
            FailAndCleanup(
                string status,
                int currentRelease,
                int candidateRelease,
                string prepare)
        {
            try
            {
                if (Directory.Exists(
                    prepare))
                {
                    Directory.Delete(
                        prepare,
                        true);
                }
            }
            catch
            {
            }

            return Fail(
                status,
                currentRelease,
                candidateRelease);
        }

        private static AppUpdateStageResult Fail(
            string status,
            int currentRelease,
            int candidateRelease)
        {
            return new AppUpdateStageResult
            {
                Success = false,
                Changed = false,
                Status =
                    string.IsNullOrWhiteSpace(
                        status)
                        ? "UPDATE_FAILED"
                        : status,
                CurrentRelease =
                    currentRelease,
                CandidateRelease =
                    candidateRelease
            };
        }
    }
    internal sealed class AppSelfCheckReport
    {
        public bool ok { get; set; }
        public string error { get; set; }
        public int release { get; set; }
        public string version { get; set; }
    }

    internal static class AppUpdateCommand
    {
        public static bool TryRun(
            string[] args,
            string baseDir)
        {
            if (args == null
                || args.Length == 0)
                return false;

            var selfCheck = false;
            string reportPath = null;

            for (var i = 0;
                i < args.Length;
                i++)
            {
                if (string.Equals(
                    args[i],
                    "--self-check",
                    StringComparison.OrdinalIgnoreCase))
                {
                    selfCheck = true;
                }
                else if (string.Equals(
                    args[i],
                    "--self-check-report",
                    StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length)
                {
                    reportPath =
                        args[++i];
                }
            }

            if (!selfCheck)
                return false;

            string error;
            var ok =
                AppSelfCheck.ValidateDirectory(
                    baseDir,
                    out error);

            AppReleaseIdentity identity;
            string identityError;
            AppReleaseIdentityStore.TryRead(
                baseDir,
                out identity,
                out identityError);

            if (!string.IsNullOrWhiteSpace(
                reportPath))
            {
                var serializer =
                    new JavaScriptSerializer();
                var report =
                    new AppSelfCheckReport
                    {
                        ok = ok,
                        error = error,
                        release =
                            identity == null
                                ? 0
                                : identity.release,
                        version =
                            identity == null
                                ? null
                                : identity.version
                    };

                var full =
                    Path.GetFullPath(
                        reportPath);
                var parent =
                    Path.GetDirectoryName(
                        full);
                if (!string.IsNullOrWhiteSpace(
                    parent))
                {
                    Directory.CreateDirectory(
                        parent);
                }

                File.WriteAllText(
                    full,
                    serializer.Serialize(
                        report),
                    new UTF8Encoding(false));
            }

            Environment.ExitCode =
                ok ? 0 : 1;
            return true;
        }
    }

}
