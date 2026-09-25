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

    internal sealed class TransitionMutation
    {
        public string Path { get; set; }
        public bool ExpectedExists { get; set; }
        public string ExpectedSha256 { get; set; }
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
            desktop["deploymentMode"] = "3p";

            var meta = ParseObject(
                baseline.Meta,
                "CLAUDE_CONFIG_META_INVALID");
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

            // Activation trigger is written last. The WRN-owned profile must exist
            // before metadata points to it, and both must be valid before deploymentMode
            // makes Claude treat the third-party profile as active.
            var mutations = new[]
            {
                BuildMutation(
                    baseline.WrnProfile,
                    SerializeObject(profile),
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
                DesiredBytes = desired,
                DesiredSha256 = TransitionHash.Sha256(desired),
                Purpose = purpose
            };
        }
    }
    internal static class ClaudeTransitionExecutor
    {
        public static TransitionExecutionResult Execute(
            ClaudeActivationPlan plan,
            string transactionRoot,
            int injectFailureAfterWrites)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            if (string.IsNullOrWhiteSpace(transactionRoot))
                throw new ArgumentNullException("transactionRoot");

            ValidatePlan(plan);

            if (!TransitionSafety.LiveClaudeWritesEnabled
                && plan.Mutations.Any(delegate(TransitionMutation mutation)
                {
                    return TransitionSafety.IsActualClaudeConfigPath(mutation.Path);
                }))
            {
                throw new InvalidOperationException(
                    "LIVE_CLAUDE_WRITES_DISABLED_PENDING_MANAGED_QUALIFICATION");
            }

            Preflight(plan);

            var transactionId = Guid.NewGuid().ToString("N");
            var root = Path.Combine(transactionRoot, transactionId);
            var backupRoot = Path.Combine(root, "backup");
            var stageRoot = Path.Combine(root, "stage");

            Directory.CreateDirectory(backupRoot);
            Directory.CreateDirectory(stageRoot);

            var applied = new List<int>();

            try
            {
                for (var i = 0; i < plan.Mutations.Length; i++)
                {
                    var mutation = plan.Mutations[i];

                    if (!Directory.Exists(Path.GetDirectoryName(mutation.Path)))
                        throw new InvalidOperationException(
                            "TARGET_PARENT_DIRECTORY_MISSING");

                    if (mutation.ExpectedExists)
                    {
                        var backup = Path.Combine(
                            backupRoot,
                            i.ToString("D2") + ".bak");
                        WriteAllBytesFlush(
                            backup,
                            File.ReadAllBytes(mutation.Path));
                    }

                    var staged = Path.Combine(
                        stageRoot,
                        i.ToString("D2") + ".new");
                    WriteAllBytesFlush(staged, mutation.DesiredBytes);
                }

                for (var i = 0; i < plan.Mutations.Length; i++)
                {
                    var mutation = plan.Mutations[i];
                    var staged = Path.Combine(
                        stageRoot,
                        i.ToString("D2") + ".new");

                    ApplyOne(mutation, staged);
                    applied.Add(i);

                    if (injectFailureAfterWrites > 0
                        && applied.Count >= injectFailureAfterWrites)
                    {
                        throw new IOException(
                            "INJECTED_TRANSITION_FAILURE");
                    }
                }

                return new TransitionExecutionResult
                {
                    Success = true,
                    RolledBack = false,
                    AppliedMutations = applied.Count,
                    Status = "TRANSITION_APPLIED"
                };
            }
            catch
            {
                RollBack(plan, backupRoot, applied);
                return new TransitionExecutionResult
                {
                    Success = false,
                    RolledBack = true,
                    AppliedMutations = applied.Count,
                    Status = "TRANSITION_ROLLED_BACK"
                };
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                        Directory.Delete(root, true);
                }
                catch
                {
                }
            }
        }

        public static TransitionExecutionResult Execute(
            ClaudeActivationPlan plan,
            string transactionRoot)
        {
            return Execute(plan, transactionRoot, 0);
        }
        private static void ValidatePlan(ClaudeActivationPlan plan)
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
                    || mutation.DesiredBytes == null
                    || !allowlist.Contains(mutation.Path)
                    || !seen.Add(mutation.Path))
                {
                    throw new InvalidOperationException(
                        "TRANSITION_MUTATION_NOT_ALLOWLISTED");
                }

                if (!string.Equals(
                    mutation.DesiredSha256,
                    TransitionHash.Sha256(mutation.DesiredBytes),
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "TRANSITION_DESIRED_HASH_INVALID");
                }
            }
        }

        private static void Preflight(ClaudeActivationPlan plan)
        {
            foreach (var mutation in plan.Mutations)
            {
                var exists = File.Exists(mutation.Path);
                if (exists != mutation.ExpectedExists)
                {
                    throw new InvalidOperationException(
                        "TRANSITION_SOURCE_EXISTENCE_CHANGED");
                }

                if (exists)
                {
                    var hash = TransitionHash.Sha256(
                        File.ReadAllBytes(mutation.Path));
                    if (!string.Equals(
                        hash,
                        mutation.ExpectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "TRANSITION_SOURCE_HASH_CHANGED");
                    }
                }
            }
        }

        private static void ApplyOne(
            TransitionMutation mutation,
            string stagedPath)
        {
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

            var appliedHash = TransitionHash.Sha256(
                File.ReadAllBytes(mutation.Path));

            if (!string.Equals(
                appliedHash,
                mutation.DesiredSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "TRANSITION_POST_WRITE_HASH_MISMATCH");
            }
        }

        private static void RollBack(
            ClaudeActivationPlan plan,
            string backupRoot,
            List<int> applied)
        {
            for (var cursor = applied.Count - 1; cursor >= 0; cursor--)
            {
                var index = applied[cursor];
                var mutation = plan.Mutations[index];

                if (mutation.ExpectedExists)
                {
                    var backup = Path.Combine(
                        backupRoot,
                        index.ToString("D2") + ".bak");

                    if (!File.Exists(backup))
                        throw new IOException(
                            "TRANSITION_ROLLBACK_BACKUP_MISSING");

                    var restore = backup + ".restore";
                    File.Copy(backup, restore, true);

                    if (File.Exists(mutation.Path))
                    {
                        File.Replace(
                            restore,
                            mutation.Path,
                            null,
                            true);
                    }
                    else
                    {
                        File.Move(restore, mutation.Path);
                    }
                }
                else if (File.Exists(mutation.Path))
                {
                    File.Delete(mutation.Path);
                }
            }

            foreach (var mutation in plan.Mutations)
            {
                var exists = File.Exists(mutation.Path);
                if (exists != mutation.ExpectedExists)
                    throw new IOException(
                        "TRANSITION_ROLLBACK_EXISTENCE_MISMATCH");

                if (exists)
                {
                    var hash = TransitionHash.Sha256(
                        File.ReadAllBytes(mutation.Path));
                    if (!string.Equals(
                        hash,
                        mutation.ExpectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException(
                            "TRANSITION_ROLLBACK_HASH_MISMATCH");
                    }
                }
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
