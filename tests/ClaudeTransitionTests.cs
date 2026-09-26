using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;

internal static class ClaudeTransitionTests
{
    private static int _failures;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
    {
        MaxJsonLength = int.MaxValue,
        RecursionLimit = 256
    };

    private sealed class Fixture
    {
        public string Root;
        public ClaudePaths Paths;
        public byte[] DesktopBefore;
        public byte[] MetaBefore;
    }

    private static void Check(string name, bool condition)
    {
        if (condition)
            Console.WriteLine("PASS " + name);
        else
        {
            Console.WriteLine("FAIL " + name);
            _failures++;
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.WriteLine("Expected: <catalogue.json> <catalogue.sig> <temp-root>");
            return 2;
        }

        CatalogueDocument catalogue;
        string error;
        var bytes = File.ReadAllBytes(args[0]);
        var signature = File.ReadAllText(args[1]);

        Check(
            "signed catalogue accepted",
            CatalogueVerifier.TryVerifyAndParse(
                bytes,
                signature,
                out catalogue,
                out error));

        if (catalogue == null)
            return 1;

        var baseRoot = args[2];
        if (Directory.Exists(baseRoot))
            Directory.Delete(baseRoot, true);
        Directory.CreateDirectory(baseRoot);

        TestCompileAndApply(baseRoot, catalogue);
        TestMissingConfigLibraryRoundTrip(baseRoot, catalogue);
        TestMissingConfigLibraryInterruptedActivation(baseRoot, catalogue);
        TestMissingConfigLibraryForeignFileFailsClosed(baseRoot, catalogue);
        TestRollback(baseRoot, catalogue);
        TestStalePreflight(baseRoot, catalogue);
        TestUnsafeSourceBlocked(baseRoot, catalogue);
        TestProfileCollisionBlocked(baseRoot, catalogue);
        TestRoundTripPreservesChanges(baseRoot, catalogue);
        TestInterruptedActivationRecovery(baseRoot, catalogue);
        TestCompletedActivationRecovery(baseRoot, catalogue);
        TestInterruptedDeactivationRecovery(baseRoot, catalogue);
        TestRecoveryConflictFailsClosed(baseRoot, catalogue);
        TestDeactivationOwnershipGuard(baseRoot, catalogue);
        TestLivePathGuard(baseRoot);

        Console.WriteLine(
            _failures == 0
                ? "ALL_TRANSITION_TESTS_PASS"
                : "TRANSITION_TESTS_FAILED=" + _failures);

        return _failures == 0 ? 0 : 1;
    }

    private static ClaudeDiscoverySnapshot HealthyWtw()
    {
        return new ClaudeDiscoverySnapshot
        {
            ReadOnly = true,
            InstallKind = ClaudeInstallKind.ManagedPackage,
            ClaudeRunning = false,
            ConfigReadable = true,
            Mode = ClaudeMode.Wtw
        };
    }

    private static ClaudeDiscoverySnapshot HealthyWrn()
    {
        return new ClaudeDiscoverySnapshot
        {
            ReadOnly = true,
            InstallKind = ClaudeInstallKind.ManagedPackage,
            ClaudeRunning = false,
            ConfigReadable = true,
            Mode = ClaudeMode.Wrn
        };
    }

    private static Fixture CreateFixture(
        string baseRoot,
        string name,
        bool createProfile)
    {
        var root = Path.Combine(baseRoot, name);
        if (Directory.Exists(root))
            Directory.Delete(root, true);

        var local = Path.Combine(root, "LocalAppData");
        var roaming = Path.Combine(root, "RoamingAppData");
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(roaming);

        var paths = new ClaudePaths(local, roaming);
        Directory.CreateDirectory(paths.ThirdPartyRoot);
        Directory.CreateDirectory(paths.ConfigLibraryPath);

        var desktop = new Dictionary<string, object>
        {
            { "coworkUserFilesPath", @"C:\Users\fixture\Claude" },
            {
                "preferences",
                new Dictionary<string, object>
                {
                    { "sidebarMode", "chat" },
                    { "fixturePreference", "preserve-me" }
                }
            }
        };

        var meta = new Dictionary<string, object>
        {
            {
                "entries",
                new object[]
                {
                    new Dictionary<string, object>
                    {
                        { "id", "existing-profile" },
                        { "name", "Existing profile" }
                    }
                }
            },
            { "appliedId", "existing-profile" },
            { "fixtureMeta", "preserve-me" }
        };

        WriteJson(paths.DesktopConfigPath, desktop);
        WriteJson(paths.MetaPath, meta);

        if (createProfile)
        {
            WriteJson(
                paths.WrnProfilePath,
                new Dictionary<string, object>
                {
                    { "fixtureCollision", true }
                });
        }

        return new Fixture
        {
            Root = root,
            Paths = paths,
            DesktopBefore = File.ReadAllBytes(paths.DesktopConfigPath),
            MetaBefore = File.ReadAllBytes(paths.MetaPath)
        };
    }

