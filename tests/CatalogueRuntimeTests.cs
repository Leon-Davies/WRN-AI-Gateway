using System;
using System.IO;
using System.Linq;
using System.Text;
using WRN.AIGateway;

internal static class CatalogueRuntimeTests
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

    public static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.WriteLine("Expected: <bundle-dir> <v2-json> <v2-sig> <temp-root>");
            return 2;
        }

        var bundle = args[0];
        var v2Json = args[1];
        var v2Sig = args[2];
        var root = args[3];

        var v1Bytes = File.ReadAllBytes(Path.Combine(bundle, "catalogue.json"));
        var v1Sig = File.ReadAllText(Path.Combine(bundle, "catalogue.sig"));
        CatalogueDocument v1;
        string error;
        Check("bundled signature valid",
            CatalogueVerifier.TryVerifyAndParse(v1Bytes, v1Sig, out v1, out error));
        Check("bundled release is 1", v1 != null && v1.release == 1);
        Check("four visible candidate models",
            v1 != null && v1.models.Count(m => m.visible) == 4);
        Check("Luna is default",
            v1 != null && v1.defaultModelKey == "gpt6-luna");
        Check("all catalogue models require ZDR",
            v1 != null && v1.models.All(m => m.zdrRequired));

        var legacyAliases = v1.models
            .Select(m => m.claudeAlias)
            .ToArray();
        foreach (var model in v1.models)
        {
            if (model.claudeAlias.StartsWith(
                "anthropic/",
                StringComparison.OrdinalIgnoreCase))
            {
                model.claudeAlias =
                    model.claudeAlias.Substring(
                        "anthropic/".Length);
            }
        }
        Check(
            "current Claude-compatible WRN aliases accepted",
            CatalogueValidator.Validate(v1, out error)
            && v1.models.All(m =>
                m.claudeAlias.StartsWith(
                    "claude-wrn-",
                    StringComparison.OrdinalIgnoreCase)));

        v1.models[0].claudeAlias = "openai/not-a-wrn-alias";
        Check(
            "unscoped arbitrary alias rejected",
            !CatalogueValidator.Validate(v1, out error)
            && error == "CATALOGUE_ALIAS_INVALID");

        for (var i = 0; i < v1.models.Length; i++)
            v1.models[i].claudeAlias = legacyAliases[i];
        Check(
            "legacy WRN aliases remain accepted during migration",
            CatalogueValidator.Validate(v1, out error));

        var originalRelease = v1.release;
        var deepSeek = v1.models.First(m =>
            m.key == "deepseek-v41-flash");
        var originalDeepSeekAlias = deepSeek.claudeAlias;

        v1.release = 6;
        deepSeek.claudeAlias = "claude-wrn-deepseek";
        Check(
            "release 6 rejects Claude-incompatible provider route names",
            !CatalogueValidator.Validate(v1, out error)
            && error == "CATALOGUE_ALIAS_CLAUDE_DESKTOP_INCOMPATIBLE");

        deepSeek.claudeAlias = "claude-wrn-m004";
        Check(
            "release 6 accepts provider-neutral opaque route IDs",
            CatalogueValidator.Validate(v1, out error));

        var originalMinimumAppVersion = v1.minimumAppVersion;
        v1.minimumAppVersion = "0.6.0";
        Check(
            "phase 6 catalogue compatibility version accepted",
            CatalogueValidator.Validate(v1, out error));

        v1.minimumAppVersion = "0.6.1";
        Check(
            "future catalogue compatibility version rejected",
            !CatalogueValidator.Validate(v1, out error)
            && error == "CATALOGUE_APP_TOO_OLD");

        v1.minimumAppVersion = originalMinimumAppVersion;
        v1.release = originalRelease;
        deepSeek.claudeAlias = originalDeepSeekAlias;

        var tampered = (byte[])v1Bytes.Clone();
        tampered[tampered.Length / 2] ^= 0x01;
        CatalogueDocument ignored;
        Check("tamper rejected",
            !CatalogueVerifier.TryVerifyAndParse(tampered, v1Sig, out ignored, out error)
            && error == "CATALOGUE_SIGNATURE_INVALID");

        if (Directory.Exists(root)) Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        var store = new CatalogueStore(Directory.GetParent(bundle).FullName, root);
        var initial = store.LoadBestAvailable();
        Check("store starts from bundled release", initial.Catalogue.release == 1 && initial.Source == "bundled");

        var v2Bytes = File.ReadAllBytes(v2Json);
        var v2Signature = File.ReadAllText(v2Sig);
        var promoted = store.AcceptCandidate(v2Bytes, v2Signature);
        Check("v2 promotes", promoted.Success && promoted.Changed && promoted.CandidateRelease == 2);
        var current = store.LoadBestAvailable();
        Check("v2 becomes cached current", current.Catalogue.release == 2 && current.Source == "cached-current");
        Check("remote metadata visible",
            current.Catalogue.models.First(m => m.key == "gpt6-luna").tagline.Contains("remotely updated"));

        var replay = store.AcceptCandidate(v2Bytes, v2Signature);
        Check("identical current release is no-op",
            replay.Success && !replay.Changed && replay.Status == "CATALOGUE_ALREADY_CURRENT");

        var rollback = store.AcceptCandidate(v1Bytes, v1Sig);
        Check("signed rollback rejected",
            !rollback.Success && rollback.Status == "CATALOGUE_ROLLBACK_REJECTED");

        var mutatedV2 = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(v2Bytes).Replace(
            "Remote catalogue propagation test.",
            "Changed without a new release."));
        var reuse = store.AcceptCandidate(mutatedV2, v2Signature);
        Check("same-release tamper rejected before reuse",
            !reuse.Success && reuse.Status == "CATALOGUE_SIGNATURE_INVALID");

        var invalidCurrentDir = Path.Combine(
            root,
            "releases",
            "00000003");
        Directory.CreateDirectory(invalidCurrentDir);
        File.WriteAllBytes(
            Path.Combine(invalidCurrentDir, "catalogue.json"),
            mutatedV2);
        File.WriteAllText(
            Path.Combine(invalidCurrentDir, "catalogue.sig"),
            v2Signature);
        File.WriteAllText(
            Path.Combine(root, "current.txt"),
            "3");
        File.WriteAllText(
            Path.Combine(root, "previous.txt"),
            "2");

        var lastKnownGood = store.LoadBestAvailable();
        Check("invalid current falls back to newest valid previous release",
            lastKnownGood.Catalogue.release == 2
            && lastKnownGood.Source == "cached-previous");

        Console.WriteLine(_failures == 0 ? "ALL_CATALOGUE_TESTS_PASS" : "CATALOGUE_TESTS_FAILED=" + _failures);
        return _failures == 0 ? 0 : 1;
    }
}
