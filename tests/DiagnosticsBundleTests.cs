using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;

internal static class DiagnosticsBundleTests
{
    private static int _failures;
    private static readonly JavaScriptSerializer Json =
        new JavaScriptSerializer();

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
        if (args.Length != 3)
        {
            Console.WriteLine(
                "Expected: <app-base-dir> <state-root> <output-root>");
            return 2;
        }

        var baseDir = args[0];
        var stateRoot = args[1];
        var outputRoot = args[2];

        if (Directory.Exists(stateRoot))
            Directory.Delete(stateRoot, true);
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, true);

        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(outputRoot);

        SeedSensitiveFixture(stateRoot);

        var result =
            DiagnosticsBundle.Create(
                baseDir,
                stateRoot,
                outputRoot);

        Check(
            "diagnostics bundle created",
            result.Success
            && result.Status
                == "DIAGNOSTICS_CREATED"
            && File.Exists(result.Path));

        if (!result.Success
            || !File.Exists(result.Path))
        {
            return 1;
        }

        using (var archive =
            ZipFile.OpenRead(result.Path))
        {
            var names =
                archive.Entries
                    .Select(delegate(ZipArchiveEntry e)
                    {
                        return e.FullName;
                    })
                    .OrderBy(delegate(string x)
                    {
                        return x;
                    })
                    .ToArray();

            Check(
                "bundle contains only allowlisted files",
                names.SequenceEqual(
                    new[]
                    {
                        "diagnostics.json",
                        "gateway.log",
                        "update-last-activation.json"
                    }));

            var diagnostics =
                ReadEntry(
                    archive,
                    "diagnostics.json");
            var gateway =
                ReadEntry(
                    archive,
                    "gateway.log");
            var update =
                ReadEntry(
                    archive,
                    "update-last-activation.json");

            Check(
                "safe runtime metadata included",
                diagnostics.IndexOf(
                    "\"schemaVersion\":1",
                    StringComparison.Ordinal) >= 0
                && diagnostics.IndexOf(
                    "\"privacyNote\"",
                    StringComparison.Ordinal) >= 0
                && diagnostics.IndexOf(
                    "\"claudeInstallKind\"",
                    StringComparison.Ordinal) >= 0
                && diagnostics.IndexOf(
                    "\"catalogueRelease\"",
                    StringComparison.Ordinal) >= 0
                && diagnostics.IndexOf(
                    "\"credentialConfigured\"",
                    StringComparison.Ordinal) >= 0);

            Check(
                "credential plaintext is not present",
                AllAbsent(
                    diagnostics,
                    gateway,
                    update,
                    "sk-or-fixture-super-secret-token",
                    "fixture-openrouter-credential",
                    "fixture-local-bearer-token"));

            Check(
                "prompt and Claude content are not copied",
                AllAbsent(
                    diagnostics,
                    gateway,
                    update,
                    "CONFIDENTIAL_PROMPT_CONTENT",
                    "CLAUDE_HISTORY_SENTINEL"));

            Check(
                "gateway log secret-like fields are redacted",
                gateway.IndexOf(
                    "[redacted]",
                    StringComparison.Ordinal) >= 0
                && gateway.IndexOf(
                    "SAFE_GATEWAY_STATUS",
                    StringComparison.Ordinal) >= 0
                && gateway.IndexOf(
                    "sk-or-fixture-super-secret-token",
                    StringComparison.Ordinal) < 0
                && gateway.IndexOf(
                    "Bearer fixture-local-bearer-token",
                    StringComparison.Ordinal) < 0);

            Check(
                "safe updater audit retained",
                update.IndexOf(
                    "UPDATE_ACTIVATED",
                    StringComparison.Ordinal) >= 0);
        }

        Check(
            "sanitizer handles common secret shapes",
            DiagnosticsBundle.SanitizeLine(
                "api key = value-123")
                .IndexOf(
                    "value-123",
                    StringComparison.Ordinal) < 0
            && DiagnosticsBundle.SanitizeLine(
                "Bearer token-456")
                .IndexOf(
                    "token-456",
                    StringComparison.Ordinal) < 0);

        Console.WriteLine(
            _failures == 0
                ? "ALL_DIAGNOSTICS_BUNDLE_TESTS_PASS"
                : "DIAGNOSTICS_BUNDLE_TESTS_FAILED="
                    + _failures);

        return _failures == 0 ? 0 : 1;
    }

    private static void SeedSensitiveFixture(
        string stateRoot)
    {
        var gatewayRoot =
            Path.Combine(
                stateRoot,
                "gateway");
        Directory.CreateDirectory(gatewayRoot);
        File.WriteAllText(
            Path.Combine(
                gatewayRoot,
                "gateway.log"),
            "SAFE_GATEWAY_STATUS ready"
            + Environment.NewLine
            + "Authorization: Bearer fixture-local-bearer-token"
            + Environment.NewLine
            + "api key = fixture-openrouter-credential"
            + Environment.NewLine
            + "sk-or-fixture-super-secret-token",
            Encoding.UTF8);

        var updatesRoot =
            Path.Combine(
                stateRoot,
                "updates");
        Directory.CreateDirectory(updatesRoot);
        File.WriteAllText(
            Path.Combine(
                updatesRoot,
                "last-activation.json"),
            "{\"status\":\"UPDATE_ACTIVATED\",\"success\":true}",
            Encoding.UTF8);

        var credentialsRoot =
            Path.Combine(
                stateRoot,
                "credentials");
        Directory.CreateDirectory(credentialsRoot);
        File.WriteAllText(
            Path.Combine(
                credentialsRoot,
                "openrouter.key.dpapi"),
            "fixture-openrouter-credential",
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(
                stateRoot,
                "prompt.txt"),
            "CONFIDENTIAL_PROMPT_CONTENT",
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(
                stateRoot,
                "claude-history.db"),
            "CLAUDE_HISTORY_SENTINEL",
            Encoding.UTF8);
    }

    private static string ReadEntry(
        ZipArchive archive,
        string name)
    {
        var entry =
            archive.GetEntry(name);

        if (entry == null)
            return string.Empty;

        using (var stream =
            entry.Open())
        using (var reader =
            new StreamReader(
                stream,
                Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }

    private static bool AllAbsent(
        params string[] values)
    {
        if (values == null
            || values.Length < 2)
        {
            return true;
        }

        var needles =
            values.Skip(
                values.Length / 2)
                .ToArray();
        var haystacks =
            values.Take(
                values.Length / 2)
                .ToArray();

        foreach (var haystack in haystacks)
        {
            foreach (var needle in needles)
            {
                if ((haystack ?? string.Empty)
                    .IndexOf(
                        needle,
                        StringComparison.Ordinal)
                    >= 0)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
