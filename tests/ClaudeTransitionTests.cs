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
        TestRollback(baseRoot, catalogue);
        TestStalePreflight(baseRoot, catalogue);
        TestUnsafeSourceBlocked(baseRoot, catalogue);
        TestProfileCollisionBlocked(baseRoot, catalogue);
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