    private static Fixture CreateMissingConfigLibraryFixture(
        string baseRoot,
        string name)
    {
        var root = Path.Combine(baseRoot, name);
        if (Directory.Exists(root))
            Directory.Delete(root, true);

        var local = Path.Combine(root, "LocalAppData");
        var roaming = Path.Combine(root, "RoamingAppData");
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(roaming);

        var paths = new ClaudePaths(local, roaming);
        Directory.CreateDirectory(paths.ThirdPartyRoot);

        return new Fixture
        {
            Root = root,
            Paths = paths,
            DesktopBefore = null,
            MetaBefore = null
        };
    }

    private static void TestMissingConfigLibraryRoundTrip(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateMissingConfigLibraryFixture(
            baseRoot,
            "missing-config-library-roundtrip");
        var stateRoot = Path.Combine(
            fixture.Root,
            "transactions");

        var activation = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "missing-parent-token",
            43127);

        Check(
            "missing configLibrary compiles one owned directory create",
            activation.DirectoryMutations != null
            && activation.DirectoryMutations.Length == 1
            && string.Equals(
                activation.DirectoryMutations[0].Path,
                fixture.Paths.ConfigLibraryPath,
                StringComparison.OrdinalIgnoreCase)
            && !activation.DirectoryMutations[0].ExpectedExists
            && activation.DirectoryMutations[0].DesiredExists);

        var applied = ClaudeTransitionExecutor.Execute(
            activation,
            stateRoot);
        Check(
            "missing configLibrary activation succeeds",
            applied.Success);
        Check(
            "activation creates configLibrary and exactly the WRN config files",
            Directory.Exists(fixture.Paths.ConfigLibraryPath)
            && File.Exists(fixture.Paths.DesktopConfigPath)
            && File.Exists(fixture.Paths.MetaPath)
            && File.Exists(fixture.Paths.WrnProfilePath));

        var deactivation = ClaudeDeactivationCompiler.Compile(
            HealthyWrn(),
            fixture.Paths,
            stateRoot);
        Check(
            "WTW restoration owns removal of WRN-created configLibrary",
            deactivation.DirectoryMutations != null
            && deactivation.DirectoryMutations.Length == 1
            && deactivation.DirectoryMutations[0].ExpectedExists
            && !deactivation.DirectoryMutations[0].DesiredExists);

