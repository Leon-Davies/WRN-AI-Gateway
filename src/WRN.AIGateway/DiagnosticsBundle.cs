using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WRN.AIGateway
{
    internal sealed class DiagnosticsBundleResult
    {
        public bool Success { get; set; }
        public string Status { get; set; }
        public string Path { get; set; }
    }

    internal sealed class DiagnosticsDocument
    {
        public int schemaVersion { get; set; }
        public string createdAtUtc { get; set; }
        public string osVersion { get; set; }
        public string appVersion { get; set; }
        public int appRelease { get; set; }
        public string claudeInstallKind { get; set; }
        public string claudeMode { get; set; }
        public bool claudeRunning { get; set; }
        public int claudeProcessCount { get; set; }
        public bool managedPackageMarkerFound { get; set; }
        public bool claudeConfigReadable { get; set; }
        public bool desktopConfigExists { get; set; }
        public bool metaExists { get; set; }
        public bool wrnProfileExists { get; set; }
        public int catalogueRelease { get; set; }
        public string catalogueSource { get; set; }
        public string catalogueStatus { get; set; }
        public bool credentialConfigured { get; set; }
        public bool credentialDecryptable { get; set; }
        public bool credentialValidationMetadataPresent { get; set; }
        public bool gatewayConfigured { get; set; }
        public int gatewayPort { get; set; }
        public int gatewayProcessCount { get; set; }
        public bool transitionRecoveryPending { get; set; }
        public bool appUpdateStaged { get; set; }
        public string privacyNote { get; set; }
    }

    internal static class DiagnosticsBundle
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer
            {
                MaxJsonLength = 2 * 1024 * 1024,
                RecursionLimit = 64
            };

        private static readonly Regex SecretPattern =
            new Regex(
                @"(?i)(sk-or-[A-Za-z0-9_-]{8,}|bearer\s+[A-Za-z0-9._-]+|api[_ -]?key\s*[:=]\s*\S+)",
                RegexOptions.Compiled);

        public static DiagnosticsBundleResult Create(
            string baseDir,
            string stateRoot)
        {
            var outputRoot =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    "WRN AI Gateway",
                    "Diagnostics");

            return Create(
                baseDir,
                stateRoot,
                outputRoot);
        }

        internal static DiagnosticsBundleResult Create(
            string baseDir,
            string stateRoot,
            string outputRoot)
        {
            try
            {
                Directory.CreateDirectory(outputRoot);

                var stamp =
                    DateTime.Now.ToString(
                        "yyyyMMdd-HHmmss");

                var finalPath =
                    Path.Combine(
                        outputRoot,
                        "WRN-AI-Gateway-Diagnostics-"
                        + stamp
                        + ".zip");

                if (File.Exists(finalPath))
                    File.Delete(finalPath);

                var document =
                    BuildDocument(
                        baseDir,
                        stateRoot);

                using (var archive =
                    ZipFile.Open(
                        finalPath,
                        ZipArchiveMode.Create))
                {
                    WriteTextEntry(
                        archive,
                        "diagnostics.json",
                        Json.Serialize(document));

                    AddSafeGatewayLog(
                        archive,
                        stateRoot);

                    AddSafeUpdateAudit(
                        archive,
                        stateRoot);
                }

                return new DiagnosticsBundleResult
                {
                    Success = true,
                    Status =
                        "DIAGNOSTICS_CREATED",
                    Path = finalPath
                };
            }
            catch
            {
                return new DiagnosticsBundleResult
                {
                    Success = false,
                    Status =
                        "DIAGNOSTICS_CREATE_FAILED"
                };
            }
        }

        private static DiagnosticsDocument BuildDocument(
            string baseDir,
            string stateRoot)
        {
            var discovery =
                ClaudeDiscovery.Inspect();

            CatalogueLoadResult catalogue = null;
            try
            {
                catalogue =
                    new CatalogueStore(
                        baseDir,
                        Path.Combine(
                            stateRoot,
                            "catalogue"))
                        .LoadBestAvailable();
            }
            catch
            {
            }

            CredentialStatus credential = null;
            try
            {
                credential =
                    OpenRouterCredentialStore
                        .Inspect(stateRoot);
            }
            catch
            {
            }

            AppReleaseIdentity identity = null;
            string identityError;
            AppReleaseIdentityStore.TryRead(
                baseDir,
                out identity,
                out identityError);

            return new DiagnosticsDocument
            {
                schemaVersion = 1,
                createdAtUtc =
                    DateTimeOffset.UtcNow.ToString("o"),
                osVersion =
                    Environment.OSVersion.VersionString,
                appVersion =
                    identity == null
                        ? "development"
                        : identity.version,
                appRelease =
                    identity == null
                        ? 0
                        : identity.release,
                claudeInstallKind =
                    discovery.InstallKind.ToString(),
                claudeMode =
                    discovery.Mode.ToString(),
                claudeRunning =
                    discovery.ClaudeRunning,
                claudeProcessCount =
                    discovery.ClaudeProcessCount,
                managedPackageMarkerFound =
                    discovery.ManagedPackageMarkerFound,
                claudeConfigReadable =
                    discovery.ConfigReadable,
                desktopConfigExists =
                    discovery.DesktopConfigExists,
                metaExists =
                    discovery.MetaExists,
                wrnProfileExists =
                    discovery.WrnProfileExists,
                catalogueRelease =
                    catalogue == null
                        || catalogue.Catalogue == null
                        ? 0
                        : catalogue.Catalogue.release,
                catalogueSource =
                    catalogue == null
                        ? "unavailable"
                        : catalogue.Source,
                catalogueStatus =
                    catalogue == null
                        ? "CATALOGUE_UNAVAILABLE"
                        : catalogue.Status,
                credentialConfigured =
                    credential != null
                    && credential.Configured,
                credentialDecryptable =
                    credential != null
                    && credential.Decryptable,
                credentialValidationMetadataPresent =
                    credential != null
                    && credential.ValidationMetadataPresent,
                gatewayConfigured =
                    credential != null
                    && credential.GatewayConfigured,
                gatewayPort =
                    credential == null
                        ? 0
                        : credential.GatewayPort,
                gatewayProcessCount =
                    Process.GetProcessesByName(
                        "WRN-AI-Gateway-Gateway")
                        .Length,
                transitionRecoveryPending =
                    Directory.Exists(
                        Path.Combine(
                            stateRoot,
                            "transition",
                            "pending")),
                appUpdateStaged =
                    HasStagedUpdate(stateRoot),
                privacyNote =
                    "Contains WRN operational metadata only. "
                    + "No OpenRouter credential, Claude chat/history, "
                    + "prompt text, file contents, cookies, or Claude databases are collected."
            };
        }

        private static bool HasStagedUpdate(
            string stateRoot)
        {
            var root =
                Path.Combine(
                    stateRoot,
                    "updates",
                    "staged");

            if (!Directory.Exists(root))
                return false;

            try
            {
                return Directory
                    .GetDirectories(root)
                    .Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void AddSafeGatewayLog(
            ZipArchive archive,
            string stateRoot)
        {
            AddSafeTail(
                archive,
                Path.Combine(
                    stateRoot,
                    "gateway",
                    "gateway.log"),
                "gateway.log",
                300);
        }

        private static void AddSafeUpdateAudit(
            ZipArchive archive,
            string stateRoot)
        {
            AddSafeTail(
                archive,
                Path.Combine(
                    stateRoot,
                    "updates",
                    "last-activation.json"),
                "update-last-activation.json",
                100);
        }

        private static void AddSafeTail(
            ZipArchive archive,
            string sourcePath,
            string entryName,
            int maximumLines)
        {
            if (!File.Exists(sourcePath))
                return;

            string[] lines;
            try
            {
                lines =
                    File.ReadAllLines(
                        sourcePath,
                        Encoding.UTF8);
            }
            catch
            {
                return;
            }

            var start =
                Math.Max(
                    0,
                    lines.Length
                    - maximumLines);

            var safe =
                lines
                    .Skip(start)
                    .Select(SanitizeLine)
                    .ToArray();

            WriteTextEntry(
                archive,
                entryName,
                string.Join(
                    Environment.NewLine,
                    safe));
        }

        internal static string SanitizeLine(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return SecretPattern.Replace(
                value,
                "[redacted]");
        }

        private static void WriteTextEntry(
            ZipArchive archive,
            string name,
            string text)
        {
            var entry =
                archive.CreateEntry(
                    name,
                    CompressionLevel.Optimal);

            using (var stream =
                entry.Open())
            using (var writer =
                new StreamWriter(
                    stream,
                    new UTF8Encoding(false)))
            {
                writer.Write(
                    text
                    ?? string.Empty);
            }
        }
    }
}
