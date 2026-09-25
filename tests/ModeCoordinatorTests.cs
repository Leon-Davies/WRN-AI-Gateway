using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;

internal static class ModeCoordinatorTests
{
    private static int _failures;
    private static readonly JavaScriptSerializer Json =
        new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 256
        };

    private sealed class Fixture
    {
        public string Root;
        public string StateRoot;
        public ClaudePaths Paths;
        public string LocalGatewayKey;
        public string OpenRouterKey;
        public int Port;
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
        if (args.Length != 2)
        {
            Console.WriteLine("Expected: <app-base-dir> <temp-root>");
            return 2;
        }

        var baseDir = args[0];
        var tempRoot = args[1];
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, true);
        Directory.CreateDirectory(tempRoot);

        TestHealthyWrnPreflight(baseDir, tempRoot);
        TestMissingCredentialBlocked(baseDir, tempRoot);
        TestRecoveryInstallBlocked(baseDir, tempRoot);
        TestPendingRecoveryRequired(baseDir, tempRoot);
        TestWtwRestorePreflight(baseDir, tempRoot);

        Console.WriteLine(
            _failures == 0
                ? "ALL_MODE_COORDINATOR_TESTS_PASS"
                : "MODE_COORDINATOR_TESTS_FAILED=" + _failures);
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
        string tempRoot,
        string name,
        int port)
    {
        var root = Path.Combine(tempRoot, name);
        var local = Path.Combine(root, "LocalAppData");
        var roaming = Path.Combine(root, "RoamingAppData");
        var state = Path.Combine(root, "WRN-State");
        Directory.CreateDirectory(local);
        Directory.CreateDirectory(roaming);
        Directory.CreateDirectory(state);

        var paths = new ClaudePaths(local, roaming);
        Directory.CreateDirectory(paths.ThirdPartyRoot);
        Directory.CreateDirectory(paths.ConfigLibraryPath);

        WriteJson(
            paths.DesktopConfigPath,
            new Dictionary<string, object>
            {
                { "coworkUserFilesPath", @"C:\Users\fixture\Claude" },
                {
                    "preferences",
                    new Dictionary<string, object>
                    {
                        { "fixturePreference", "preserve-me" }
                    }
                }
            });

        WriteJson(
            paths.MetaPath,
            new Dictionary<string, object>
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
                { "appliedId", "existing-profile" }
            });

        var fixture = new Fixture
        {
            Root = root,
            StateRoot = state,
            Paths = paths,
            Port = port,
            LocalGatewayKey =
                "fixture-local-gateway-key-" + Guid.NewGuid().ToString("N"),
            OpenRouterKey =
                "fixture-openrouter-key-" + Guid.NewGuid().ToString("N")
        };

        ProvisionGatewayState(fixture);
        return fixture;
    }

    private static void ProvisionGatewayState(Fixture fixture)
    {
        var gatewayRoot =
            Path.Combine(fixture.StateRoot, "gateway");
        var credentialRoot =
            Path.Combine(fixture.StateRoot, "credentials");
        Directory.CreateDirectory(gatewayRoot);
        Directory.CreateDirectory(credentialRoot);

        WriteJson(
            Path.Combine(gatewayRoot, "gateway.json"),
            new Dictionary<string, object>
            {
                { "LocalApiKey", fixture.LocalGatewayKey },
                { "Port", fixture.Port }
            });

        var plain = Encoding.UTF8.GetBytes(
            fixture.OpenRouterKey);
        var protectedBytes = ProtectedData.Protect(
            plain,
            null,
            DataProtectionScope.CurrentUser);

        try
        {
            File.WriteAllText(
                Path.Combine(
                    credentialRoot,
                    "openrouter.key.dpapi"),
                Convert.ToBase64String(protectedBytes),
                Encoding.ASCII);
        }
        finally
        {
            Array.Clear(plain, 0, plain.Length);
            Array.Clear(
                protectedBytes,
                0,
                protectedBytes.Length);
        }
    }

    private static void TestHealthyWrnPreflight(
        string baseDir,
        string tempRoot)
    {
        var fixture =
            CreateFixture(tempRoot, "healthy-wrn", 58131);

        var report = ModeCoordinator.Inspect(
            "wrn",
            baseDir,
            fixture.StateRoot,
            fixture.Paths,
            HealthyWtw());

        Check(
            "healthy synthetic WRN preflight compatible",
            report.PreflightCompatible);
        Check(
            "signed catalogue validated",
            report.SignedCatalogueValid
            && report.CatalogueRelease >= 1);
        Check(
            "gateway binary found",
            report.GatewayBinaryPresent);
        Check(
            "gateway config valid",
            report.GatewayConfigValid
            && report.GatewayPort == fixture.Port);
        Check(
            "OpenRouter credential decryptable",
            report.OpenRouterCredentialDecryptable);
        Check(
            "transition plan compiled",
            report.TransitionPlanCompiled
            && report.PlannedMutations.Length == 3);
        Check(
            "stopped gateway reported as start-required",
            !report.GatewayHealthy
            && report.GatewayStartRequired);

        var runtime = GatewayLifecycle.EnsureHealthy(
            baseDir,
            fixture.StateRoot,
            report.CatalogueRelease,
            5000);
        Console.WriteLine(
            "GATEWAY_RUNTIME status=" + runtime.Status
            + " healthy=" + runtime.Healthy
            + " started=" + runtime.Started
            + " pid=" + runtime.ProcessId);
        if (!runtime.Healthy)
        {
            var logPath = Path.Combine(
                fixture.StateRoot,
                "gateway",
                "gateway.log");
            if (File.Exists(logPath))
                Console.WriteLine(
                    "GATEWAY_LOG " + File.ReadAllText(logPath));
        }
        Check(
            "owned gateway starts for healthy fixture",
            runtime.Healthy
            && runtime.Started
            && runtime.ProcessId > 0);

        var runningReport = ModeCoordinator.Inspect(
            "wrn",
            baseDir,
            fixture.StateRoot,
            fixture.Paths,
            HealthyWtw());
        Check(
            "running owned gateway becomes healthy preflight",
            runningReport.GatewayHealthy
            && !runningReport.GatewayStartRequired);

        Check(
            "owned gateway stops by verified process identity",
            GatewayLifecycle.StopOwned(
                baseDir,
                fixture.StateRoot));

        var stoppedReport = ModeCoordinator.Inspect(
            "wrn",
            baseDir,
            fixture.StateRoot,
            fixture.Paths,
            HealthyWtw());
        Check(
            "stopped owned gateway returns to start-required",
            !stoppedReport.GatewayHealthy
            && stoppedReport.GatewayStartRequired);

        Check(
            "live execution remains disabled",
            !report.LiveExecutionEnabled
            && !report.LiveExecutionAllowed);

        var serialized = Json.Serialize(report);
        Check(
            "preflight report excludes local gateway credential",
            serialized.IndexOf(
                fixture.LocalGatewayKey,
                StringComparison.Ordinal) < 0);
        Check(
            "preflight report excludes OpenRouter credential",
            serialized.IndexOf(
                fixture.OpenRouterKey,
                StringComparison.Ordinal) < 0);
    }

    private static void TestMissingCredentialBlocked(
        string baseDir,
        string tempRoot)
    {
        var fixture =
            CreateFixture(tempRoot, "missing-credential", 58132);
        File.Delete(
            Path.Combine(
                fixture.StateRoot,
                "credentials",
                "openrouter.key.dpapi"));

        var report = ModeCoordinator.Inspect(
            "wrn",
            baseDir,
            fixture.StateRoot,
            fixture.Paths,
            HealthyWtw());

        Check(
            "missing OpenRouter credential blocks WRN preflight",
            !report.PreflightCompatible
            && report.PreflightBlockReason.Contains(
                "OpenRouter credential"));
    }

    private static void TestRecoveryInstallBlocked(
        string baseDir,
        string tempRoot)
    {
        var fixture =
            CreateFixture(tempRoot, "recovery-install", 58133);
        var discovery = HealthyWtw();
        discovery.InstallKind =
            ClaudeInstallKind.RecoveryDiagnostic;

        var report = ModeCoordinator.Inspect(
            "wrn",
            baseDir,
            fixture.StateRoot,
            fixture.Paths,
            discovery);

        Check(
            "recovery installation blocks coordinator",
            !report.PreflightCompatible
            && report.PreflightBlockReason.Contains(
                "Unsupported Claude installation"));
    }

    private static void TestPendingRecoveryRequired(
        string baseDir,
        string tempRoot)
    {
        var fixture =
            CreateFixture(tempRoot, "pending-recovery", 58134);
        var catalogue = new CatalogueStore(
            baseDir,
            Path.Combine(fixture.StateRoot, "catalogue"))
            .LoadBestAvailable()
            .Catalogue;

        var plan = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            fixture.LocalGatewayKey,
            fixture.Port);

        var transitionRoot =
            Path.Combine(fixture.StateRoot, "transition");
        ClaudeTransitionExecutor.ExecuteInterruptedForTest(
            plan,
            transitionRoot,
            1);

        var blocked = ModeCoordinator.Inspect(
            "wrn",
            baseDir,
            fixture.StateRoot,
            fixture.Paths,
            HealthyWtw());

        Check(
            "pending transition blocks new plan",
            blocked.PendingTransitionPresent
            && !blocked.PreflightCompatible);

        var recovery =
            ModeCoordinator.RecoverBeforePlanning(
                fixture.StateRoot);
        Check(
            "coordinator recovery rolls back pending fixture",
            recovery.RolledBack);

        var after = ModeCoordinator.Inspect(
            "wrn",
            baseDir,
            fixture.StateRoot,
            fixture.Paths,
            HealthyWtw());
        Check(
            "preflight can retry after recovery",
            after.PreflightCompatible
            && !after.PendingTransitionPresent);
    }

    private static void TestWtwRestorePreflight(
        string baseDir,
        string tempRoot)
    {
        var fixture =
            CreateFixture(tempRoot, "healthy-wtw-restore", 58135);
        var catalogue = new CatalogueStore(
            baseDir,
            Path.Combine(fixture.StateRoot, "catalogue"))
            .LoadBestAvailable()
            .Catalogue;
        var transitionRoot =
            Path.Combine(fixture.StateRoot, "transition");

        var activation = ClaudeActivationCompiler.Compile(
            HealthyWtw(),
            fixture.Paths,
            catalogue,
            fixture.LocalGatewayKey,
            fixture.Port);
        Check(
            "WTW restore fixture activates",
            ClaudeTransitionExecutor.Execute(
                activation,
                transitionRoot).Success);

        var report = ModeCoordinator.Inspect(
            "wtw",
            baseDir,
            fixture.StateRoot,
            fixture.Paths,
            HealthyWrn());

        Check(
            "healthy synthetic WTW restore preflight compatible",
            report.PreflightCompatible);
        Check(
            "WTW restore ownership baseline detected",
            report.OwnershipBaselinePresent);
        Check(
            "WTW restore plan compiled",
            report.TransitionPlanCompiled
            && report.TransitionPlanKind.Contains(
                "WRN_TO_WTW"));
        Check(
            "WTW restore still cannot execute live",
            !report.LiveExecutionAllowed);
    }

    private static void WriteJson(
        string path,
        Dictionary<string, object> value)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent))
            Directory.CreateDirectory(parent);

        File.WriteAllText(
            path,
            Json.Serialize(value),
            new UTF8Encoding(false));
    }
}