        var restored = ClaudeTransitionExecutor.Execute(
            deactivation,
            stateRoot);
        Check(
            "missing configLibrary round-trip restores WTW baseline",
            restored.Success
            && Directory.Exists(fixture.Paths.ThirdPartyRoot)
            && !Directory.Exists(fixture.Paths.ConfigLibraryPath)
            && !File.Exists(fixture.Paths.DesktopConfigPath)
            && !File.Exists(fixture.Paths.MetaPath)
            && !File.Exists(fixture.Paths.WrnProfilePath)
            && !File.Exists(
                ClaudeTransitionState.OwnershipBaselinePath(
                    stateRoot)));
    }

    private static void TestMissingConfigLibraryInterruptedActivation(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateMissingConfigLibraryFixture(
            baseRoot,
            "missing-config-library-interrupted");
        var stateRoot = Path.Combine(
            fixture.Root,
            "transactions");
        var activation = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "missing-parent-interrupt-token",
            43127);

        var interrupted =
            ClaudeTransitionExecutor.ExecuteInterruptedForTest(
                activation,
                stateRoot,
                1);
        Check(
            "missing configLibrary interruption publishes recoverable state",
            interrupted.Status == "TRANSITION_INTERRUPTED_FOR_TEST"
            && Directory.Exists(fixture.Paths.ConfigLibraryPath));

        var recovery =
            ClaudeTransitionExecutor.RecoverPending(
                stateRoot);
        Check(
            "missing configLibrary rollback removes WRN-created directory",
            recovery.RolledBack
            && !Directory.Exists(fixture.Paths.ConfigLibraryPath)
            && !File.Exists(fixture.Paths.DesktopConfigPath)
            && !File.Exists(fixture.Paths.MetaPath)
            && !File.Exists(fixture.Paths.WrnProfilePath)
            && !File.Exists(
                ClaudeTransitionState.OwnershipBaselinePath(
                    stateRoot)));
    }

    private static void TestMissingConfigLibraryForeignFileFailsClosed(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateMissingConfigLibraryFixture(
            baseRoot,
            "missing-config-library-foreign-file");
        var stateRoot = Path.Combine(
            fixture.Root,
            "transactions");
        var activation = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "missing-parent-foreign-token",
            43127);

        Check(
            "foreign-file fixture activates",
            ClaudeTransitionExecutor.Execute(
                activation,
                stateRoot).Success);

        var foreignPath = Path.Combine(
            fixture.Paths.ConfigLibraryPath,
            "foreign.json");
        File.WriteAllText(
            foreignPath,
            "{}",
            new UTF8Encoding(false));

        var deactivation = ClaudeDeactivationCompiler.Compile(
            HealthyWrn(),
            fixture.Paths,
            stateRoot);
        var result = ClaudeTransitionExecutor.Execute(
            deactivation,
            stateRoot);

        Check(
            "foreign configLibrary content blocks directory deletion and rolls back",
            !result.Success
            && result.RolledBack
            && result.Status == "TRANSITION_ROLLED_BACK"
            && Directory.Exists(fixture.Paths.ConfigLibraryPath)
            && File.Exists(foreignPath)
            && File.Exists(fixture.Paths.DesktopConfigPath)
            && File.Exists(fixture.Paths.MetaPath)
            && File.Exists(fixture.Paths.WrnProfilePath)
            && File.Exists(
                ClaudeTransitionState.OwnershipBaselinePath(
                    stateRoot)));
    }

    private static void TestCompileAndApply(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(baseRoot, "apply", false);
        var token = "fixture-local-gateway-token";

        var plan = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            token,
            43127);

        Check("plan has exactly three mutations", plan.Mutations.Length == 3);
        Check(
            "profile is staged first",
            string.Equals(
                plan.Mutations[0].Path,
                fixture.Paths.WrnProfilePath,
                StringComparison.OrdinalIgnoreCase));
        Check(
            "meta is staged second",
            string.Equals(
                plan.Mutations[1].Path,
                fixture.Paths.MetaPath,
                StringComparison.OrdinalIgnoreCase));
        Check(
            "deployment trigger is written last",
            string.Equals(
                plan.Mutations[2].Path,
                fixture.Paths.DesktopConfigPath,
                StringComparison.OrdinalIgnoreCase));

        var allowlist = new HashSet<string>(
            plan.AllowlistedPaths,
            StringComparer.OrdinalIgnoreCase);
        Check(
            "allowlist is exactly the three Phase 2A files",
            allowlist.SetEquals(
                new[]
                {
                    fixture.Paths.DesktopConfigPath,
                    fixture.Paths.MetaPath,
                    fixture.Paths.WrnProfilePath
                }));

        var profileJson = Encoding.UTF8.GetString(
            plan.Mutations[0].DesiredBytes);
        Check(
            "generated profile contains no upstream model IDs",
            !catalogue.models.Any(
                delegate(CatalogueModel model)
                {
                    return profileJson.IndexOf(
                        model.upstreamModel,
                        StringComparison.OrdinalIgnoreCase) >= 0;
                }));

        var profile = ParseObject(plan.Mutations[0].DesiredBytes);
        var inferenceModels = ReadObjectArray(profile["inferenceModels"]);
        var defaultModel = catalogue.models.First(
            delegate(CatalogueModel model)
            {
                return string.Equals(
                    model.key,
                    catalogue.defaultModelKey,
                    StringComparison.OrdinalIgnoreCase);
            });

        var firstModel = inferenceModels[0] as Dictionary<string, object>;
        Check(
            "catalogue default alias is first in Claude picker",
            firstModel != null
            && Convert.ToString(firstModel["name"]) == defaultModel.claudeAlias);

        var result = ClaudeTransitionExecutor.Execute(
            plan,
            Path.Combine(fixture.Root, "transactions"));

        Check("fixture transition succeeds", result.Success);
        Check("fixture transition does not roll back", !result.RolledBack);
        Check("all three mutations applied", result.AppliedMutations == 3);

        var desktop = ParseObject(
            File.ReadAllBytes(fixture.Paths.DesktopConfigPath));
        Check(
            "deploymentMode becomes 3p",
            Convert.ToString(desktop["deploymentMode"]) == "3p");
        Check(
            "cowork path preserved",
            Convert.ToString(desktop["coworkUserFilesPath"])
                == @"C:\Users\fixture\Claude");

        var preferences =
            desktop["preferences"] as Dictionary<string, object>;
        Check(
            "unrelated desktop preference preserved",
            preferences != null
            && Convert.ToString(preferences["fixturePreference"])
                == "preserve-me");

        var meta = ParseObject(
            File.ReadAllBytes(fixture.Paths.MetaPath));
        Check(
            "meta appliedId selects WRN profile",
            Convert.ToString(meta["appliedId"]) == ClaudePaths.WrnProfileId);
        Check(
            "unrelated meta field preserved",
            Convert.ToString(meta["fixtureMeta"]) == "preserve-me");

        var entries = ReadObjectArray(meta["entries"]);
        Check(
            "existing config-library entry preserved",
            entries.OfType<Dictionary<string, object>>().Any(
                delegate(Dictionary<string, object> entry)
                {
                    return Convert.ToString(entry["id"])
                        == "existing-profile";
                }));
        Check(
            "WRN config-library entry added",
            entries.OfType<Dictionary<string, object>>().Any(
                delegate(Dictionary<string, object> entry)
                {
                    return Convert.ToString(entry["id"])
                        == ClaudePaths.WrnProfileId;
                }));

        var appliedProfile = ParseObject(
            File.ReadAllBytes(fixture.Paths.WrnProfilePath));
        Check(
            "profile points only to loopback gateway",
            Convert.ToString(appliedProfile["inferenceGatewayBaseUrl"])
                == "http://127.0.0.1:43127");
        Check(
            "profile uses supplied local bearer token",
            Convert.ToString(appliedProfile["inferenceGatewayApiKey"])
                == token);
        Check(
            "profile bearer auth scheme set",
            Convert.ToString(appliedProfile["inferenceGatewayAuthScheme"])
                == "bearer");
        Check(
            "profile enables chat cowork and code surfaces",
            Convert.ToBoolean(appliedProfile["chatTabEnabled"])
            && Convert.ToBoolean(appliedProfile["coworkTabEnabled"])
            && Convert.ToBoolean(
                appliedProfile["isClaudeCodeForDesktopEnabled"]));

        var appliedModels =
            ReadObjectArray(appliedProfile["inferenceModels"]);
        var visibleCount = catalogue.models.Count(
            delegate(CatalogueModel model) { return model.visible; });
        Check(
            "profile contains exactly visible catalogue models",
            appliedModels.Length == visibleCount);
    }

    private static void TestRollback(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(baseRoot, "rollback", false);

        var plan = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "fixture-token",
            43127);

        var result = ClaudeTransitionExecutor.Execute(
            plan,
            Path.Combine(fixture.Root, "transactions"),
            2);

        Check("injected failure does not succeed", !result.Success);
        Check("injected failure rolls back", result.RolledBack);
        Check("two writes were attempted before failure", result.AppliedMutations == 2);
        Check(
            "desktop bytes restored after rollback",
            fixture.DesktopBefore.SequenceEqual(
                File.ReadAllBytes(fixture.Paths.DesktopConfigPath)));
        Check(
            "meta bytes restored after rollback",
            fixture.MetaBefore.SequenceEqual(
                File.ReadAllBytes(fixture.Paths.MetaPath)));
        Check(
            "new WRN profile removed after rollback",
            !File.Exists(fixture.Paths.WrnProfilePath));
    }

    private static void TestStalePreflight(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(baseRoot, "stale", false);

        var plan = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "fixture-token",
            43127);

        File.AppendAllText(
            fixture.Paths.DesktopConfigPath,
            Environment.NewLine + " ");

        var blocked = false;
        try
        {
            ClaudeTransitionExecutor.Execute(
                plan,
                Path.Combine(fixture.Root, "transactions"));
        }
        catch (InvalidOperationException ex)
        {
            blocked = ex.Message == "TRANSITION_SOURCE_HASH_CHANGED";
        }

        Check("stale source hash blocks transition", blocked);
        Check(
            "stale preflight creates no WRN profile",
            !File.Exists(fixture.Paths.WrnProfilePath));
    }

    private static void TestUnsafeSourceBlocked(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(baseRoot, "unsafe", false);
        var discovery = HealthyWtw();
        discovery.InstallKind = ClaudeInstallKind.RecoveryDiagnostic;

        var blocked = false;
        try
        {
            ClaudeActivationCompiler.Compile(
                discovery,
                fixture.Paths,
                catalogue,
                "fixture-token",
                43127);
        }
        catch (InvalidOperationException ex)
        {
            blocked = ex.Message == "WRN_ACTIVATION_SOURCE_STATE_NOT_SAFE";
        }

        Check("recovery build cannot compile live transition", blocked);

        discovery = HealthyWtw();
        discovery.ClaudeRunning = true;
        blocked = false;
        try
        {
            ClaudeActivationCompiler.Compile(
                discovery,
                fixture.Paths,
                catalogue,
                "fixture-token",
                43127);
        }
        catch (InvalidOperationException ex)
        {
            blocked = ex.Message == "WRN_ACTIVATION_SOURCE_STATE_NOT_SAFE";
        }

        Check("running Claude cannot compile transition", blocked);
    }

    private static void TestProfileCollisionBlocked(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(baseRoot, "collision", true);

        var blocked = false;
        try
        {
            ClaudeActivationCompiler.Compile(
                HealthyWtw(),
                fixture.Paths,
                catalogue,
                "fixture-token",
                43127);
        }
        catch (InvalidOperationException ex)
        {
            blocked = ex.Message == "WRN_PROFILE_COLLISION_AT_WTW_BASELINE";
        }

        Check("pre-existing WRN profile collision blocked", blocked);
    }

    private static void TestRoundTripPreservesChanges(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(baseRoot, "roundtrip", false);
        var stateRoot = Path.Combine(fixture.Root, "transactions");

        var activation = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "roundtrip-token",
            43127);

        var activated = ClaudeTransitionExecutor.Execute(
            activation,
            stateRoot);
        Check("roundtrip activation succeeds", activated.Success);
        Check(
            "activation ownership baseline persisted",
            File.Exists(
                ClaudeTransitionState.OwnershipBaselinePath(
                    stateRoot)));

        var ownershipBaselineText = File.ReadAllText(
            ClaudeTransitionState.OwnershipBaselinePath(
                stateRoot));
        Check(
            "ownership baseline excludes Claude preference snapshots",
            ownershipBaselineText.IndexOf(
                "fixturePreference",
                StringComparison.OrdinalIgnoreCase) < 0
            && ownershipBaselineText.IndexOf(
                "coworkUserFilesPath",
                StringComparison.OrdinalIgnoreCase) < 0);
        Check(
            "ownership baseline excludes local gateway credential",
            ownershipBaselineText.IndexOf(
                "roundtrip-token",
                StringComparison.Ordinal) < 0);

        var desktop = ParseObject(
            File.ReadAllBytes(fixture.Paths.DesktopConfigPath));
        var preferences =
            desktop["preferences"] as Dictionary<string, object>;
        preferences["fixturePreference"] = "changed-during-wrn";
        preferences["newDuringWrn"] = "keep-this";
        WriteJson(fixture.Paths.DesktopConfigPath, desktop);

        var meta = ParseObject(
            File.ReadAllBytes(fixture.Paths.MetaPath));
        meta["runtimeMeta"] = "changed-during-wrn";
        var entries = ReadObjectArray(meta["entries"]).ToList();
        entries.Add(
            new Dictionary<string, object>
            {
                { "id", "added-during-wrn" },
                { "name", "Added during WRN" }
            });
        meta["entries"] = entries.ToArray();
        WriteJson(fixture.Paths.MetaPath, meta);

        var deactivation = ClaudeDeactivationCompiler.Compile(
            HealthyWrn(),
            fixture.Paths,
            stateRoot);

        Check(
            "WTW restore deactivates deployment mode first",
            string.Equals(
                deactivation.Mutations[0].Path,
                fixture.Paths.DesktopConfigPath,
                StringComparison.OrdinalIgnoreCase));
        Check(
            "WTW restore updates metadata second",
            string.Equals(
                deactivation.Mutations[1].Path,
                fixture.Paths.MetaPath,
                StringComparison.OrdinalIgnoreCase));
        Check(
            "WTW restore removes WRN profile last",
            string.Equals(
                deactivation.Mutations[2].Path,
                fixture.Paths.WrnProfilePath,
                StringComparison.OrdinalIgnoreCase)
            && !deactivation.Mutations[2].DesiredExists);

        var restored = ClaudeTransitionExecutor.Execute(
            deactivation,
            stateRoot);
        Check("roundtrip WTW restoration succeeds", restored.Success);

        desktop = ParseObject(
            File.ReadAllBytes(fixture.Paths.DesktopConfigPath));
        Check(
            "deploymentMode absence restored field-by-field",
            !desktop.ContainsKey("deploymentMode"));

        preferences =
            desktop["preferences"] as Dictionary<string, object>;
        Check(
            "preference changed during WRN preserved",
            Convert.ToString(preferences["fixturePreference"])
                == "changed-during-wrn");
        Check(
            "new preference created during WRN preserved",
            Convert.ToString(preferences["newDuringWrn"])
                == "keep-this");

        meta = ParseObject(
            File.ReadAllBytes(fixture.Paths.MetaPath));
        Check(
            "pre-WRN appliedId restored",
            Convert.ToString(meta["appliedId"])
                == "existing-profile");
        Check(
            "metadata changed during WRN preserved",
            Convert.ToString(meta["runtimeMeta"])
                == "changed-during-wrn");

        entries = ReadObjectArray(meta["entries"]).ToList();
        Check(
            "original non-WRN profile preserved",
            entries.OfType<Dictionary<string, object>>().Any(
                delegate(Dictionary<string, object> entry)
                {
                    return Convert.ToString(entry["id"])
                        == "existing-profile";
                }));
        Check(
            "profile added during WRN preserved",
            entries.OfType<Dictionary<string, object>>().Any(
                delegate(Dictionary<string, object> entry)
                {
                    return Convert.ToString(entry["id"])
                        == "added-during-wrn";
                }));
        Check(
            "only WRN metadata entry removed",
            !entries.OfType<Dictionary<string, object>>().Any(
                delegate(Dictionary<string, object> entry)
                {
                    return Convert.ToString(entry["id"])
                        == ClaudePaths.WrnProfileId;
                }));
        Check(
            "WRN-owned profile removed",
            !File.Exists(fixture.Paths.WrnProfilePath));
        Check(
            "ownership baseline removed after WTW restoration",
            !File.Exists(
                ClaudeTransitionState.OwnershipBaselinePath(
                    stateRoot)));
    }

    private static void TestInterruptedActivationRecovery(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(
            baseRoot,
            "activation-crash-partial",
            false);
        var stateRoot =
            Path.Combine(fixture.Root, "transactions");

        var plan = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "crash-token",
            43127);

        var interrupted =
            ClaudeTransitionExecutor.ExecuteInterruptedForTest(
                plan,
                stateRoot,
                2);

        Check(
            "partial activation interruption retained pending journal",
            interrupted.Status == "TRANSITION_INTERRUPTED_FOR_TEST"
            && Directory.Exists(
                ClaudeTransitionState.PendingTransactionPath(
                    stateRoot)));

        var recovery =
            ClaudeTransitionExecutor.RecoverPending(
                stateRoot);
        Check(
            "partial activation recovery rolls back",
            recovery.RolledBack
            && recovery.Status
                == "TRANSITION_RECOVERY_ROLLED_BACK");
        Check(
            "crash recovery restores desktop bytes",
            fixture.DesktopBefore.SequenceEqual(
                File.ReadAllBytes(
                    fixture.Paths.DesktopConfigPath)));
        Check(
            "crash recovery restores meta bytes",
            fixture.MetaBefore.SequenceEqual(
                File.ReadAllBytes(
                    fixture.Paths.MetaPath)));
        Check(
            "crash recovery removes new WRN profile",
            !File.Exists(fixture.Paths.WrnProfilePath));
        Check(
            "crash recovery removes activation baseline",
            !File.Exists(
                ClaudeTransitionState.OwnershipBaselinePath(
                    stateRoot)));
        Check(
            "crash recovery clears pending transaction",
            !Directory.Exists(
                ClaudeTransitionState.PendingTransactionPath(
                    stateRoot)));
    }

    private static void TestCompletedActivationRecovery(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(
            baseRoot,
            "activation-crash-complete",
            false);
        var stateRoot =
            Path.Combine(fixture.Root, "transactions");

        var plan = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "complete-crash-token",
            43127);

        var interrupted =
            ClaudeTransitionExecutor.ExecuteInterruptedForTest(
                plan,
                stateRoot,
                3);
        Check(
            "complete activation can be interrupted before journal cleanup",
            interrupted.Status == "TRANSITION_INTERRUPTED_FOR_TEST");

        var recovery =
            ClaudeTransitionExecutor.RecoverPending(
                stateRoot);
        Check(
            "fully-applied activation recovery finalizes",
            recovery.Success
            && !recovery.RolledBack
            && recovery.Status
                == "TRANSITION_RECOVERY_FINALIZED");

        var desktop = ParseObject(
            File.ReadAllBytes(fixture.Paths.DesktopConfigPath));
        Check(
            "finalized recovery leaves WRN deployment active",
            Convert.ToString(desktop["deploymentMode"])
                == "3p");
        Check(
            "finalized recovery retains ownership baseline",
            File.Exists(
                ClaudeTransitionState.OwnershipBaselinePath(
                    stateRoot)));
    }

    private static void TestInterruptedDeactivationRecovery(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(
            baseRoot,
            "deactivation-crash",
            false);
        var stateRoot =
            Path.Combine(fixture.Root, "transactions");

        var activation = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "deactivation-crash-token",
            43127);
        Check(
            "deactivation crash fixture activates first",
            ClaudeTransitionExecutor.Execute(
                activation,
                stateRoot).Success);

        var deactivation = ClaudeDeactivationCompiler.Compile(
            HealthyWrn(),
            fixture.Paths,
            stateRoot);

        var interrupted =
            ClaudeTransitionExecutor.ExecuteInterruptedForTest(
                deactivation,
                stateRoot,
                2);
        Check(
            "partial WTW restore interruption retained pending journal",
            interrupted.Status == "TRANSITION_INTERRUPTED_FOR_TEST");

        var recovery =
            ClaudeTransitionExecutor.RecoverPending(
                stateRoot);
        Check(
            "partial WTW restore recovers back to WRN",
            recovery.RolledBack);

        var desktop = ParseObject(
            File.ReadAllBytes(fixture.Paths.DesktopConfigPath));
        var meta = ParseObject(
            File.ReadAllBytes(fixture.Paths.MetaPath));
        Check(
            "reverse crash recovery reactivates deploymentMode",
            Convert.ToString(desktop["deploymentMode"])
                == "3p");
        Check(
            "reverse crash recovery reselects WRN profile",
            Convert.ToString(meta["appliedId"])
                == ClaudePaths.WrnProfileId);
        Check(
            "reverse crash recovery restores WRN profile",
            File.Exists(fixture.Paths.WrnProfilePath));
        Check(
            "reverse crash recovery retains ownership baseline",
            File.Exists(
                ClaudeTransitionState.OwnershipBaselinePath(
                    stateRoot)));

        var retry = ClaudeDeactivationCompiler.Compile(
            HealthyWrn(),
            fixture.Paths,
            stateRoot);
        Check(
            "WTW restore can retry after recovery",
            ClaudeTransitionExecutor.Execute(
                retry,
                stateRoot).Success);
    }

    private static void TestRecoveryConflictFailsClosed(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(
            baseRoot,
            "recovery-conflict",
            false);
        var stateRoot =
            Path.Combine(fixture.Root, "transactions");

        var plan = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "conflict-token",
            43127);

        ClaudeTransitionExecutor.ExecuteInterruptedForTest(
            plan,
            stateRoot,
            1);

        var meta = ParseObject(
            File.ReadAllBytes(fixture.Paths.MetaPath));
        meta["unexpectedExternalChange"] = true;
        WriteJson(fixture.Paths.MetaPath, meta);
        var conflictBytes =
            File.ReadAllBytes(fixture.Paths.MetaPath);

        var blocked = false;
        try
        {
            ClaudeTransitionExecutor.RecoverPending(
                stateRoot);
        }
        catch (IOException ex)
        {
            blocked = ex.Message
                == "TRANSITION_RECOVERY_CONFLICT";
        }

        Check(
            "unexpected crash-recovery state fails closed",
            blocked);
        Check(
            "recovery conflict does not overwrite unexpected file",
            conflictBytes.SequenceEqual(
                File.ReadAllBytes(
                    fixture.Paths.MetaPath)));
    }

    private static void TestDeactivationOwnershipGuard(
        string baseRoot,
        CatalogueDocument catalogue)
    {
        var fixture = CreateFixture(
            baseRoot,
            "ownership-guard",
            false);
        var stateRoot =
            Path.Combine(fixture.Root, "transactions");

        var activation = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            "ownership-token",
            43127);
        Check(
            "ownership guard fixture activates",
            ClaudeTransitionExecutor.Execute(
                activation,
                stateRoot).Success);

        File.AppendAllText(
            fixture.Paths.WrnProfilePath,
            Environment.NewLine + " ");

        var blocked = false;
        try
        {
            ClaudeDeactivationCompiler.Compile(
                HealthyWrn(),
                fixture.Paths,
                stateRoot);
        }
        catch (InvalidOperationException ex)
        {
            blocked = ex.Message
                == "WRN_PROFILE_CHANGED_SINCE_ACTIVATION";
        }

        Check(
            "changed WRN-owned profile blocks WTW restoration",
            blocked);
    }

    private static void TestLivePathGuard(string baseRoot)
    {
        var paths = ClaudePaths.Current();
        var actualPaths = new[]
        {
            paths.WrnProfilePath,
            paths.MetaPath,
            paths.DesktopConfigPath
        };

        var before = actualPaths.ToDictionary(
            delegate(string path) { return path; },
            delegate(string path)
            {
                return File.Exists(path)
                    ? TransitionHash.Sha256(File.ReadAllBytes(path))
                    : null;
            },
            StringComparer.OrdinalIgnoreCase);

        var mutations = actualPaths.Select(
            delegate(string path)
            {
                var desired = Encoding.UTF8.GetBytes("{}");
                return new TransitionMutation
                {
                    Path = path,
                    ExpectedExists = File.Exists(path),
                    ExpectedSha256 = File.Exists(path)
                        ? TransitionHash.Sha256(File.ReadAllBytes(path))
                        : null,
                    DesiredExists = true,
                    DesiredBytes = desired,
                    DesiredSha256 = TransitionHash.Sha256(desired),
                    Purpose = "guard-test"
                };
            }).ToArray();

        var plan = new ClaudeActivationPlan
        {
            Kind = "LIVE_GUARD_TEST",
            Mutations = mutations,
            AllowlistedPaths = actualPaths,
            ExplicitNonGoals = new string[0]
        };

        var blocked = false;
        try
        {
            ClaudeTransitionExecutor.Execute(
                plan,
                Path.Combine(baseRoot, "live-guard-transactions"));
        }
        catch (InvalidOperationException ex)
        {
            blocked = ex.Message
                == "LIVE_CLAUDE_WRITES_DISABLED_PENDING_MANAGED_QUALIFICATION";
        }

        Check("actual Claude paths are hard-disabled", blocked);

        foreach (var path in actualPaths)
        {
            var after = File.Exists(path)
                ? TransitionHash.Sha256(File.ReadAllBytes(path))
                : null;
            Check(
                "live guard leaves actual file untouched: "
                    + Path.GetFileName(path),
                string.Equals(
                    before[path],
                    after,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private static Dictionary<string, object> ParseObject(byte[] bytes)
    {
        return Json.DeserializeObject(
            Encoding.UTF8.GetString(bytes))
            as Dictionary<string, object>;
    }

    private static object[] ReadObjectArray(object value)
    {
        var array = value as object[];
        if (array != null) return array;

        var list = value as ArrayList;
        if (list != null) return list.Cast<object>().ToArray();

        return new object[0];
    }

    private static void WriteJson(
        string path,
        Dictionary<string, object> value)
    {
        var bytes = new UTF8Encoding(false).GetBytes(
            Json.Serialize(value));
        File.WriteAllBytes(path, bytes);
    }
}