using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace WRN.AIGateway
{
    internal enum ClaudeInstallKind
    {
        Missing,
        ManagedPackage,
        UserInstall,
        RecoveryDiagnostic,
        Unknown
    }

    internal enum ClaudeMode
    {
        Wtw,
        Wrn,
        ThirdPartyOther,
        Degraded,
        Unknown
    }

    internal sealed class ClaudePaths
    {
        public string LocalAppDataRoot { get; private set; }
        public string RoamingAppDataRoot { get; private set; }
        public string ThirdPartyRoot { get; private set; }
        public string DesktopConfigPath { get; private set; }
        public string ConfigLibraryPath { get; private set; }
        public string MetaPath { get; private set; }
        public string WrnProfilePath { get; private set; }

        public const string WrnProfileId = "a179a3b8-7f6e-4c80-9e33-3ed210fe3d41";

        public ClaudePaths(string localAppDataRoot, string roamingAppDataRoot)
        {
            LocalAppDataRoot = localAppDataRoot;
            RoamingAppDataRoot = roamingAppDataRoot;
            ThirdPartyRoot = Path.Combine(localAppDataRoot, "Claude-3p");
            DesktopConfigPath = Path.Combine(ThirdPartyRoot, "claude_desktop_config.json");
            ConfigLibraryPath = Path.Combine(ThirdPartyRoot, "configLibrary");
            MetaPath = Path.Combine(ConfigLibraryPath, "_meta.json");
            WrnProfilePath = Path.Combine(ConfigLibraryPath, WrnProfileId + ".json");
        }

        public static ClaudePaths Current()
        {
            return new ClaudePaths(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        }
    }

    internal sealed class ClaudeDiscoverySnapshot
    {
        public bool ReadOnly { get; set; }
        public string ObservedAtUtc { get; set; }
        [ScriptIgnore]
        public ClaudeInstallKind InstallKind { get; set; }
        public string InstallKindName { get { return InstallKind.ToString(); } }
        public string InstallReason { get; set; }
        public bool ClaudeRunning { get; set; }
        public int ClaudeProcessCount { get; set; }
        public string[] ClaudeProcessLocations { get; set; }
        [ScriptIgnore]
        public ClaudeMode Mode { get; set; }
        public string ModeName { get { return Mode.ToString(); } }
        public string ModeReason { get; set; }
        public bool DesktopConfigExists { get; set; }
        public bool MetaExists { get; set; }
        public bool WrnProfileExists { get; set; }
        public bool ConfigReadable { get; set; }
        public bool ManagedPackageMarkerFound { get; set; }
        public string DesktopConfigPath { get; set; }
        public string MetaPath { get; set; }
        public string WrnProfilePath { get; set; }
    }

    internal sealed class TransitionPlan
    {
        public string TargetMode { get; set; }
        public bool Allowed { get; set; }
        public string BlockReason { get; set; }
        public string[] ClaudeConfigWriteAllowlist { get; set; }
        public string[] PlannedActions { get; set; }
        public string[] ExplicitNonGoals { get; set; }
    }

    internal static class ClaudeDiscovery
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 128
        };

        public static ClaudeDiscoverySnapshot Inspect()
        {
            var paths = ClaudePaths.Current();
            var processLocations = GetClaudeProcessLocations();
            var managedPackageMarker = HasManagedPackageMarker();
            var installKind = ClassifyInstallation(processLocations, managedPackageMarker);
            var mode = ClaudeMode.Unknown;
            var modeReason = string.Empty;
            var configReadable = true;

            string desktopJson = null;
            string metaJson = null;
            try
            {
                if (File.Exists(paths.DesktopConfigPath))
                    desktopJson = File.ReadAllText(paths.DesktopConfigPath, Encoding.UTF8);
                if (File.Exists(paths.MetaPath))
                    metaJson = File.ReadAllText(paths.MetaPath, Encoding.UTF8);

                mode = ClassifyModeJson(desktopJson, metaJson, File.Exists(paths.WrnProfilePath), out modeReason);
            }
            catch (Exception ex)
            {
                configReadable = false;
                mode = ClaudeMode.Degraded;
                modeReason = "Claude configuration could not be read safely: " + ex.GetType().Name;
            }

            return new ClaudeDiscoverySnapshot
            {
                ReadOnly = true,
                ObservedAtUtc = DateTimeOffset.UtcNow.ToString("o"),
                InstallKind = installKind,
                InstallReason = ExplainInstallation(installKind, processLocations, managedPackageMarker),
                ClaudeRunning = processLocations.Length > 0,
                ClaudeProcessCount = processLocations.Length,
                ClaudeProcessLocations = processLocations,
                Mode = mode,
                ModeReason = modeReason,
                DesktopConfigExists = File.Exists(paths.DesktopConfigPath),
                MetaExists = File.Exists(paths.MetaPath),
                WrnProfileExists = File.Exists(paths.WrnProfilePath),
                ConfigReadable = configReadable,
                ManagedPackageMarkerFound = managedPackageMarker,
                DesktopConfigPath = paths.DesktopConfigPath,
                MetaPath = paths.MetaPath,
                WrnProfilePath = paths.WrnProfilePath
            };
        }

        internal static ClaudeInstallKind ClassifyInstallation(IEnumerable<string> processLocations, bool managedPackageMarkerFound)
        {
            var locations = (processLocations ?? Enumerable.Empty<string>())
                .Where(delegate(string p) { return !string.IsNullOrWhiteSpace(p); })
                .ToArray();

            if (locations.Any(delegate(string p)
                {
                    return p.IndexOf(@"\Documents\WindowsApps\Claude-Recovery\", StringComparison.OrdinalIgnoreCase) >= 0
                        || p.IndexOf(@"\Claude-Recovery\", StringComparison.OrdinalIgnoreCase) >= 0;
                }))
                return ClaudeInstallKind.RecoveryDiagnostic;

            if (locations.Any(delegate(string p)
                {
                    return p.IndexOf(@"\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0;
                }))
                return ClaudeInstallKind.ManagedPackage;

            if (managedPackageMarkerFound)
                return ClaudeInstallKind.ManagedPackage;

            if (locations.Any(delegate(string p)
                {
                    return p.IndexOf(@"\AppData\Local\", StringComparison.OrdinalIgnoreCase) >= 0;
                }))
                return ClaudeInstallKind.UserInstall;

            if (locations.Length == 0)
                return ClaudeInstallKind.Missing;

            return ClaudeInstallKind.Unknown;
        }

        internal static ClaudeMode ClassifyModeJson(string desktopJson, string metaJson, bool wrnProfileExists, out string reason)
        {
            try
            {
                Dictionary<string, object> desktop = null;
                if (!string.IsNullOrWhiteSpace(desktopJson))
                    desktop = Json.DeserializeObject(desktopJson) as Dictionary<string, object>;

                if (desktop == null)
                {
                    reason = "No third-party deployment mode is configured.";
                    return ClaudeMode.Wtw;
                }

                object deploymentModeValue;
                if (!desktop.TryGetValue("deploymentMode", out deploymentModeValue)
                    || deploymentModeValue == null
                    || string.IsNullOrWhiteSpace(Convert.ToString(deploymentModeValue)))
                {
                    reason = "No third-party deployment mode is configured.";
                    return ClaudeMode.Wtw;
                }

                var deploymentMode = Convert.ToString(deploymentModeValue);
                if (!string.Equals(deploymentMode, "3p", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "An unrecognised deployment mode is configured.";
                    return ClaudeMode.Degraded;
                }

                Dictionary<string, object> meta = null;
                if (!string.IsNullOrWhiteSpace(metaJson))
                    meta = Json.DeserializeObject(metaJson) as Dictionary<string, object>;

                string appliedId = null;
                if (meta != null)
                {
                    object applied;
                    if (meta.TryGetValue("appliedId", out applied) && applied != null)
                        appliedId = Convert.ToString(applied);
                }

                if (string.Equals(appliedId, ClaudePaths.WrnProfileId, StringComparison.OrdinalIgnoreCase) && wrnProfileExists)
                {
                    reason = "WRN third-party inference profile is the applied Claude profile.";
                    return ClaudeMode.Wrn;
                }

                reason = "Claude is in third-party inference mode, but the applied profile is not the WRN profile.";
                return ClaudeMode.ThirdPartyOther;
            }
            catch
            {
                reason = "Claude configuration JSON is malformed or incompatible.";
                return ClaudeMode.Degraded;
            }
        }

        private static string[] GetClaudeProcessLocations()
        {
            var paths = new List<string>();
            foreach (var process in Process.GetProcessesByName("claude"))
            {
                try
                {
                    var location = process.MainModule == null ? null : process.MainModule.FileName;
                    if (!string.IsNullOrWhiteSpace(location))
                        paths.Add(location);
                    else
                        paths.Add("<path unavailable>");
                }
                catch
                {
                    paths.Add("<path unavailable>");
                }
                finally
                {
                    process.Dispose();
                }
            }
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool HasManagedPackageMarker()
        {
            return RegistryContainsClaudePackage(Registry.CurrentUser,
                       @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages")
                   || RegistryContainsClaudePackage(Registry.LocalMachine,
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications");
        }

        private static bool RegistryContainsClaudePackage(RegistryKey root, string path)
        {
            try
            {
                using (var key = root.OpenSubKey(path, false))
                {
                    if (key == null) return false;
                    return key.GetSubKeyNames().Any(delegate(string name)
                    {
                        return name.IndexOf("Claude", StringComparison.OrdinalIgnoreCase) >= 0
                            || name.IndexOf("Anthropic", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                }
            }
            catch
            {
                return false;
            }
        }

        private static string ExplainInstallation(ClaudeInstallKind kind, string[] paths, bool managedPackageMarker)
        {
            if (kind == ClaudeInstallKind.RecoveryDiagnostic)
                return "A diagnostic Claude-Recovery copy is currently running; it must not be used as the production switching baseline.";
            if (kind == ClaudeInstallKind.ManagedPackage)
                return managedPackageMarker
                    ? "A managed-package registration marker was found."
                    : "A running Claude process is located under Program Files\\WindowsApps.";
            if (kind == ClaudeInstallKind.UserInstall)
                return "Claude is running from a per-user application path rather than the expected managed package.";
            if (kind == ClaudeInstallKind.Missing)
                return "No running Claude process or managed-package registration marker was found.";
            return "Claude installation type could not be established safely.";
        }
    }

    internal static class TransitionPlanner
    {
        public static TransitionPlan Plan(ClaudeDiscoverySnapshot snapshot, string targetMode)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            var paths = ClaudePaths.Current();
            var target = string.Equals(targetMode, "wrn", StringComparison.OrdinalIgnoreCase) ? "WRN" : "WTW";
            var allowlist = new[]
            {
                paths.DesktopConfigPath,
                paths.MetaPath,
                paths.WrnProfilePath
            };

            var nonGoals = new[]
            {
                "Do not write Claude conversation history.",
                "Do not read or write Cowork session history.",
                "Do not touch IndexedDB, Local Storage, cookies, account/session data, or user workspace files.",
                "Do not install, uninstall, patch, copy, or re-register Claude.",
                "Do not encode model names or OpenRouter model IDs in the mode transition."
            };

            var blocked = GetBlockReason(snapshot, target);
            if (blocked != null)
            {
                return new TransitionPlan
                {
                    TargetMode = target,
                    Allowed = false,
                    BlockReason = blocked,
                    ClaudeConfigWriteAllowlist = allowlist,
                    PlannedActions = new string[0],
                    ExplicitNonGoals = nonGoals
                };
            }

            string[] actions;
            if (target == "WRN")
            {
                actions = new[]
                {
                    "Capture the proven-safe Claude configuration baseline metadata.",
                    "Stage only the WRN-owned profile and the allowlisted third-party activation fields.",
                    "Validate staged JSON and postconditions before activation.",
                    "Start the WRN loopback gateway only after the configuration preflight passes.",
                    "Atomically activate WRN mode while Claude is closed."
                };
            }
            else
            {
                actions = new[]
                {
                    "Stop or detach WRN-only gateway components.",
                    "Restore only the proven-safe allowlisted Claude configuration fields from baseline.",
                    "Remove or deactivate only WRN-owned profile metadata.",
                    "Validate the restored WTW configuration before activation.",
                    "Leave all Claude history/session stores untouched."
                };
            }

            return new TransitionPlan
            {
                TargetMode = target,
                Allowed = true,
                BlockReason = null,
                ClaudeConfigWriteAllowlist = allowlist,
                PlannedActions = actions,
                ExplicitNonGoals = nonGoals
            };
        }

        private static string GetBlockReason(ClaudeDiscoverySnapshot snapshot, string target)
        {
            if (!snapshot.ReadOnly)
                return "Discovery snapshot is not marked read-only.";
            if (!snapshot.ConfigReadable || snapshot.Mode == ClaudeMode.Degraded)
                return "Claude configuration is unreadable, malformed, or incompatible.";
            if (snapshot.InstallKind != ClaudeInstallKind.ManagedPackage)
                return "Unsupported Claude installation: " + snapshot.InstallKind + ". A healthy managed Claude installation is required.";
            if (snapshot.ClaudeRunning)
                return "Claude is running. Mode transitions are only permitted after a normal Claude exit.";
            if (target == "WRN" && snapshot.Mode != ClaudeMode.Wtw)
                return "WRN activation requires a validated WTW baseline.";
            if (target == "WTW" && snapshot.Mode != ClaudeMode.Wrn)
                return "WTW restoration requires a validated WRN state and captured WTW baseline.";
            return null;
        }

        internal static bool IsAllowlistedClaudeWrite(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            var paths = ClaudePaths.Current();
            return string.Equals(candidate, paths.DesktopConfigPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, paths.MetaPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, paths.WrnProfilePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class DiscoveryReport
    {
        public ClaudeDiscoverySnapshot Discovery { get; set; }
        public TransitionPlan PlanToWrn { get; set; }
        public TransitionPlan PlanToWtw { get; set; }
    }

    internal static class DiscoveryCommand
    {
        public static bool TryRun(string[] args)
        {
            if (args == null || args.Length == 0) return false;

            string reportPath = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--discovery-report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    reportPath = args[i + 1];
                    break;
                }
            }
            if (string.IsNullOrWhiteSpace(reportPath)) return false;

            var snapshot = ClaudeDiscovery.Inspect();
            var report = new DiscoveryReport
            {
                Discovery = snapshot,
                PlanToWrn = TransitionPlanner.Plan(snapshot, "wrn"),
                PlanToWtw = TransitionPlanner.Plan(snapshot, "wtw")
            };

            var json = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 128
            }.Serialize(report);

            var parent = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(reportPath, json, new UTF8Encoding(false));
            return true;
        }
    }
}
