using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WRN.AIGateway
{
    internal sealed class ModeGatewayConfig
    {
        public string LocalApiKey { get; set; }
        public int Port { get; set; }
    }

    internal sealed class ModePreflightReport
    {
        public string TargetMode { get; set; }
        public ClaudeDiscoverySnapshot Discovery { get; set; }
        public bool PendingTransitionPresent { get; set; }
        public bool OwnershipBaselinePresent { get; set; }
        public bool SignedCatalogueValid { get; set; }
        public int CatalogueRelease { get; set; }
        public string CatalogueSource { get; set; }
        public bool GatewayBinaryPresent { get; set; }
        public bool GatewayConfigPresent { get; set; }
        public bool GatewayConfigValid { get; set; }
        public int GatewayPort { get; set; }
        public bool OpenRouterCredentialPresent { get; set; }
        public bool OpenRouterCredentialDecryptable { get; set; }
        public bool GatewayHealthy { get; set; }
        public bool GatewayStartRequired { get; set; }
        public bool TransitionPlanCompiled { get; set; }
        public string TransitionPlanKind { get; set; }
        public string[] PlannedMutations { get; set; }
        public string[] PlannedPaths { get; set; }
        public bool PreflightCompatible { get; set; }
        public string PreflightBlockReason { get; set; }
        public bool LiveExecutionEnabled { get; set; }
        public bool LiveExecutionAllowed { get; set; }
        public string LiveExecutionBlockReason { get; set; }
    }

    internal static class ModeCoordinator
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 256
            };

        public static ModePreflightReport Inspect(
            string targetMode,
            string baseDir,
            string stateRoot,
            ClaudePaths paths,
            ClaudeDiscoverySnapshot discovery)
        {
            if (string.IsNullOrWhiteSpace(baseDir))
                throw new ArgumentNullException("baseDir");
            if (string.IsNullOrWhiteSpace(stateRoot))
                throw new ArgumentNullException("stateRoot");
            if (paths == null) throw new ArgumentNullException("paths");
            if (discovery == null) throw new ArgumentNullException("discovery");

            var target = NormalizeTarget(targetMode);
            var transitionRoot = Path.Combine(stateRoot, "transition");
            var report = new ModePreflightReport
            {
                TargetMode = target,
                Discovery = discovery,
                PendingTransitionPresent = Directory.Exists(
                    ClaudeTransitionState.PendingTransactionPath(
                        transitionRoot)),
                OwnershipBaselinePresent = File.Exists(
                    ClaudeTransitionState.OwnershipBaselinePath(
                        transitionRoot)),
                GatewayBinaryPresent = File.Exists(
                    Path.Combine(
                        baseDir,
                        "WRN-AI-Gateway-Gateway.exe")),
                PlannedMutations = new string[0],
                PlannedPaths = new string[0],
                LiveExecutionEnabled =
                    TransitionSafety.LiveClaudeWritesEnabled
            };

            if (report.PendingTransitionPresent)
                return Block(
                    report,
                    "A pending transition must be recovered before another mode plan is compiled.");

            var discoveryPlan = TransitionPlanner.Plan(
                discovery,
                target.ToLowerInvariant());
            if (!discoveryPlan.Allowed)
                return Block(
                    report,
                    discoveryPlan.BlockReason);

            CatalogueLoadResult catalogueLoad;
            try
            {
                catalogueLoad = new CatalogueStore(
                    baseDir,
                    Path.Combine(stateRoot, "catalogue"))
                    .LoadBestAvailable();
                report.SignedCatalogueValid = true;
                report.CatalogueRelease =
                    catalogueLoad.Catalogue.release;
                report.CatalogueSource =
                    catalogueLoad.Source;
            }
            catch
            {
                return Block(
                    report,
                    "No valid signed WRN model catalogue is available.");
            }

            ModeGatewayConfig gatewayConfig = null;
            string gatewayConfigError = null;
            ProbeGatewayConfig(
                stateRoot,
                out gatewayConfig,
                out gatewayConfigError);

            report.GatewayConfigPresent =
                File.Exists(
                    Path.Combine(
                        stateRoot,
                        "gateway",
                        "gateway.json"));
            report.GatewayConfigValid =
                gatewayConfig != null;
            report.GatewayPort =
                gatewayConfig == null ? 0 : gatewayConfig.Port;

            report.OpenRouterCredentialPresent =
                File.Exists(
                    Path.Combine(
                        stateRoot,
                        "credentials",
                        "openrouter.key.dpapi"));
            report.OpenRouterCredentialDecryptable =
                ProbeOpenRouterCredential(stateRoot);

            if (target == "WRN")
            {
                if (!report.GatewayBinaryPresent)
                    return Block(
                        report,
                        "The WRN loopback gateway binary is missing.");
                if (gatewayConfig == null)
                    return Block(
                        report,
                        gatewayConfigError
                            ?? "Gateway configuration is invalid.");
                if (!report.OpenRouterCredentialDecryptable)
                    return Block(
                        report,
                        "A usable CurrentUser-protected OpenRouter credential is not configured.");

                report.GatewayHealthy = ProbeGatewayHealth(
                    gatewayConfig.Port,
                    report.CatalogueRelease);
                report.GatewayStartRequired =
                    !report.GatewayHealthy;
            }

            ClaudeActivationPlan compiled;
            try
            {
                if (target == "WRN")
                {
                    compiled = ClaudeActivationCompiler.Compile(
                        discovery,
                        paths,
                        catalogueLoad.Catalogue,
                        gatewayConfig.LocalApiKey,
                        gatewayConfig.Port);
                }
                else
                {
                    compiled = ClaudeDeactivationCompiler.Compile(
                        discovery,
                        paths,
                        transitionRoot);
                }
            }
            catch (Exception ex)
            {
                return Block(
                    report,
                    "Transition plan could not be compiled: "
                    + SafeStatus(ex.Message));
            }

            report.TransitionPlanCompiled = true;
            report.TransitionPlanKind = compiled.Kind;

            var directoryMutations =
                compiled.DirectoryMutations
                ?? new TransitionDirectoryMutation[0];
            var directoryCreates = directoryMutations
                .Where(delegate(TransitionDirectoryMutation mutation)
                {
                    return mutation.DesiredExists;
                });
            var directoryDeletes = directoryMutations
                .Where(delegate(TransitionDirectoryMutation mutation)
                {
                    return !mutation.DesiredExists;
                });

            report.PlannedMutations = directoryCreates
                .Select(delegate(TransitionDirectoryMutation mutation)
                {
                    return mutation.Purpose;
                })
                .Concat(
                    compiled.Mutations.Select(
                        delegate(TransitionMutation mutation)
                        {
                            return mutation.Purpose;
                        }))
                .Concat(
                    directoryDeletes.Select(
                        delegate(TransitionDirectoryMutation mutation)
                        {
                            return mutation.Purpose;
                        }))
                .ToArray();
            report.PlannedPaths = directoryCreates
                .Select(delegate(TransitionDirectoryMutation mutation)
                {
                    return mutation.Path;
                })
                .Concat(
                    compiled.Mutations.Select(
                        delegate(TransitionMutation mutation)
                        {
                            return mutation.Path;
                        }))
                .Concat(
                    directoryDeletes.Select(
                        delegate(TransitionDirectoryMutation mutation)
                        {
                            return mutation.Path;
                        }))
                .ToArray();

            report.PreflightCompatible = true;
            report.PreflightBlockReason = null;
            report.LiveExecutionAllowed =
                report.PreflightCompatible
                && report.LiveExecutionEnabled;

            if (!report.LiveExecutionAllowed)
            {
                report.LiveExecutionBlockReason =
                    "Live Claude writes remain disabled pending clean managed-Claude qualification.";
            }

            return report;
        }

        public static TransitionExecutionResult RecoverBeforePlanning(
            string stateRoot)
        {
            if (string.IsNullOrWhiteSpace(stateRoot))
                throw new ArgumentNullException("stateRoot");

            return ClaudeTransitionExecutor.RecoverPending(
                Path.Combine(stateRoot, "transition"));
        }

        private static string NormalizeTarget(string targetMode)
        {
            if (string.Equals(
                targetMode,
                "wrn",
                StringComparison.OrdinalIgnoreCase))
                return "WRN";
            if (string.Equals(
                targetMode,
                "wtw",
                StringComparison.OrdinalIgnoreCase))
                return "WTW";

            throw new ArgumentException(
                "Target mode must be WRN or WTW.",
                "targetMode");
        }

        private static ModePreflightReport Block(
            ModePreflightReport report,
            string reason)
        {
            report.PreflightCompatible = false;
            report.PreflightBlockReason =
                string.IsNullOrWhiteSpace(reason)
                    ? "Preflight blocked."
                    : reason;
            report.LiveExecutionAllowed = false;
            report.LiveExecutionBlockReason =
                report.PreflightBlockReason;
            return report;
        }

        internal static void ProbeGatewayConfig(
            string stateRoot,
            out ModeGatewayConfig config,
            out string error)
        {
            config = null;
            error = null;

            var path = Path.Combine(
                stateRoot,
                "gateway",
                "gateway.json");
            if (!File.Exists(path))
            {
                error = "Gateway configuration is missing.";
                return;
            }

            try
            {
                var candidate =
                    Json.Deserialize<ModeGatewayConfig>(
                        File.ReadAllText(path, Encoding.UTF8));
                if (candidate == null
                    || string.IsNullOrWhiteSpace(
                        candidate.LocalApiKey)
                    || candidate.Port < 1024
                    || candidate.Port > 65535)
                {
                    error = "Gateway configuration is invalid.";
                    return;
                }

                config = candidate;
            }
            catch
            {
                error = "Gateway configuration is unreadable.";
            }
        }

        private static bool ProbeOpenRouterCredential(
            string stateRoot)
        {
            var path = Path.Combine(
                stateRoot,
                "credentials",
                "openrouter.key.dpapi");
            if (!File.Exists(path))
                return false;

            byte[] protectedBytes = null;
            byte[] plain = null;
            try
            {
                protectedBytes = Convert.FromBase64String(
                    File.ReadAllText(
                        path,
                        Encoding.ASCII).Trim());
                plain = ProtectedData.Unprotect(
                    protectedBytes,
                    null,
                    DataProtectionScope.CurrentUser);

                return plain != null
                    && plain.Length >= 16
                    && plain.Any(
                        delegate(byte value)
                        {
                            return value != 0
                                && !char.IsWhiteSpace(
                                    (char)value);
                        });
            }
            catch
            {
                return false;
            }
            finally
            {
                if (plain != null)
                    Array.Clear(plain, 0, plain.Length);
                if (protectedBytes != null)
                    Array.Clear(
                        protectedBytes,
                        0,
                        protectedBytes.Length);
            }
        }

        internal static bool ProbeGatewayHealth(
            int port,
            int expectedCatalogueRelease)
        {
            try
            {
                var request =
                    (HttpWebRequest)WebRequest.Create(
                        "http://127.0.0.1:"
                        + port
                        + "/health");
                request.Method = "GET";
                request.Timeout = 750;
                request.ReadWriteTimeout = 750;
                request.Proxy = null;

                using (var response =
                    (HttpWebResponse)request.GetResponse())
                using (var reader =
                    new StreamReader(
                        response.GetResponseStream(),
                        Encoding.UTF8))
                {
                    if (response.StatusCode
                        != HttpStatusCode.OK)
                        return false;

                    var body = reader.ReadToEnd();
                    var parsed =
                        Json.DeserializeObject(body)
                        as Dictionary<string, object>;
                    if (parsed == null)
                        return false;

                    object ok;
                    object release;
                    return parsed.TryGetValue("ok", out ok)
                        && Convert.ToBoolean(ok)
                        && parsed.TryGetValue(
                            "catalogueRelease",
                            out release)
                        && Convert.ToInt32(release)
                            == expectedCatalogueRelease;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string SafeStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";
            var safe = value
                .Replace("\r", " ")
                .Replace("\n", " ");
            return safe.Length > 160
                ? safe.Substring(0, 160)
                : safe;
        }
    }

    internal static class ModeCoordinatorCommand
    {
        public static bool TryRun(
            string[] args,
            string baseDir)
        {
            if (args == null || args.Length == 0)
                return false;

            string target = null;
            string reportPath = null;

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(
                    args[i],
                    "--mode-preflight-report",
                    StringComparison.OrdinalIgnoreCase)
                    && i + 2 < args.Length)
                {
                    target = args[i + 1];
                    reportPath = args[i + 2];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(reportPath))
                return false;

            var stateRoot = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "WRN-AI-Gateway");
            var report = ModeCoordinator.Inspect(
                target,
                baseDir,
                stateRoot,
                ClaudePaths.Current(),
                ClaudeDiscovery.Inspect());

            var json = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 256
            }.Serialize(report);

            var full = Path.GetFullPath(reportPath);
            var parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            File.WriteAllText(
                full,
                json,
                new UTF8Encoding(false));
            return true;
        }
    }
}