using System;
using System.IO;
using System.Linq;
using WRN.AIGateway;

internal static class AppUpdateRuntimeTests
{
    private static int _failures;

    private static void Check(
        string name,
        bool condition)
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
        if (args.Length != 1)
        {
            Console.WriteLine(
                "Expected: <temp-root>");
            return 2;
        }

        var root =
            Path.GetFullPath(args[0]);

        if (Directory.Exists(root))
            Directory.Delete(root, true);

        Directory.CreateDirectory(root);

        try
        {
            TestInstalledContext(root);
            TestPersistentUpdaterHelper(root);
            TestDevelopmentActivationBlocked(root);

            Console.WriteLine(
                _failures == 0
                    ? "ALL_APP_UPDATE_RUNTIME_TESTS_PASS"
                    : "APP_UPDATE_RUNTIME_TESTS_FAILED="
                        + _failures);

            return _failures == 0 ? 0 : 1;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void TestInstalledContext(
        string root)
    {
        var install =
            Path.Combine(root, "install");
        var current =
            Path.Combine(install, "current");
        var dist =
            Path.Combine(install, "dist");

        Directory.CreateDirectory(current);
        Directory.CreateDirectory(dist);

        string resolved;
        Check(
            "fixture current directory accepted as installed context",
            AppUpdateRuntime.TryGetInstalledContext(
                current,
                install,
                out resolved)
            && string.Equals(
                Path.GetFullPath(resolved),
                Path.GetFullPath(install),
                StringComparison.OrdinalIgnoreCase));

        Check(
            "development-style sibling directory rejected",
            !AppUpdateRuntime.TryGetInstalledContext(
                dist,
                install,
                out resolved));

        var lookalike =
            Path.Combine(
                root,
                "install-other",
                "current");
        Directory.CreateDirectory(lookalike);

        Check(
            "lookalike current directory outside install root rejected",
            !AppUpdateRuntime.TryGetInstalledContext(
                lookalike,
                install,
                out resolved));
    }

    private static void TestPersistentUpdaterHelper(
        string root)
    {
        var install =
            Path.Combine(root, "helper-install");
        var current =
            Path.Combine(install, "current");
        Directory.CreateDirectory(current);

        var source =
            Path.Combine(
                current,
                "WRN-AI-Gateway-Updater.exe");

        var first =
            Enumerable.Range(0, 128)
                .Select(
                    delegate(int value)
                    {
                        return (byte)(
                            value % 251);
                    })
                .ToArray();

        File.WriteAllBytes(
            source,
            first);

        var prepared =
            AppUpdateRuntime.PrepareUpdaterHelper(
                current,
                install);

        Check(
            "updater helper prepared",
            File.Exists(prepared));

        Check(
            "updater helper copied outside current tree",
            !Path.GetFullPath(prepared)
                .StartsWith(
                    Path.GetFullPath(current)
                        .TrimEnd(
                            Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));

        Check(
            "persistent updater helper is under WRN updater directory",
            string.Equals(
                Path.GetDirectoryName(
                    Path.GetFullPath(prepared)),
                Path.GetFullPath(
                    Path.Combine(
                        install,
                        "updater")),
                StringComparison.OrdinalIgnoreCase));

        Check(
            "prepared helper bytes match source",
            File.ReadAllBytes(source)
                .SequenceEqual(
                    File.ReadAllBytes(prepared)));

        var second =
            Enumerable.Range(0, 193)
                .Select(
                    delegate(int value)
                    {
                        return (byte)(
                            (value * 7) % 251);
                    })
                .ToArray();

        File.WriteAllBytes(
            source,
            second);

        var replaced =
            AppUpdateRuntime.PrepareUpdaterHelper(
                current,
                install);

        Check(
            "existing persistent helper safely replaced",
            string.Equals(
                prepared,
                replaced,
                StringComparison.OrdinalIgnoreCase)
            && File.ReadAllBytes(replaced)
                .SequenceEqual(second));
    }

    private static void TestDevelopmentActivationBlocked(
        string root)
    {
        var development =
            Path.Combine(
                root,
                "development",
                "dist");
        var candidate =
            Path.Combine(
                root,
                "development",
                "updates",
                "staged",
                "candidate",
                "app");

        Directory.CreateDirectory(
            development);
        Directory.CreateDirectory(
            candidate);

        var result =
            AppUpdateRuntime.StartActivation(
                development,
                candidate);

        Check(
            "development build cannot launch installed-app activation",
            !result.Started
            && result.Status
                == "UPDATE_NOT_INSTALLED_CONTEXT");
    }
}
