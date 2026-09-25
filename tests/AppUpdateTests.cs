using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;
using WRN.AIGateway.Updater;

internal static class AppUpdateTests
{
    private sealed class Fixture
    {
        public string Root;
        public string Current;
        public string Previous;
        public byte[] CredentialSentinel;
        public byte[] CatalogueSentinel;
        public byte[] TransitionSentinel;
    }

    private sealed class SignedCandidate
    {
        public byte[] ManifestBytes;
        public string Signature;
        public byte[] ArtifactBytes;
        public AppReleaseManifest Manifest;
    }

    private static readonly JavaScriptSerializer Json =
        new JavaScriptSerializer
        {
            MaxJsonLength = 4 * 1024 * 1024,
            RecursionLimit = 128
        };

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
        if (args.Length != 2)
        {
            Console.WriteLine(
                "Expected: <dist-root> <temp-root>");
            return 2;
        }

        var dist = Path.GetFullPath(args[0]);
        var temp = Path.GetFullPath(args[1]);

        if (Directory.Exists(temp))
            Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);

        Check(
            "updater helper is built",
            File.Exists(
                Path.Combine(
                    dist,
                    "WRN-AI-Gateway-Updater.exe")));

        using (var rsa =
            new RSACryptoServiceProvider(2048))
        {
            var publicKey =
                rsa.ToXmlString(false);

            TestManifestSignature(
                dist,
                temp,
                rsa,
                publicKey);
            TestTamperedArtifact(
                dist,
                temp,
                rsa,
                publicKey);
            TestRollbackRejected(
                dist,
                temp,
                rsa,
                publicKey);
            TestZipTraversalRejected(
                temp,
                rsa,
                publicKey);
            TestIdentityMismatchRejected(
                dist,
                temp,
                rsa,
                publicKey);
            TestActivationSuccess(
                dist,
                temp,
                rsa,
                publicKey);
            TestActivationRollback(
                dist,
                temp,
                rsa,
                publicKey);
            TestPendingTransitionDefers(
                dist,
                temp,
                rsa,
                publicKey);
            TestSelfCheckRejectsBrokenApp(
                dist,
                temp);
        }

        Console.WriteLine(
            _failures == 0
                ? "ALL_APP_UPDATE_TESTS_PASS"
                : "APP_UPDATE_TESTS_FAILED="
                    + _failures);

        return _failures == 0 ? 0 : 1;
    }

    private static void TestManifestSignature(
        string dist,
        string temp,
        RSACryptoServiceProvider rsa,
        string publicKey)
    {
        var fixture =
            CreateFixture(
                dist,
                temp,
                "signature",
                1,
                0);

        var candidate =
            BuildCandidate(
                dist,
                temp,
                "signature-candidate",
                2,
                "0.5.0-beta.2",
                2,
                rsa);

        AppReleaseManifest parsed;
        string error;
        Check(
            "signed app manifest accepted",
            AppUpdateVerifier.TryVerifyManifest(
                candidate.ManifestBytes,
                candidate.Signature,
                publicKey,
                out parsed,
                out error)
            && parsed != null
            && parsed.release == 2);

        var tampered =
            (byte[])candidate.ManifestBytes.Clone();
        tampered[tampered.Length / 2] ^= 0x01;

        Check(
            "tampered app manifest rejected",
            !AppUpdateVerifier.TryVerifyManifest(
                tampered,
                candidate.Signature,
                publicKey,
                out parsed,
                out error)
            && error
                == "UPDATE_MANIFEST_SIGNATURE_INVALID");

        var store =
            new AppUpdateStore(
                fixture.Root);
        var staged =
            store.StageCandidate(
                candidate.ManifestBytes,
                candidate.Signature,
                candidate.ArtifactBytes,
                publicKey);

        Check(
            "valid signed update stages",
            staged.Success
            && staged.Changed
            && staged.Status == "UPDATE_STAGED"
            && staged.CandidateRelease == 2
            && Directory.Exists(
                staged.CandidateAppRoot));

        AppReleaseIdentity identity;
        Check(
            "staged identity matches signed manifest",
            AppReleaseIdentityStore.TryRead(
                staged.CandidateAppRoot,
                out identity,
                out error)
            && identity.release == 2
            && identity.version
                == "0.5.0-beta.2");

        AssertStateSentinels(
            "staging leaves durable state untouched",
            fixture);
    }

    private static void TestTamperedArtifact(
        string dist,
        string temp,
        RSACryptoServiceProvider rsa,
        string publicKey)
    {
        var fixture =
            CreateFixture(
                dist,
                temp,
                "artifact-tamper",
                1,
                -1);

        var candidate =
            BuildCandidate(
                dist,
                temp,
                "artifact-tamper-candidate",
                2,
                "0.5.0-beta.2",
                2,
                rsa);

        var tampered =
            (byte[])candidate.ArtifactBytes.Clone();
        tampered[tampered.Length / 2] ^= 0x01;

        var result =
            new AppUpdateStore(
                fixture.Root)
                .StageCandidate(
                    candidate.ManifestBytes,
                    candidate.Signature,
                    tampered,
                    publicKey);

        Check(
            "tampered update artifact rejected",
            !result.Success
            && result.Status
                == "UPDATE_ARTIFACT_HASH_MISMATCH");

        Check(
            "tampered artifact does not replace current",
            ReadRelease(fixture.Current) == 1);

        AssertStateSentinels(
            "tampered artifact leaves durable state untouched",
            fixture);
    }

    private static void TestRollbackRejected(
        string dist,
        string temp,
        RSACryptoServiceProvider rsa,
        string publicKey)
    {
        var fixture =
            CreateFixture(
                dist,
                temp,
                "rollback-rejected",
                2,
                1);

        var candidate =
            BuildCandidate(
                dist,
                temp,
                "rollback-candidate",
                1,
                "0.5.0-beta.1",
                1,
                rsa);

        var result =
            new AppUpdateStore(
                fixture.Root)
                .StageCandidate(
                    candidate.ManifestBytes,
                    candidate.Signature,
                    candidate.ArtifactBytes,
                    publicKey);

        Check(
            "signed app downgrade rejected",
            !result.Success
            && result.Status
                == "UPDATE_ROLLBACK_REJECTED");

        Check(
            "downgrade rejection preserves current",
            ReadRelease(fixture.Current) == 2);
    }

    private static void TestZipTraversalRejected(
        string temp,
        RSACryptoServiceProvider rsa,
        string publicKey)
    {
        var fixtureRoot =
            Path.Combine(
                temp,
                "zip-traversal");
        var current =
            Path.Combine(
                fixtureRoot,
                "current");
        Directory.CreateDirectory(current);
        WriteIdentity(
            current,
            1,
            "0.5.0-beta.1");

        var malicious =
            CreateZipWithEntry(
                "../escaped.txt",
                "should-not-exist");

        var candidate =
            SignArtifact(
                2,
                "0.5.0-beta.2",
                malicious,
                rsa);

        var result =
            new AppUpdateStore(
                fixtureRoot)
                .StageCandidate(
                    candidate.ManifestBytes,
                    candidate.Signature,
                    candidate.ArtifactBytes,
                    publicKey);

        Check(
            "zip path traversal rejected",
            !result.Success
            && result.Status
                == "UPDATE_ARCHIVE_INVALID");

        Check(
            "zip traversal writes nothing outside staging",
            !File.Exists(
                Path.Combine(
                    fixtureRoot,
                    "updates",
                    "escaped.txt"))
            && !File.Exists(
                Path.Combine(
                    fixtureRoot,
                    "escaped.txt")));
    }

    private static void TestIdentityMismatchRejected(
        string dist,
        string temp,
        RSACryptoServiceProvider rsa,
        string publicKey)
    {
        var fixture =
            CreateFixture(
                dist,
                temp,
                "identity-mismatch",
                1,
                -1);

        var candidate =
            BuildCandidate(
                dist,
                temp,
                "identity-mismatch-candidate",
                3,
                "0.5.0-beta.3",
                2,
                rsa);

        var result =
            new AppUpdateStore(
                fixture.Root)
                .StageCandidate(
                    candidate.ManifestBytes,
                    candidate.Signature,
                    candidate.ArtifactBytes,
                    publicKey);

        Check(
            "artifact identity must match signed manifest",
            !result.Success
            && result.Status
                == "UPDATE_IDENTITY_MISMATCH");

        Check(
            "identity mismatch preserves current",
            ReadRelease(fixture.Current) == 1);
    }

    private static void TestActivationSuccess(
        string dist,
        string temp,
        RSACryptoServiceProvider rsa,
        string publicKey)
    {
        var fixture =
            CreateFixture(
                dist,
                temp,
                "activate-success",
                1,
                0);

        var candidate =
            BuildCandidate(
                dist,
                temp,
                "activate-success-candidate",
                2,
                "0.5.0-beta.2",
                2,
                rsa);

        var staged =
            new AppUpdateStore(
                fixture.Root)
                .StageCandidate(
                    candidate.ManifestBytes,
                    candidate.Signature,
                    candidate.ArtifactBytes,
                    publicKey);

        var result =
            UpdateActivator.Activate(
                fixture.Root,
                staged.CandidateAppRoot,
                delegate(string activeRoot)
                {
                    string error;
                    return AppSelfCheck.ValidateDirectory(
                        activeRoot,
                        out error);
                });

        Check(
            "healthy staged update activates",
            result.Success
            && !result.RolledBack
            && result.Status
                == "UPDATE_ACTIVATED");

        Check(
            "new app becomes current",
            ReadRelease(fixture.Current) == 2);

        Check(
            "old current becomes last-known-good previous",
            Directory.Exists(fixture.Previous)
            && ReadRelease(fixture.Previous) == 1);

        AssertStateSentinels(
            "successful activation preserves durable state",
            fixture);
    }

    private static void TestActivationRollback(
        string dist,
        string temp,
        RSACryptoServiceProvider rsa,
        string publicKey)
    {
        var fixture =
            CreateFixture(
                dist,
                temp,
                "activate-rollback",
                1,
                0);

        var candidate =
            BuildCandidate(
                dist,
                temp,
                "activate-rollback-candidate",
                2,
                "0.5.0-beta.2",
                2,
                rsa);

        var staged =
            new AppUpdateStore(
                fixture.Root)
                .StageCandidate(
                    candidate.ManifestBytes,
                    candidate.Signature,
                    candidate.ArtifactBytes,
                    publicKey);

        var result =
            UpdateActivator.Activate(
                fixture.Root,
                staged.CandidateAppRoot,
                delegate(string activeRoot)
                {
                    return false;
                });

        Check(
            "failed post-activation health rolls back",
            !result.Success
            && result.RolledBack
            && result.Status
                == "UPDATE_ACTIVATION_ROLLED_BACK");

        Check(
            "rollback restores old current",
            ReadRelease(fixture.Current) == 1);

        Check(
            "rollback restores older previous",
            Directory.Exists(fixture.Previous)
            && ReadRelease(fixture.Previous) == 0);

        Check(
            "failed candidate retained outside current",
            !string.IsNullOrWhiteSpace(
                result.FailedCandidateRoot)
            && Directory.Exists(
                result.FailedCandidateRoot)
            && ReadRelease(
                result.FailedCandidateRoot) == 2);

        AssertStateSentinels(
            "rollback preserves durable state",
            fixture);
    }

    private static void TestPendingTransitionDefers(
        string dist,
        string temp,
        RSACryptoServiceProvider rsa,
        string publicKey)
    {
        var fixture =
            CreateFixture(
                dist,
                temp,
                "pending-transition",
                1,
                -1);

        var candidate =
            BuildCandidate(
                dist,
                temp,
                "pending-transition-candidate",
                2,
                "0.5.0-beta.2",
                2,
                rsa);

        var staged =
            new AppUpdateStore(
                fixture.Root)
                .StageCandidate(
                    candidate.ManifestBytes,
                    candidate.Signature,
                    candidate.ArtifactBytes,
                    publicKey);

        Directory.CreateDirectory(
            Path.Combine(
                fixture.Root,
                "transition",
                "pending"));

        var result =
            UpdateActivator.Activate(
                fixture.Root,
                staged.CandidateAppRoot,
                delegate(string activeRoot)
                {
                    return true;
                });

        Check(
            "pending Claude transition defers app activation",
            !result.Success
            && !result.RolledBack
            && result.Status
                == "UPDATE_DEFERRED_RECOVERY_PENDING");

        Check(
            "deferred activation preserves current",
            ReadRelease(fixture.Current) == 1);
    }

    private static void TestSelfCheckRejectsBrokenApp(
        string dist,
        string temp)
    {
        var root =
            Path.Combine(
                temp,
                "broken-self-check");
        CopyDirectory(
            dist,
            root);
        WriteIdentity(
            root,
            9,
            "broken-fixture");

        File.Delete(
            Path.Combine(
                root,
                "WRN-AI-Gateway-Gateway.exe"));

        string error;
        Check(
            "self-check rejects missing required binary",
            !AppSelfCheck.ValidateDirectory(
                root,
                out error)
            && error.StartsWith(
                "SELF_CHECK_REQUIRED_FILE_MISSING:",
                StringComparison.Ordinal));
    }

    private static Fixture CreateFixture(
        string dist,
        string temp,
        string name,
        int currentRelease,
        int previousRelease)
    {
        var root =
            Path.Combine(
                temp,
                name);

        if (Directory.Exists(root))
            Directory.Delete(root, true);
        Directory.CreateDirectory(root);

        var current =
            Path.Combine(root, "current");
        CopyDirectory(
            dist,
            current);
        WriteIdentity(
            current,
            currentRelease,
            "fixture-current-"
            + currentRelease);

        var previous =
            Path.Combine(root, "previous");
        if (previousRelease >= 0)
        {
            CopyDirectory(
                dist,
                previous);
            WriteIdentity(
                previous,
                previousRelease,
                "fixture-previous-"
                + previousRelease);
        }

        var fixture =
            new Fixture
            {
                Root = root,
                Current = current,
                Previous = previous,
                CredentialSentinel =
                    Encoding.UTF8.GetBytes(
                        "credential-sentinel"),
                CatalogueSentinel =
                    Encoding.UTF8.GetBytes(
                        "catalogue-sentinel"),
                TransitionSentinel =
                    Encoding.UTF8.GetBytes(
                        "transition-sentinel")
            };

        WriteSentinel(
            root,
            "credentials\sentinel.bin",
            fixture.CredentialSentinel);
        WriteSentinel(
            root,
            "catalogue\sentinel.bin",
            fixture.CatalogueSentinel);
        WriteSentinel(
            root,
            "transition\sentinel.bin",
            fixture.TransitionSentinel);

        return fixture;
    }

    private static SignedCandidate BuildCandidate(
        string dist,
        string temp,
        string name,
        int identityRelease,
        string identityVersion,
        int manifestRelease,
        RSACryptoServiceProvider rsa)
    {
        var source =
            Path.Combine(
                temp,
                name);

        if (Directory.Exists(source))
            Directory.Delete(source, true);

        CopyDirectory(
            dist,
            source);
        WriteIdentity(
            source,
            identityRelease,
            identityVersion);

        var artifact =
            ZipDirectory(source);

        return SignArtifact(
            manifestRelease,
            identityVersion,
            artifact,
            rsa);
    }

    private static SignedCandidate SignArtifact(
        int release,
        string version,
        byte[] artifact,
        RSACryptoServiceProvider rsa)
    {
        var manifest =
            new AppReleaseManifest
            {
                schemaVersion = 1,
                release = release,
                version = version,
                publishedAt =
                    DateTimeOffset.UtcNow
                        .ToString("o"),
                artifactUrl =
                    "https://updates.example.invalid/"
                    + "wrn-app-r"
                    + release
                    + ".zip",
                artifactSha256 =
                    AppUpdateVerifier
                        .Sha256Hex(
                            artifact),
                artifactSize =
                    artifact.LongLength,
                title =
                    "Fixture release "
                    + release,
                notes =
                    "Updater qualification fixture."
            };

        var manifestBytes =
            new UTF8Encoding(false)
                .GetBytes(
                    Json.Serialize(
                        manifest));

        var signature =
            rsa.SignData(
                manifestBytes,
                CryptoConfig.MapNameToOID(
                    "SHA256"));

        try
        {
            return new SignedCandidate
            {
                ManifestBytes =
                    manifestBytes,
                Signature =
                    Convert.ToBase64String(
                        signature),
                ArtifactBytes =
                    artifact,
                Manifest =
                    manifest
            };
        }
        finally
        {
            Array.Clear(
                signature,
                0,
                signature.Length);
        }
    }

    private static byte[] ZipDirectory(
        string root)
    {
        using (var memory =
            new MemoryStream())
        {
            using (var archive =
                new ZipArchive(
                    memory,
                    ZipArchiveMode.Create,
                    true))
            {
                foreach (var file
                    in Directory.GetFiles(
                        root,
                        "*",
                        SearchOption.AllDirectories))
                {
                    var relative =
                        file.Substring(
                            root.Length)
                            .TrimStart(
                                Path.DirectorySeparatorChar)
                            .Replace(
                                Path.DirectorySeparatorChar,
                                '/');

                    var entry =
                        archive.CreateEntry(
                            relative,
                            CompressionLevel.Optimal);

                    using (var input =
                        File.OpenRead(file))
                    using (var output =
                        entry.Open())
                    {
                        input.CopyTo(output);
                    }
                }
            }

            return memory.ToArray();
        }
    }

    private static byte[] CreateZipWithEntry(
        string name,
        string content)
    {
        using (var memory =
            new MemoryStream())
        {
            using (var archive =
                new ZipArchive(
                    memory,
                    ZipArchiveMode.Create,
                    true))
            {
                var entry =
                    archive.CreateEntry(name);

                using (var writer =
                    new StreamWriter(
                        entry.Open(),
                        new UTF8Encoding(false)))
                {
                    writer.Write(content);
                }
            }

            return memory.ToArray();
        }
    }

    private static void CopyDirectory(
        string source,
        string destination)
    {
        Directory.CreateDirectory(
            destination);

        foreach (var file
            in Directory.GetFiles(source))
        {
            File.Copy(
                file,
                Path.Combine(
                    destination,
                    Path.GetFileName(file)),
                true);
        }

        foreach (var directory
            in Directory.GetDirectories(source))
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
        var identity =
            new AppReleaseIdentity
            {
                schemaVersion = 1,
                release = release,
                version = version
            };

        File.WriteAllText(
            Path.Combine(
                appRoot,
                "app-release.json"),
            Json.Serialize(identity),
            new UTF8Encoding(false));
    }

    private static int ReadRelease(
        string appRoot)
    {
        AppReleaseIdentity identity;
        string error;

        if (!AppReleaseIdentityStore.TryRead(
            appRoot,
            out identity,
            out error))
            return -1;

        return identity.release;
    }

    private static void WriteSentinel(
        string root,
        string relative,
        byte[] bytes)
    {
        var path =
            Path.Combine(
                root,
                relative);
        Directory.CreateDirectory(
            Path.GetDirectoryName(path));
        File.WriteAllBytes(
            path,
            bytes);
    }

    private static void AssertStateSentinels(
        string name,
        Fixture fixture)
    {
        Check(
            name,
            fixture.CredentialSentinel
                .SequenceEqual(
                    File.ReadAllBytes(
                        Path.Combine(
                            fixture.Root,
                            "credentials",
                            "sentinel.bin")))
            && fixture.CatalogueSentinel
                .SequenceEqual(
                    File.ReadAllBytes(
                        Path.Combine(
                            fixture.Root,
                            "catalogue",
                            "sentinel.bin")))
            && fixture.TransitionSentinel
                .SequenceEqual(
                    File.ReadAllBytes(
                        Path.Combine(
                            fixture.Root,
                            "transition",
                            "sentinel.bin"))));
    }
}
