using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WRN.AIGateway
{
    internal sealed class TransitionFileSnapshot
    {
        public string Path { get; set; }
        public bool Exists { get; set; }
        public byte[] Bytes { get; set; }
        public string Sha256 { get; set; }
    }

    internal sealed class ClaudeConfigBaseline
    {
        public TransitionFileSnapshot DesktopConfig { get; set; }
        public TransitionFileSnapshot Meta { get; set; }
        public TransitionFileSnapshot WrnProfile { get; set; }

        public static ClaudeConfigBaseline Capture(ClaudePaths paths)
        {
            if (paths == null) throw new ArgumentNullException("paths");

            return new ClaudeConfigBaseline
            {
                DesktopConfig = CaptureFile(paths.DesktopConfigPath),
                Meta = CaptureFile(paths.MetaPath),
                WrnProfile = CaptureFile(paths.WrnProfilePath)
            };
        }

        private static TransitionFileSnapshot CaptureFile(string path)
        {
            var exists = File.Exists(path);
            var bytes = exists ? File.ReadAllBytes(path) : null;
            return new TransitionFileSnapshot
            {
                Path = path,
                Exists = exists,
                Bytes = bytes,
                Sha256 = exists ? TransitionHash.Sha256(bytes) : null
            };
        }
    }

    internal sealed class ClaudeOwnershipBaseline
    {
        public int SchemaVersion { get; set; }
        public string CreatedAtUtc { get; set; }
        public int ActivationCatalogueRelease { get; set; }
        public bool DesktopDeploymentModeExisted { get; set; }
        public object DesktopDeploymentModeValue { get; set; }
        public bool MetaAppliedIdExisted { get; set; }
        public object MetaAppliedIdValue { get; set; }
        public string WrnProfileSha256 { get; set; }
    }

    internal sealed class TransitionMutation
    {
        public string Path { get; set; }
        public bool ExpectedExists { get; set; }
        public string ExpectedSha256 { get; set; }
        public bool DesiredExists { get; set; }
        public byte[] DesiredBytes { get; set; }
        public string DesiredSha256 { get; set; }
        public string Purpose { get; set; }
    }

    internal sealed class ClaudeActivationPlan
    {
        public string Kind { get; set; }
        public int CatalogueRelease { get; set; }
        public string DefaultModelKey { get; set; }
        public TransitionMutation[] Mutations { get; set; }
        public byte[] OwnershipBaselineBytes { get; set; }
        public string OwnershipBaselineExpectedSha256 { get; set; }
        public bool RemoveOwnershipBaselineOnSuccess { get; set; }
        public string[] AllowlistedPaths { get; set; }
        public string[] ExplicitNonGoals { get; set; }
    }

    internal sealed class TransitionExecutionResult
    {
        public bool Success { get; set; }
        public bool RolledBack { get; set; }
        public int AppliedMutations { get; set; }
        public string Status { get; set; }
    }

    internal static class TransitionSafety
    {
        // Release-blocking clean-managed-Claude qualification has not happened yet.
        public const bool LiveClaudeWritesEnabled = false;

        public static bool IsActualClaudeConfigPath(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            var actual = ClaudePaths.Current();

            return string.Equals(candidate, actual.DesktopConfigPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, actual.MetaPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, actual.WrnProfilePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class TransitionHash
    {
        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return string.Concat(
                    sha.ComputeHash(bytes ?? new byte[0])
                        .Select(delegate(byte b) { return b.ToString("x2"); }));
            }
        }
    }
    internal static class ClaudeActivationCompiler
    {
        private const string WrnProfileName = "WRN AI Gateway - OpenRouter";

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 256
        };

        public static ClaudeActivationPlan Compile(
            ClaudeDiscoverySnapshot discovery,
            ClaudePaths paths,
            CatalogueDocument catalogue,
            string localGatewayToken,
            int gatewayPort)
        {
            if (discovery == null) throw new ArgumentNullException("discovery");
            if (paths == null) throw new ArgumentNullException("paths");
            if (catalogue == null) throw new ArgumentNullException("catalogue");

            ValidateSourceState(discovery, localGatewayToken, gatewayPort);

            string catalogueError;
            if (!CatalogueValidator.Validate(catalogue, out catalogueError))
                throw new InvalidOperationException("CATALOGUE_INVALID:" + catalogueError);

            if (!Directory.Exists(paths.ThirdPartyRoot)
                || !Directory.Exists(paths.ConfigLibraryPath))
            {
                throw new InvalidOperationException(
                    "CLAUDE_CONFIG_PARENT_MISSING_PENDING_MANAGED_QUALIFICATION");
            }

            var baseline = ClaudeConfigBaseline.Capture(paths);
            if (baseline.WrnProfile.Exists)
                throw new InvalidOperationException("WRN_PROFILE_COLLISION_AT_WTW_BASELINE");

            var desktop = ParseObject(
                baseline.DesktopConfig,
                "CLAUDE_DESKTOP_CONFIG_INVALID");
            object priorDeploymentMode;
            var priorDeploymentModeExisted =
                desktop.TryGetValue("deploymentMode", out priorDeploymentMode);
            desktop["deploymentMode"] = "3p";

            var meta = ParseObject(
                baseline.Meta,
                "CLAUDE_CONFIG_META_INVALID");
            object priorAppliedId;
            var priorAppliedIdExisted =
                meta.TryGetValue("appliedId", out priorAppliedId);
            var entries = ReadEntries(meta);

            foreach (var entry in entries)
            {
                var map = entry as Dictionary<string, object>;
                object id;
                if (map != null
                    && map.TryGetValue("id", out id)
                    && string.Equals(
                        Convert.ToString(id),
                        ClaudePaths.WrnProfileId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "WRN_PROFILE_ID_COLLISION_AT_WTW_BASELINE");
                }
            }

            entries.Add(new Dictionary<string, object>
            {
                { "id", ClaudePaths.WrnProfileId },
                { "name", WrnProfileName }
            });
            meta["entries"] = entries.ToArray();
            meta["appliedId"] = ClaudePaths.WrnProfileId;

            var profile = BuildProfile(catalogue, localGatewayToken, gatewayPort);
            var profileBytes = SerializeObject(profile);

            var ownershipBaseline = new ClaudeOwnershipBaseline
            {
                SchemaVersion = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                ActivationCatalogueRelease = catalogue.release,
                DesktopDeploymentModeExisted = priorDeploymentModeExisted,
                DesktopDeploymentModeValue = priorDeploymentMode,
                MetaAppliedIdExisted = priorAppliedIdExisted,
                MetaAppliedIdValue = priorAppliedId,
                WrnProfileSha256 = TransitionHash.Sha256(profileBytes)
            };
            var ownershipBaselineBytes = new UTF8Encoding(false).GetBytes(
                Json.Serialize(ownershipBaseline));

            // Activation trigger is written last. The WRN-owned profile must exist
            // before metadata points to it, and both must be valid before deploymentMode
            // makes Claude treat the third-party profile as active.
            var mutations = new[]
            {
                BuildMutation(
                    baseline.WrnProfile,
                    profileBytes,
                    "Write the WRN-owned inference profile generated from the signed catalogue."),
                BuildMutation(
                    baseline.Meta,
                    SerializeObject(meta),
                    "Select the WRN-owned Claude configuration profile."),
                BuildMutation(
                    baseline.DesktopConfig,
                    SerializeObject(desktop),
                    "Activate Claude third-party deployment mode.")
            };

            return new ClaudeActivationPlan
            {
                Kind = "WTW_TO_WRN_FIXTURE_QUALIFICATION",
                CatalogueRelease = catalogue.release,
                DefaultModelKey = catalogue.defaultModelKey,
                Mutations = mutations,
                OwnershipBaselineBytes = ownershipBaselineBytes,
                OwnershipBaselineExpectedSha256 = null,
                RemoveOwnershipBaselineOnSuccess = false,
                AllowlistedPaths = new[]
                {
                    paths.DesktopConfigPath,
                    paths.MetaPath,
                    paths.WrnProfilePath
                },
                ExplicitNonGoals = new[]
                {
                    "Do not write Claude history or Cowork session stores.",
                    "Do not touch IndexedDB, Local Storage, cookies or account/session data.",
                    "Do not install, uninstall, patch, copy or re-register Claude.",
                    "Do not terminate or restart Claude.",
                    "Do not use hard-coded model names or upstream model IDs."
                }
            };
        }
        private static void ValidateSourceState(
            ClaudeDiscoverySnapshot discovery,
            string localGatewayToken,
            int gatewayPort)
        {
            if (!discovery.ReadOnly
                || !discovery.ConfigReadable
                || discovery.InstallKind != ClaudeInstallKind.ManagedPackage
                || discovery.ClaudeRunning
                || discovery.Mode != ClaudeMode.Wtw)
            {
                throw new InvalidOperationException(
                    "WRN_ACTIVATION_SOURCE_STATE_NOT_SAFE");
            }

            if (string.IsNullOrWhiteSpace(localGatewayToken))
                throw new InvalidOperationException("LOCAL_GATEWAY_TOKEN_MISSING");

            if (gatewayPort < 1024 || gatewayPort > 65535)
                throw new InvalidOperationException("GATEWAY_PORT_INVALID");
        }

        private static Dictionary<string, object> ParseObject(
            TransitionFileSnapshot snapshot,
            string error)
        {
            if (!snapshot.Exists)
                return new Dictionary<string, object>(
                    StringComparer.OrdinalIgnoreCase);

            try
            {
                var value = Json.DeserializeObject(
                    Encoding.UTF8.GetString(snapshot.Bytes))
                    as Dictionary<string, object>;

                if (value == null)
                    throw new InvalidDataException();

                return value;
            }
            catch
            {
                throw new InvalidOperationException(error);
            }
        }

        private static List<object> ReadEntries(
            Dictionary<string, object> meta)
        {
            object value;
            if (!meta.TryGetValue("entries", out value) || value == null)
                return new List<object>();

            var array = value as object[];
            if (array != null)
                return array.ToList();

            var list = value as ArrayList;
            if (list != null)
                return list.Cast<object>().ToList();

            throw new InvalidOperationException(
                "CLAUDE_CONFIG_META_ENTRIES_INVALID");
        }

        private static Dictionary<string, object> BuildProfile(
            CatalogueDocument catalogue,
            string localGatewayToken,
            int gatewayPort)
        {
            var visible = catalogue.models
                .Where(delegate(CatalogueModel model) { return model.visible; })
                .OrderByDescending(delegate(CatalogueModel model)
                {
                    return string.Equals(
                        model.key,
                        catalogue.defaultModelKey,
                        StringComparison.OrdinalIgnoreCase);
                })
                .Select(delegate(CatalogueModel model)
                {
                    return (object)new Dictionary<string, object>
                    {
                        { "name", model.claudeAlias },
                        { "labelOverride", model.label }
                    };
                })
                .ToArray();

            return new Dictionary<string, object>
            {
                {
                    "inferenceGatewayBaseUrl",
                    "http://127.0.0.1:" + gatewayPort
                },
                { "inferenceGatewayApiKey", localGatewayToken },
                { "inferenceGatewayAuthScheme", "bearer" },
                { "modelDiscoveryEnabled", false },
                { "inferenceModels", visible },
                { "inferenceProvider", "gateway" },
                { "inferenceCredentialKind", "static" },
                { "chatTabEnabled", true }
            };
        }

        private static byte[] SerializeObject(
            Dictionary<string, object> value)
        {
            return new UTF8Encoding(false).GetBytes(Json.Serialize(value));
        }

        private static TransitionMutation BuildMutation(
            TransitionFileSnapshot before,
            byte[] desired,
            string purpose)
        {
            return new TransitionMutation
            {
                Path = before.Path,
                ExpectedExists = before.Exists,
                ExpectedSha256 = before.Sha256,
                DesiredExists = true,
                DesiredBytes = desired,
                DesiredSha256 = TransitionHash.Sha256(desired),
                Purpose = purpose
            };
        }
    }
    internal static class ClaudeTransitionState
    {
        public static string OwnershipBaselinePath(string transitionStateRoot)
        {
            return Path.Combine(transitionStateRoot, "activation-baseline.json");
        }

        public static string PendingTransactionPath(string transitionStateRoot)
        {
            return Path.Combine(transitionStateRoot, "pending");
        }
    }

    internal static class ClaudeDeactivationCompiler
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 256
        };

        public static ClaudeActivationPlan Compile(
            ClaudeDiscoverySnapshot discovery,
            ClaudePaths paths,
            string transitionStateRoot)
        {
            if (discovery == null) throw new ArgumentNullException("discovery");
            if (paths == null) throw new ArgumentNullException("paths");
            if (string.IsNullOrWhiteSpace(transitionStateRoot))
                throw new ArgumentNullException("transitionStateRoot");

            ValidateSourceState(discovery);

            var baselinePath =
                ClaudeTransitionState.OwnershipBaselinePath(transitionStateRoot);
            if (!File.Exists(baselinePath))
                throw new InvalidOperationException(
                    "WRN_OWNERSHIP_BASELINE_MISSING");

            var baselineBytes = File.ReadAllBytes(baselinePath);
            ClaudeOwnershipBaseline ownership;
            try
            {
                ownership = Json.Deserialize<ClaudeOwnershipBaseline>(
                    Encoding.UTF8.GetString(baselineBytes));
            }
            catch
            {
                throw new InvalidOperationException(
                    "WRN_OWNERSHIP_BASELINE_INVALID");
            }

            if (ownership == null
                || ownership.SchemaVersion != 1
                || ownership.ActivationCatalogueRelease < 1
                || string.IsNullOrWhiteSpace(ownership.WrnProfileSha256))
            {
                throw new InvalidOperationException(
                    "WRN_OWNERSHIP_BASELINE_INVALID");
            }

            var current = ClaudeConfigBaseline.Capture(paths);
            if (!current.DesktopConfig.Exists
                || !current.Meta.Exists
                || !current.WrnProfile.Exists)
            {
                throw new InvalidOperationException(
                    "WRN_CONFIGURATION_INCOMPLETE");
            }

            if (!string.Equals(
                current.WrnProfile.Sha256,
                ownership.WrnProfileSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "WRN_PROFILE_CHANGED_SINCE_ACTIVATION");
            }

            var desktop = ParseObject(
                current.DesktopConfig,
                "CLAUDE_DESKTOP_CONFIG_INVALID");
            object deploymentMode;
            if (!desktop.TryGetValue("deploymentMode", out deploymentMode)
                || !string.Equals(
                    Convert.ToString(deploymentMode),
                    "3p",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "WRN_DEPLOYMENT_MODE_NOT_ACTIVE");
            }

            if (ownership.DesktopDeploymentModeExisted)
                desktop["deploymentMode"] =
                    ownership.DesktopDeploymentModeValue;
            else
                desktop.Remove("deploymentMode");

            var meta = ParseObject(
                current.Meta,
                "CLAUDE_CONFIG_META_INVALID");
            object appliedId;
            if (!meta.TryGetValue("appliedId", out appliedId)
                || !string.Equals(
                    Convert.ToString(appliedId),
                    ClaudePaths.WrnProfileId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "WRN_PROFILE_NOT_SELECTED");
            }

            var entries = ReadEntries(meta);
            var wrnEntries = entries
                .OfType<Dictionary<string, object>>()
                .Where(delegate(Dictionary<string, object> entry)
                {
                    object id;
                    return entry.TryGetValue("id", out id)
                        && string.Equals(
                            Convert.ToString(id),
                            ClaudePaths.WrnProfileId,
                            StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();

            if (wrnEntries.Length != 1)
                throw new InvalidOperationException(
                    "WRN_PROFILE_ENTRY_COUNT_INVALID");

            entries.Remove(wrnEntries[0]);
            meta["entries"] = entries.ToArray();

            if (ownership.MetaAppliedIdExisted)
                meta["appliedId"] = ownership.MetaAppliedIdValue;
            else
                meta.Remove("appliedId");

            var mutations = new[]
            {
                BuildMutation(
                    current.DesktopConfig,
                    SerializeObject(desktop),
                    true,
                    "Deactivate Claude third-party deployment mode first."),
                BuildMutation(
                    current.Meta,
                    SerializeObject(meta),
                    true,
                    "Restore the pre-WRN profile selection and remove only the WRN entry."),
                BuildMutation(
                    current.WrnProfile,
                    null,
                    false,
                    "Remove the WRN-owned inference profile last.")
            };

            return new ClaudeActivationPlan
            {
                Kind = "WRN_TO_WTW_FIXTURE_QUALIFICATION",
                CatalogueRelease = ownership.ActivationCatalogueRelease,
                DefaultModelKey = null,
                Mutations = mutations,
                OwnershipBaselineBytes = null,
                OwnershipBaselineExpectedSha256 =
                    TransitionHash.Sha256(baselineBytes),
                RemoveOwnershipBaselineOnSuccess = true,
                AllowlistedPaths = new[]
                {
                    paths.DesktopConfigPath,
                    paths.MetaPath,
                    paths.WrnProfilePath
                },
                ExplicitNonGoals = new[]
                {
                    "Do not restore whole desktop/meta snapshots over newer user preferences.",
                    "Do not write Claude history or Cowork session stores.",
                    "Do not touch IndexedDB, Local Storage, cookies or account/session data.",
                    "Do not install, uninstall, patch, copy or re-register Claude.",
                    "Do not terminate or restart Claude."
                }
            };
        }

        private static void ValidateSourceState(
            ClaudeDiscoverySnapshot discovery)
        {
            if (!discovery.ReadOnly
                || !discovery.ConfigReadable
                || discovery.InstallKind != ClaudeInstallKind.ManagedPackage
                || discovery.ClaudeRunning
                || discovery.Mode != ClaudeMode.Wrn)
            {
                throw new InvalidOperationException(
                    "WTW_RESTORATION_SOURCE_STATE_NOT_SAFE");
            }
        }

        private static Dictionary<string, object> ParseObject(
            TransitionFileSnapshot snapshot,
            string error)
        {
            try
            {
                var value = Json.DeserializeObject(
                    Encoding.UTF8.GetString(snapshot.Bytes))
                    as Dictionary<string, object>;
                if (value == null) throw new InvalidDataException();
                return value;
            }
            catch
            {
                throw new InvalidOperationException(error);
            }
        }

        private static List<object> ReadEntries(
            Dictionary<string, object> meta)
        {
            object value;
            if (!meta.TryGetValue("entries", out value) || value == null)
                return new List<object>();

            var array = value as object[];
            if (array != null) return array.ToList();

            var list = value as ArrayList;
            if (list != null) return list.Cast<object>().ToList();

            throw new InvalidOperationException(
                "CLAUDE_CONFIG_META_ENTRIES_INVALID");
        }

        private static byte[] SerializeObject(
            Dictionary<string, object> value)
        {
            return new UTF8Encoding(false).GetBytes(Json.Serialize(value));
        }

        private static TransitionMutation BuildMutation(
            TransitionFileSnapshot before,
            byte[] desired,
            bool desiredExists,
            string purpose)
        {
            return new TransitionMutation
            {
                Path = before.Path,
                ExpectedExists = before.Exists,
                ExpectedSha256 = before.Sha256,
                DesiredExists = desiredExists,
                DesiredBytes = desired,
                DesiredSha256 = desiredExists
                    ? TransitionHash.Sha256(desired)
                    : null,
                Purpose = purpose
            };
        }
    }

    internal sealed class TransitionJournalMutation
    {
        public string Path { get; set; }
        public bool ExpectedExists { get; set; }
        public string ExpectedSha256 { get; set; }
        public bool DesiredExists { get; set; }
        public string DesiredSha256 { get; set; }
    }

    internal sealed class TransitionJournal
    {
        public int SchemaVersion { get; set; }
        public string Kind { get; set; }
        public string BaselineAction { get; set; }
        public string BaselineSha256 { get; set; }
        public TransitionJournalMutation[] Mutations { get; set; }
    }

    internal static class ClaudeTransitionExecutor
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 256
            };

        public static TransitionExecutionResult Execute(
            ClaudeActivationPlan plan,
            string transitionStateRoot,
            int injectFailureAfterWrites)
        {
            return ExecuteCore(
                plan,
                transitionStateRoot,
                injectFailureAfterWrites,
                false);
        }

        public static TransitionExecutionResult Execute(
            ClaudeActivationPlan plan,
            string transitionStateRoot)
        {
            return Execute(plan, transitionStateRoot, 0);
        }

        internal static TransitionExecutionResult ExecuteInterruptedForTest(
            ClaudeActivationPlan plan,
            string transitionStateRoot,
            int interruptAfterWrites)
        {
            if (interruptAfterWrites < 1)
                throw new ArgumentOutOfRangeException(
                    "interruptAfterWrites");

            return ExecuteCore(
                plan,
                transitionStateRoot,
                interruptAfterWrites,
                true);
        }

        public static TransitionExecutionResult RecoverPending(
            string transitionStateRoot)
        {
            if (string.IsNullOrWhiteSpace(transitionStateRoot))
                throw new ArgumentNullException("transitionStateRoot");

            Directory.CreateDirectory(transitionStateRoot);
            using (AcquireLock(transitionStateRoot))
            {
                return RecoverPendingUnlocked(transitionStateRoot);
            }
        }

        private static TransitionExecutionResult ExecuteCore(
            ClaudeActivationPlan plan,
            string transitionStateRoot,
            int interruptAfterWrites,
            bool simulateInterruption)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            if (string.IsNullOrWhiteSpace(transitionStateRoot))
                throw new ArgumentNullException("transitionStateRoot");

            ValidatePlan(plan);

            if (!TransitionSafety.LiveClaudeWritesEnabled
                && plan.Mutations.Any(delegate(TransitionMutation mutation)
                {
                    return TransitionSafety.IsActualClaudeConfigPath(
                        mutation.Path);
                }))
            {
                throw new InvalidOperationException(
                    "LIVE_CLAUDE_WRITES_DISABLED_PENDING_MANAGED_QUALIFICATION");
            }

            Directory.CreateDirectory(transitionStateRoot);

            using (AcquireLock(transitionStateRoot))
            {
                var pendingRoot =
                    ClaudeTransitionState.PendingTransactionPath(
                        transitionStateRoot);
                if (Directory.Exists(pendingRoot))
                {
                    throw new InvalidOperationException(
                        "TRANSITION_RECOVERY_REQUIRED");
                }

                CleanupAbandonedPreparation(transitionStateRoot);
                Preflight(plan);
                PreflightOwnershipBaseline(
                    plan,
                    transitionStateRoot);

                var prepareRoot = Path.Combine(
                    transitionStateRoot,
                    "prepare-" + Guid.NewGuid().ToString("N"));
                var backupRoot = Path.Combine(prepareRoot, "backup");
                var stageRoot = Path.Combine(prepareRoot, "stage");
                Directory.CreateDirectory(backupRoot);
                Directory.CreateDirectory(stageRoot);

                var pendingPublished = false;
                var appliedCount = 0;

                try
                {
                    for (var i = 0; i < plan.Mutations.Length; i++)
                    {
                        var mutation = plan.Mutations[i];

                        if (!Directory.Exists(
                            Path.GetDirectoryName(mutation.Path)))
                        {
                            throw new InvalidOperationException(
                                "TARGET_PARENT_DIRECTORY_MISSING");
                        }

                        if (mutation.ExpectedExists)
                        {
                            WriteAllBytesFlush(
                                Path.Combine(
                                    backupRoot,
                                    i.ToString("D2") + ".bak"),
                                File.ReadAllBytes(mutation.Path));
                        }

                        if (mutation.DesiredExists)
                        {
                            WriteAllBytesFlush(
                                Path.Combine(
                                    stageRoot,
                                    i.ToString("D2") + ".new"),
                                mutation.DesiredBytes);
                        }
                    }

                    var journal = BuildJournal(plan);
                    WriteProtectedJournal(
                        Path.Combine(prepareRoot, "journal.dpapi"),
                        journal);

                    Directory.Move(prepareRoot, pendingRoot);
                    pendingPublished = true;

                    PersistActivationBaselineIfNeeded(
                        plan,
                        transitionStateRoot);

                    for (var i = 0; i < plan.Mutations.Length; i++)
                    {
                        var mutation = plan.Mutations[i];
                        var staged = Path.Combine(
                            pendingRoot,
                            "stage",
                            i.ToString("D2") + ".new");

                        ApplyOne(mutation, staged);
                        appliedCount++;

                        if (interruptAfterWrites > 0
                            && appliedCount >= interruptAfterWrites)
                        {
                            if (simulateInterruption)
                            {
                                return new TransitionExecutionResult
                                {
                                    Success = false,
                                    RolledBack = false,
                                    AppliedMutations = appliedCount,
                                    Status =
                                        "TRANSITION_INTERRUPTED_FOR_TEST"
                                };
                            }

                            throw new IOException(
                                "INJECTED_TRANSITION_FAILURE");
                        }
                    }

                    CompleteOwnershipBaseline(
                        plan,
                        transitionStateRoot);

                    Directory.Delete(pendingRoot, true);

                    return new TransitionExecutionResult
                    {
                        Success = true,
                        RolledBack = false,
                        AppliedMutations = appliedCount,
                        Status = "TRANSITION_APPLIED"
                    };
                }
                catch
                {
                    if (pendingPublished)
                    {
                        var recovery =
                            RecoverPendingUnlocked(transitionStateRoot);
                        return new TransitionExecutionResult
                        {
                            Success = false,
                            RolledBack = recovery.RolledBack,
                            AppliedMutations = appliedCount,
                            Status = recovery.RolledBack
                                ? "TRANSITION_ROLLED_BACK"
                                : recovery.Status
                        };
                    }

                    try
                    {
                        if (Directory.Exists(prepareRoot))
                            Directory.Delete(prepareRoot, true);
                    }
                    catch
                    {
                    }

                    throw;
                }
            }
        }

        private static TransitionJournal BuildJournal(
            ClaudeActivationPlan plan)
        {
            var baselineAction = "none";
            var baselineHash = (string)null;

            if (plan.OwnershipBaselineBytes != null)
            {
                baselineAction = "create";
                baselineHash = TransitionHash.Sha256(
                    plan.OwnershipBaselineBytes);
            }
            else if (plan.RemoveOwnershipBaselineOnSuccess)
            {
                baselineAction = "remove_on_success";
                baselineHash =
                    plan.OwnershipBaselineExpectedSha256;
            }

            return new TransitionJournal
            {
                SchemaVersion = 1,
                Kind = plan.Kind,
                BaselineAction = baselineAction,
                BaselineSha256 = baselineHash,
                Mutations = plan.Mutations.Select(
                    delegate(TransitionMutation mutation)
                    {
                        return new TransitionJournalMutation
                        {
                            Path = mutation.Path,
                            ExpectedExists =
                                mutation.ExpectedExists,
                            ExpectedSha256 =
                                mutation.ExpectedSha256,
                            DesiredExists =
                                mutation.DesiredExists,
                            DesiredSha256 =
                                mutation.DesiredSha256
                        };
                    }).ToArray()
            };
        }

        private static void ValidatePlan(
            ClaudeActivationPlan plan)
        {
            if (plan.Mutations == null
                || plan.Mutations.Length != 3
                || plan.AllowlistedPaths == null
                || plan.AllowlistedPaths.Length != 3)
            {
                throw new InvalidOperationException(
                    "TRANSITION_PLAN_SHAPE_INVALID");
            }

            var allowlist = new HashSet<string>(
                plan.AllowlistedPaths,
                StringComparer.OrdinalIgnoreCase);

            if (allowlist.Count != 3)
                throw new InvalidOperationException(
                    "TRANSITION_ALLOWLIST_INVALID");

            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var mutation in plan.Mutations)
            {
                if (mutation == null
                    || string.IsNullOrWhiteSpace(mutation.Path)
                    || !allowlist.Contains(mutation.Path)
                    || !seen.Add(mutation.Path))
                {
                    throw new InvalidOperationException(
                        "TRANSITION_MUTATION_NOT_ALLOWLISTED");
                }

                if (mutation.DesiredExists)
                {
                    if (mutation.DesiredBytes == null
                        || !string.Equals(
                            mutation.DesiredSha256,
                            TransitionHash.Sha256(
                                mutation.DesiredBytes),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "TRANSITION_DESIRED_HASH_INVALID");
                    }
                }
                else if (mutation.DesiredBytes != null
                    || mutation.DesiredSha256 != null)
                {
                    throw new InvalidOperationException(
                        "TRANSITION_DELETE_MUTATION_INVALID");
                }
            }

            if (plan.OwnershipBaselineBytes != null
                && plan.RemoveOwnershipBaselineOnSuccess)
            {
                throw new InvalidOperationException(
                    "TRANSITION_BASELINE_ACTION_INVALID");
            }

            if (plan.RemoveOwnershipBaselineOnSuccess
                && string.IsNullOrWhiteSpace(
                    plan.OwnershipBaselineExpectedSha256))
            {
                throw new InvalidOperationException(
                    "TRANSITION_BASELINE_EXPECTATION_MISSING");
            }
        }

        private static void Preflight(
            ClaudeActivationPlan plan)
        {
            foreach (var mutation in plan.Mutations)
            {
                if (!MatchesState(
                    mutation.Path,
                    mutation.ExpectedExists,
                    mutation.ExpectedSha256))
                {
                    var exists = File.Exists(mutation.Path);
                    throw new InvalidOperationException(
                        exists == mutation.ExpectedExists
                            ? "TRANSITION_SOURCE_HASH_CHANGED"
                            : "TRANSITION_SOURCE_EXISTENCE_CHANGED");
                }
            }
        }

        private static void PreflightOwnershipBaseline(
            ClaudeActivationPlan plan,
            string transitionStateRoot)
        {
            var baselinePath =
                ClaudeTransitionState.OwnershipBaselinePath(
                    transitionStateRoot);

            if (plan.OwnershipBaselineBytes != null)
            {
                if (File.Exists(baselinePath))
                    throw new InvalidOperationException(
                        "ACTIVATION_BASELINE_ALREADY_EXISTS");
                return;
            }

            if (plan.RemoveOwnershipBaselineOnSuccess)
            {
                if (!MatchesState(
                    baselinePath,
                    true,
                    plan.OwnershipBaselineExpectedSha256))
                {
                    throw new InvalidOperationException(
                        "WRN_OWNERSHIP_BASELINE_CHANGED");
                }
            }
        }

        private static void PersistActivationBaselineIfNeeded(
            ClaudeActivationPlan plan,
            string transitionStateRoot)
        {
            if (plan.OwnershipBaselineBytes == null)
                return;

            var path =
                ClaudeTransitionState.OwnershipBaselinePath(
                    transitionStateRoot);
            WriteAtomicNew(path, plan.OwnershipBaselineBytes);
        }

        private static void CompleteOwnershipBaseline(
            ClaudeActivationPlan plan,
            string transitionStateRoot)
        {
            if (!plan.RemoveOwnershipBaselineOnSuccess)
                return;

            var path =
                ClaudeTransitionState.OwnershipBaselinePath(
                    transitionStateRoot);
            if (!MatchesState(
                path,
                true,
                plan.OwnershipBaselineExpectedSha256))
            {
                throw new IOException(
                    "WRN_OWNERSHIP_BASELINE_CHANGED");
            }

            File.Delete(path);
        }

        private static TransitionExecutionResult
            RecoverPendingUnlocked(string transitionStateRoot)
        {
            var pendingRoot =
                ClaudeTransitionState.PendingTransactionPath(
                    transitionStateRoot);
            if (!Directory.Exists(pendingRoot))
            {
                return new TransitionExecutionResult
                {
                    Success = true,
                    RolledBack = false,
                    AppliedMutations = 0,
                    Status = "NO_PENDING_TRANSITION"
                };
            }

            var journal = ReadProtectedJournal(
                Path.Combine(pendingRoot, "journal.dpapi"));
            ValidateJournal(journal);

            var expectedMatches = new bool[journal.Mutations.Length];
            var desiredMatches = new bool[journal.Mutations.Length];
            var allExpected = true;
            var allDesired = true;

            for (var i = 0; i < journal.Mutations.Length; i++)
            {
                var mutation = journal.Mutations[i];
                expectedMatches[i] = MatchesState(
                    mutation.Path,
                    mutation.ExpectedExists,
                    mutation.ExpectedSha256);
                desiredMatches[i] = MatchesState(
                    mutation.Path,
                    mutation.DesiredExists,
                    mutation.DesiredSha256);

                if (!expectedMatches[i] && !desiredMatches[i])
                {
                    throw new IOException(
                        "TRANSITION_RECOVERY_CONFLICT");
                }

                allExpected = allExpected && expectedMatches[i];
                allDesired = allDesired && desiredMatches[i];
            }

            var baselinePath =
                ClaudeTransitionState.OwnershipBaselinePath(
                    transitionStateRoot);

            if (allDesired)
            {
                FinalizeRecoveredDesiredState(
                    journal,
                    baselinePath);
                Directory.Delete(pendingRoot, true);
                return new TransitionExecutionResult
                {
                    Success = true,
                    RolledBack = false,
                    AppliedMutations = journal.Mutations.Length,
                    Status = "TRANSITION_RECOVERY_FINALIZED"
                };
            }

            ValidateRecoveryBackups(
                journal,
                pendingRoot);

            for (var i = journal.Mutations.Length - 1; i >= 0; i--)
            {
                if (expectedMatches[i])
                    continue;

                RestoreExpectedState(
                    journal.Mutations[i],
                    Path.Combine(
                        pendingRoot,
                        "backup",
                        i.ToString("D2") + ".bak"));
            }

            for (var i = 0; i < journal.Mutations.Length; i++)
            {
                var mutation = journal.Mutations[i];
                if (!MatchesState(
                    mutation.Path,
                    mutation.ExpectedExists,
                    mutation.ExpectedSha256))
                {
                    throw new IOException(
                        "TRANSITION_ROLLBACK_HASH_MISMATCH");
                }
            }

            RestoreBaselineForRollback(
                journal,
                baselinePath);

            Directory.Delete(pendingRoot, true);
            return new TransitionExecutionResult
            {
                Success = false,
                RolledBack = true,
                AppliedMutations = 0,
                Status = "TRANSITION_RECOVERY_ROLLED_BACK"
            };
        }

        private static void FinalizeRecoveredDesiredState(
            TransitionJournal journal,
            string baselinePath)
        {
            if (string.Equals(
                journal.BaselineAction,
                "create",
                StringComparison.Ordinal))
            {
                if (!MatchesState(
                    baselinePath,
                    true,
                    journal.BaselineSha256))
                {
                    throw new IOException(
                        "TRANSITION_RECOVERY_BASELINE_CONFLICT");
                }
            }
            else if (string.Equals(
                journal.BaselineAction,
                "remove_on_success",
                StringComparison.Ordinal))
            {
                if (File.Exists(baselinePath))
                {
                    if (!MatchesState(
                        baselinePath,
                        true,
                        journal.BaselineSha256))
                    {
                        throw new IOException(
                            "TRANSITION_RECOVERY_BASELINE_CONFLICT");
                    }

                    File.Delete(baselinePath);
                }
            }
        }

        private static void RestoreBaselineForRollback(
            TransitionJournal journal,
            string baselinePath)
        {
            if (string.Equals(
                journal.BaselineAction,
                "create",
                StringComparison.Ordinal))
            {
                if (File.Exists(baselinePath))
                {
                    if (!MatchesState(
                        baselinePath,
                        true,
                        journal.BaselineSha256))
                    {
                        throw new IOException(
                            "TRANSITION_RECOVERY_BASELINE_CONFLICT");
                    }

                    File.Delete(baselinePath);
                }
            }
            else if (string.Equals(
                journal.BaselineAction,
                "remove_on_success",
                StringComparison.Ordinal))
            {
                if (!MatchesState(
                    baselinePath,
                    true,
                    journal.BaselineSha256))
                {
                    throw new IOException(
                        "TRANSITION_RECOVERY_BASELINE_CONFLICT");
                }
            }
        }

        private static void ValidateRecoveryBackups(
            TransitionJournal journal,
            string pendingRoot)
        {
            for (var i = 0; i < journal.Mutations.Length; i++)
            {
                var mutation = journal.Mutations[i];
                if (!mutation.ExpectedExists)
                    continue;

                var backup = Path.Combine(
                    pendingRoot,
                    "backup",
                    i.ToString("D2") + ".bak");
                if (!MatchesState(
                    backup,
                    true,
                    mutation.ExpectedSha256))
                {
                    throw new IOException(
                        "TRANSITION_ROLLBACK_BACKUP_INVALID");
                }
            }
        }

        private static void RestoreExpectedState(
            TransitionJournalMutation mutation,
            string backupPath)
        {
            if (!mutation.ExpectedExists)
            {
                if (File.Exists(mutation.Path))
                    File.Delete(mutation.Path);
                return;
            }

            var restore =
                backupPath + ".restore-" +
                Guid.NewGuid().ToString("N");
            File.Copy(backupPath, restore, true);

            if (File.Exists(mutation.Path))
                File.Replace(restore, mutation.Path, null, true);
            else
                File.Move(restore, mutation.Path);
        }

        private static void ValidateJournal(
            TransitionJournal journal)
        {
            if (journal == null
                || journal.SchemaVersion != 1
                || journal.Mutations == null
                || journal.Mutations.Length != 3)
            {
                throw new IOException(
                    "TRANSITION_JOURNAL_INVALID");
            }

            if (journal.BaselineAction != "create"
                && journal.BaselineAction != "remove_on_success"
                && journal.BaselineAction != "none")
            {
                throw new IOException(
                    "TRANSITION_JOURNAL_INVALID");
            }

            if (journal.BaselineAction != "none"
                && string.IsNullOrWhiteSpace(
                    journal.BaselineSha256))
            {
                throw new IOException(
                    "TRANSITION_JOURNAL_INVALID");
            }

            var seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var mutation in journal.Mutations)
            {
                if (mutation == null
                    || string.IsNullOrWhiteSpace(mutation.Path)
                    || !seen.Add(mutation.Path)
                    || (mutation.ExpectedExists
                        && string.IsNullOrWhiteSpace(
                            mutation.ExpectedSha256))
                    || (mutation.DesiredExists
                        && string.IsNullOrWhiteSpace(
                            mutation.DesiredSha256)))
                {
                    throw new IOException(
                        "TRANSITION_JOURNAL_INVALID");
                }
            }
        }

        private static void ApplyOne(
            TransitionMutation mutation,
            string stagedPath)
        {
            if (!mutation.DesiredExists)
            {
                if (!File.Exists(mutation.Path))
                    throw new InvalidOperationException(
                        "TRANSITION_DELETE_TARGET_MISSING");

                File.Delete(mutation.Path);

                if (File.Exists(mutation.Path))
                    throw new IOException(
                        "TRANSITION_POST_WRITE_EXISTENCE_MISMATCH");
                return;
            }

            if (mutation.ExpectedExists)
            {
                File.Replace(
                    stagedPath,
                    mutation.Path,
                    null,
                    true);
            }
            else
            {
                if (File.Exists(mutation.Path))
                    throw new InvalidOperationException(
                        "TRANSITION_NEW_TARGET_APPEARED");

                File.Move(stagedPath, mutation.Path);
            }

            if (!MatchesState(
                mutation.Path,
                true,
                mutation.DesiredSha256))
            {
                throw new IOException(
                    "TRANSITION_POST_WRITE_HASH_MISMATCH");
            }
        }

        private static bool MatchesState(
            string path,
            bool expectedExists,
            string expectedSha256)
        {
            var exists = File.Exists(path);
            if (exists != expectedExists)
                return false;

            if (!exists)
                return true;

            if (string.IsNullOrWhiteSpace(expectedSha256))
                return false;

            return string.Equals(
                TransitionHash.Sha256(
                    File.ReadAllBytes(path)),
                expectedSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private static FileStream AcquireLock(
            string transitionStateRoot)
        {
            return new FileStream(
                Path.Combine(
                    transitionStateRoot,
                    "transition.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }

        private static void CleanupAbandonedPreparation(
            string transitionStateRoot)
        {
            foreach (var path in Directory.GetDirectories(
                transitionStateRoot,
                "prepare-*"))
            {
                try { Directory.Delete(path, true); }
                catch { }
            }
        }

        private static void WriteProtectedJournal(
            string path,
            TransitionJournal journal)
        {
            var raw = Encoding.UTF8.GetBytes(
                Json.Serialize(journal));
            var protectedBytes = ProtectedData.Protect(
                raw,
                null,
                DataProtectionScope.CurrentUser);

            try
            {
                WriteAllBytesFlush(path, protectedBytes);
            }
            finally
            {
                Array.Clear(raw, 0, raw.Length);
                Array.Clear(
                    protectedBytes,
                    0,
                    protectedBytes.Length);
            }
        }

        private static TransitionJournal ReadProtectedJournal(
            string path)
        {
            if (!File.Exists(path))
                throw new IOException(
                    "TRANSITION_JOURNAL_MISSING");

            var protectedBytes = File.ReadAllBytes(path);
            byte[] raw = null;
            try
            {
                raw = ProtectedData.Unprotect(
                    protectedBytes,
                    null,
                    DataProtectionScope.CurrentUser);
                return Json.Deserialize<TransitionJournal>(
                    Encoding.UTF8.GetString(raw));
            }
            catch
            {
                throw new IOException(
                    "TRANSITION_JOURNAL_INVALID");
            }
            finally
            {
                if (raw != null)
                    Array.Clear(raw, 0, raw.Length);
                Array.Clear(
                    protectedBytes,
                    0,
                    protectedBytes.Length);
            }
        }

        private static void WriteAtomicNew(
            string path,
            byte[] bytes)
        {
            var temp =
                path + ".tmp-" + Guid.NewGuid().ToString("N");
            WriteAllBytesFlush(temp, bytes);
            try
            {
                if (File.Exists(path))
                    throw new InvalidOperationException(
                        "ACTIVATION_BASELINE_ALREADY_EXISTS");

                File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        private static void WriteAllBytesFlush(
            string path,
            byte[] bytes)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }
    }

}
