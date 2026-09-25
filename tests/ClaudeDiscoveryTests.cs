using System;
using System.Linq;
using WRN.AIGateway;

internal static class ClaudeDiscoveryTests
{
    private static int _failures;

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

    public static int Main()
    {
        Check("recovery classified",
            ClaudeDiscovery.ClassifyInstallation(
                new[] { @"C:\Users\x\Documents\WindowsApps\Claude-Recovery\claude.exe" }, false)
            == ClaudeInstallKind.RecoveryDiagnostic);

        Check("windowsapps classified managed",
            ClaudeDiscovery.ClassifyInstallation(
                new[] { @"C:\Program Files\WindowsApps\Anthropic.Claude_1.0.0_x64\claude.exe" }, false)
            == ClaudeInstallKind.ManagedPackage);

        Check("registry marker classified managed",
            ClaudeDiscovery.ClassifyInstallation(new string[0], true)
            == ClaudeInstallKind.ManagedPackage);

        Check("local install classified user",
            ClaudeDiscovery.ClassifyInstallation(
                new[] { @"C:\Users\x\AppData\Local\Programs\Claude\claude.exe" }, false)
            == ClaudeInstallKind.UserInstall);

        string reason;
        Check("baseline json classified WTW",
            ClaudeDiscovery.ClassifyModeJson(
                "{\"preferences\":{\"sidebarMode\":\"chat\"}}", null, false, out reason)
            == ClaudeMode.Wtw);

        Check("wrn json classified WRN",
            ClaudeDiscovery.ClassifyModeJson(
                "{\"deploymentMode\":\"3p\"}",
                "{\"appliedId\":\"" + ClaudePaths.WrnProfileId + "\"}",
                true,
                out reason)
            == ClaudeMode.Wrn);

        Check("other third party stays distinct",
            ClaudeDiscovery.ClassifyModeJson(
                "{\"deploymentMode\":\"3p\"}",
                "{\"appliedId\":\"another-profile\"}",
                false,
                out reason)
            == ClaudeMode.ThirdPartyOther);

        Check("malformed json degrades",
            ClaudeDiscovery.ClassifyModeJson("{bad json", null, false, out reason)
            == ClaudeMode.Degraded);

        var healthyWtw = new ClaudeDiscoverySnapshot
        {
            ReadOnly = true,
            InstallKind = ClaudeInstallKind.ManagedPackage,
            ClaudeRunning = false,
            ConfigReadable = true,
            Mode = ClaudeMode.Wtw
        };
        var toWrn = TransitionPlanner.Plan(healthyWtw, "wrn");
        Check("healthy WTW dry run allowed", toWrn.Allowed);
        Check("WRN plan has no model ids",
            !string.Join(" ", toWrn.PlannedActions).Contains("gpt-")
            && !string.Join(" ", toWrn.PlannedActions).Contains("deepseek/")
            && !string.Join(" ", toWrn.PlannedActions).Contains("anthropic/"));

        Check("all planned Claude write paths allowlisted",
            toWrn.ClaudeConfigWriteAllowlist.All(TransitionPlanner.IsAllowlistedClaudeWrite));

        var recovery = new ClaudeDiscoverySnapshot
        {
            ReadOnly = true,
            InstallKind = ClaudeInstallKind.RecoveryDiagnostic,
            ClaudeRunning = false,
            ConfigReadable = true,
            Mode = ClaudeMode.Wtw
        };
        Check("recovery build blocked", !TransitionPlanner.Plan(recovery, "wrn").Allowed);

        var running = new ClaudeDiscoverySnapshot
        {
            ReadOnly = true,
            InstallKind = ClaudeInstallKind.ManagedPackage,
            ClaudeRunning = true,
            ConfigReadable = true,
            Mode = ClaudeMode.Wtw
        };
        Check("running Claude blocked", !TransitionPlanner.Plan(running, "wrn").Allowed);

        var healthyWrn = new ClaudeDiscoverySnapshot
        {
            ReadOnly = true,
            InstallKind = ClaudeInstallKind.ManagedPackage,
            ClaudeRunning = false,
            ConfigReadable = true,
            Mode = ClaudeMode.Wrn
        };
        Check("healthy WRN restore dry run allowed", TransitionPlanner.Plan(healthyWrn, "wtw").Allowed);

        Console.WriteLine(_failures == 0 ? "ALL_DISCOVERY_TESTS_PASS" : "DISCOVERY_TESTS_FAILED=" + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
