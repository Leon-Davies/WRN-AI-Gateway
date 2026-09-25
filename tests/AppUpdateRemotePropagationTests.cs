using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using WRN.AIGateway;
using WRN.AIGateway.Updater;

internal static class AppUpdateRemotePropagationTests
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
        if (args == null || args.Length != 5)
        {
            Console.WriteLine(
                "Expected: <dist-root> <manifest> <signature> <artifact> <temp-root>");
            return 2;
        }

        var dist = Path.GetFullPath(args[0]);
        var manifestPath = Path.GetFullPath(args[1]);
        var signaturePath = Path.GetFullPath(args[2]);
        var artifactPath = Path.GetFullPath(args[3]);
        var temp = Path.GetFullPath(args[4]);

        if (Directory.Exists(temp))
            Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);

        try
        {
            var manifestBytes =
                File.ReadAllBytes(manifestPath);
            var signature =
                File.ReadAllText(
                    signaturePath,
                    Encoding.ASCII);
            var artifactBytes =
                File.ReadAllBytes(artifactPath);

            AppReleaseManifest manifest;
            string error;

            Check(
                "remote signed manifest verifies with embedded client trust root",
                AppUpdateVerifier.TryVerifyManifest(
                    manifestBytes,
                    signature,
                    out manifest,
                    out error)
                && manifest != null);

            if (manifest == null)
                return 1;

            Check(
                "remote artifact size matches signed manifest",
                artifactBytes.LongLength
                    == manifest.artifactSize);

            Check(
                "remote artifact hash matches signed manifest",
                string.Equals(
                    AppUpdateVerifier.Sha256Hex(
                        artifactBytes),
                    manifest.artifactSha256,
                    StringComparison.OrdinalIgnoreCase));

            Check(
                "remote public artifact excludes local WRN hero assets",
                !ZipContainsHero(artifactBytes));

            var installRoot =
                Path.Combine(temp, "install");
            var current =
                Path.Combine(
                    installRoot,
                    "current");

            CopyDirectory(dist, current);
            WriteIdentity(
                current,
                0,
                "remote-propagation-baseline");

            var localHero =
                Encoding.UTF8.GetBytes(
                    "REMOTE_PROPAGATION_LOCAL_WRN_BRAND");

            WriteBytes(
                Path.Combine(
                    current,
                    "assets",
                    "wrn-hero-live-local.png"),
                localHero);

            var credentialSentinel =
                Encoding.UTF8.GetBytes(
                    "credential-state-stays-outside-app-version");
            var catalogueSentinel =
                Encoding.UTF8.GetBytes(
                    "catalogue-state-stays-outside-app-version");
            var transitionSentinel =
                Encoding.UTF8.GetBytes(
                    "transition-state-stays-outside-app-version");

            WriteBytes(
                Path.Combine(
                    installRoot,
                    "credentials",
                    "live-sentinel.bin"),
                credentialSentinel);
            WriteBytes(
                Path.Combine(
                    installRoot,
                    "catalogue",
                    "live-sentinel.bin"),
                catalogueSentinel);
            WriteBytes(
                Path.Combine(
                    installRoot,
                    "transition",
                    "live-sentinel.bin"),
                transitionSentinel);

            var staged =
                new AppUpdateStore(
                    installRoot)
                    .StageCandidate(
                        manifestBytes,
                        signature,
                        artifactBytes);

            Check(
                "real remote release stages",
                staged.Success
                && staged.Changed
                && staged.Status == "UPDATE_STAGED"
                && staged.CandidateRelease
                    == manifest.release
                && Directory.Exists(
                    staged.CandidateAppRoot));

            var stagedHero =
                staged.CandidateAppRoot == null
                    ? null
                    : Path.Combine(
                        staged.CandidateAppRoot,
                        "assets",
                        "wrn-hero-live-local.png");

            Check(
                "client restores local WRN branding while staging remote public artifact",
                !string.IsNullOrWhiteSpace(
                    stagedHero)
                && File.Exists(stagedHero)
                && localHero.SequenceEqual(
                    File.ReadAllBytes(
                        stagedHero)));

            if (!staged.Success)
                return 1;

            var activation =
                UpdateActivator.Activate(
                    installRoot,
                    staged.CandidateAppRoot,
                    delegate(string activeRoot)
                    {
                        string selfCheckError;
                        return AppSelfCheck.ValidateDirectory(
                            activeRoot,
                            out selfCheckError);
                    });

            Check(
                "real remote release activates",
                activation.Success
                && !activation.RolledBack
                && activation.Status
                    == "UPDATE_ACTIVATED");

            AppReleaseIdentity currentIdentity;
            Check(
                "remote release becomes current",
                AppReleaseIdentityStore.TryRead(
                    Path.Combine(
                        installRoot,
                        "current"),
                    out currentIdentity,
                    out error)
                && currentIdentity.release
                    == manifest.release
                && currentIdentity.version
                    == manifest.version);

            AppReleaseIdentity previousIdentity;
            Check(
                "old install becomes last-known-good previous",
                AppReleaseIdentityStore.TryRead(
                    Path.Combine(
                        installRoot,
                        "previous"),
                    out previousIdentity,
                    out error)
                && previousIdentity.release == 0);

            var activeHero =
                Path.Combine(
                    installRoot,
                    "current",
                    "assets",
                    "wrn-hero-live-local.png");

            Check(
                "local WRN branding survives remote activation",
                File.Exists(activeHero)
                && localHero.SequenceEqual(
                    File.ReadAllBytes(
                        activeHero)));

            Check(
                "durable WRN state survives remote activation",
                credentialSentinel.SequenceEqual(
                    File.ReadAllBytes(
                        Path.Combine(
                            installRoot,
                            "credentials",
                            "live-sentinel.bin")))
                && catalogueSentinel.SequenceEqual(
                    File.ReadAllBytes(
                        Path.Combine(
                            installRoot,
                            "catalogue",
                            "live-sentinel.bin")))
                && transitionSentinel.SequenceEqual(
                    File.ReadAllBytes(
                        Path.Combine(
                            installRoot,
                            "transition",
                            "live-sentinel.bin"))));

            Console.WriteLine(
                _failures == 0
                    ? "PHASE5_REMOTE_PROPAGATION_PASS"
                    : "PHASE5_REMOTE_PROPAGATION_FAILED="
                        + _failures);

            return _failures == 0 ? 0 : 1;
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    private static bool ZipContainsHero(
        byte[] bytes)
    {
        using (var memory =
            new MemoryStream(
                bytes,
                false))
        using (var archive =
            new ZipArchive(
                memory,
                ZipArchiveMode.Read,
                false))
        {
            return archive.Entries.Any(
                delegate(ZipArchiveEntry entry)
                {
                    var name =
                        Path.GetFileName(
                            (entry.FullName
                                ?? string.Empty)
                                .Replace(
                                    '/',
                                    Path.DirectorySeparatorChar));

                    return name.StartsWith(
                        "wrn-hero",
                        StringComparison.OrdinalIgnoreCase)
                        && name.EndsWith(
                            ".png",
                            StringComparison.OrdinalIgnoreCase);
                });
        }
    }

    private static void CopyDirectory(
        string source,
        string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in
            Directory.GetFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(
                    destination,
                    Path.GetFileName(file)),
                true);
        }

        foreach (var directory in
            Directory.GetDirectories(source))
        {
            CopyDirectory(
                directory,
                Path.Combine(
                    destination,
                    Path.GetFileName(directory)));
        }
    }

    private static void WriteIdentity(
        string appRoot,
        int release,
        string version)
    {
        var json =
            "{"
            + "\"schemaVersion\":1,"
            + "\"release\":"
            + release
            + ","
            + "\"version\":\""
            + version
                .Replace(
                    "\"",
                    "\\\"")
            + "\""
            + "}";

        File.WriteAllText(
            Path.Combine(
                appRoot,
                "app-release.json"),
            json,
            new UTF8Encoding(false));
    }

    private static void WriteBytes(
        string path,
        byte[] bytes)
    {
        var parent =
            Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(
            parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllBytes(path, bytes);
    }
}
