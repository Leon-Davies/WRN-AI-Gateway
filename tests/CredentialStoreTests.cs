using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;

internal static class CredentialStoreTests
{
    private static int _failures;
    private static readonly JavaScriptSerializer Json =
        new JavaScriptSerializer
        {
            MaxJsonLength = 2 * 1024 * 1024,
            RecursionLimit = 64
        };

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
            Console.WriteLine(
                "Expected: <app-base-dir> <temp-root>");
            return 2;
        }

        var baseDir = args[0];
        var tempRoot = args[1];

        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, true);
        Directory.CreateDirectory(tempRoot);

        TestSaveInspectAndReplace(baseDir, tempRoot);
        TestValidationMismatchPreservesCredential(
            tempRoot);
        TestUnreadableCredential(tempRoot);
        TestRemoveKeepsGatewayConfig(tempRoot);
        TestGatewayReadsVersionedEnvelope(
            baseDir,
            tempRoot);
        TestGatewayReadsLegacyRawDpapi(
            baseDir,
            tempRoot);

        Console.WriteLine(
            _failures == 0
                ? "ALL_CREDENTIAL_STORE_TESTS_PASS"
                : "CREDENTIAL_STORE_TESTS_FAILED="
                    + _failures);

        return _failures == 0 ? 0 : 1;
    }

    private static OpenRouterKeyValidation Valid(
        string key)
    {
        return new OpenRouterKeyValidation
        {
            Valid = true,
            Status = "KEY_VALID",
            KeySha256 =
                OpenRouterKeyValidator.Sha256(key),
            ValidatedAtUtc =
                DateTimeOffset.UtcNow.ToString("o"),
            IsFreeTier = false,
            LimitRemaining = 123.45,
            LimitReset = "monthly",
            ExpiresAt = null
        };
    }

    private static void TestSaveInspectAndReplace(
        string baseDir,
        string tempRoot)
    {
        var root = Path.Combine(
            tempRoot,
            "save-replace");
        Directory.CreateDirectory(root);

        var key1 =
            "fixture-openrouter-key-one-"
            + Guid.NewGuid().ToString("N");
        var key2 =
            "fixture-openrouter-key-two-"
            + Guid.NewGuid().ToString("N");

        OpenRouterCredentialStore.SaveValidated(
            key1,
            Valid(key1),
            root);

        var credentialPath =
            OpenRouterCredentialStore.CredentialPath(
                root);
        var ciphertext1 =
            File.ReadAllText(
                credentialPath,
                Encoding.ASCII);

        Check(
            "credential file created",
            File.Exists(credentialPath));
        Check(
            "stored credential does not contain plaintext key",
            ciphertext1.IndexOf(
                key1,
                StringComparison.Ordinal) < 0);

        var status =
            OpenRouterCredentialStore.Inspect(root);
        Check(
            "stored credential reports ready",
            status.Configured
            && status.Decryptable
            && status.ValidationMetadataPresent
            && status.Status == "CREDENTIAL_READY");
        Check(
            "safe validation metadata retained",
            status.LimitRemaining.HasValue
            && Math.Abs(
                status.LimitRemaining.Value
                - 123.45) < 0.0001);

        var gatewayPath =
            Path.Combine(
                root,
                "gateway",
                "gateway.json");
        Check(
            "gateway config auto-created",
            File.Exists(gatewayPath));

        var gatewayBefore =
            File.ReadAllBytes(gatewayPath);
        var gateway =
            Json.Deserialize<ModeGatewayConfig>(
                Encoding.UTF8.GetString(
                    gatewayBefore));

        Check(
            "gateway config has random local bearer",
            gateway != null
            && !string.IsNullOrWhiteSpace(
                gateway.LocalApiKey)
            && gateway.LocalApiKey != key1);
        Check(
            "gateway config has valid loopback port",
            gateway != null
            && gateway.Port >= 1024
            && gateway.Port <= 65535);

        OpenRouterCredentialStore.SaveValidated(
            key2,
            Valid(key2),
            root);

        var gatewayAfter =
            File.ReadAllBytes(gatewayPath);
        Check(
            "key replacement preserves gateway config bytes",
            gatewayBefore.SequenceEqual(
                gatewayAfter));

        StoredOpenRouterCredential stored;
        Check(
            "replacement credential decrypts",
            OpenRouterCredentialStore.TryRead(
                credentialPath,
                out stored)
            && stored != null);
        Check(
            "replacement writes new key",
            stored != null
            && stored.Key == key2);
        if (stored != null)
            stored.Key = null;

        var ciphertext2 =
            File.ReadAllText(
                credentialPath,
                Encoding.ASCII);
        Check(
            "replacement ciphertext excludes both plaintext keys",
            ciphertext2.IndexOf(
                key1,
                StringComparison.Ordinal) < 0
            && ciphertext2.IndexOf(
                key2,
                StringComparison.Ordinal) < 0);
    }

    private static void
        TestValidationMismatchPreservesCredential(
            string tempRoot)
    {
        var root = Path.Combine(
            tempRoot,
            "mismatch");
        Directory.CreateDirectory(root);

        var key =
            "fixture-working-key-"
            + Guid.NewGuid().ToString("N");
        OpenRouterCredentialStore.SaveValidated(
            key,
            Valid(key),
            root);

        var path =
            OpenRouterCredentialStore.CredentialPath(
                root);
        var before = File.ReadAllBytes(path);

        var replacement =
            "fixture-replacement-key-"
            + Guid.NewGuid().ToString("N");
        var wrongValidation =
            Valid("different-key-for-hash");

        var blocked = false;
        try
        {
            OpenRouterCredentialStore.SaveValidated(
                replacement,
                wrongValidation,
                root);
        }
        catch (InvalidOperationException ex)
        {
            blocked =
                ex.Message
                == "KEY_VALIDATION_MISMATCH";
        }

        Check(
            "mismatched validation is blocked",
            blocked);
        Check(
            "blocked replacement preserves encrypted credential bytes",
            before.SequenceEqual(
                File.ReadAllBytes(path)));
    }

    private static void TestUnreadableCredential(
        string tempRoot)
    {
        var root = Path.Combine(
            tempRoot,
            "unreadable");
        var path =
            OpenRouterCredentialStore.CredentialPath(
                root);
        Directory.CreateDirectory(
            Path.GetDirectoryName(path));
        File.WriteAllText(
            path,
            "not-valid-base64",
            Encoding.ASCII);

        var status =
            OpenRouterCredentialStore.Inspect(root);
        Check(
            "corrupt credential reports unreadable",
            status.Configured
            && !status.Decryptable
            && status.Status
                == "CREDENTIAL_UNREADABLE");
    }

    private static void TestRemoveKeepsGatewayConfig(
        string tempRoot)
    {
        var root = Path.Combine(
            tempRoot,
            "remove");
        Directory.CreateDirectory(root);

        var key =
            "fixture-remove-key-"
            + Guid.NewGuid().ToString("N");
        OpenRouterCredentialStore.SaveValidated(
            key,
            Valid(key),
            root);

        var gatewayPath =
            Path.Combine(
                root,
                "gateway",
                "gateway.json");
        var gatewayBefore =
            File.ReadAllBytes(gatewayPath);

        OpenRouterCredentialStore.Remove(root);

        Check(
            "remove deletes only OpenRouter credential",
            !File.Exists(
                OpenRouterCredentialStore.CredentialPath(
                    root)));
        Check(
            "remove leaves gateway config intact",
            File.Exists(gatewayPath)
            && gatewayBefore.SequenceEqual(
                File.ReadAllBytes(gatewayPath)));
    }

    private static void TestGatewayReadsVersionedEnvelope(
        string baseDir,
        string tempRoot)
    {
        var root = Path.Combine(
            tempRoot,
            "gateway-envelope");
        Directory.CreateDirectory(root);

        var key =
            "fixture-envelope-key-"
            + Guid.NewGuid().ToString("N");
        OpenRouterCredentialStore.SaveValidated(
            key,
            Valid(key),
            root);

        var catalogue =
            new CatalogueStore(
                baseDir,
                Path.Combine(root, "catalogue"))
                .LoadBestAvailable();

        var runtime =
            GatewayLifecycle.EnsureHealthy(
                baseDir,
                root,
                catalogue.Catalogue.release,
                5000);

        Check(
            "gateway starts from versioned DPAPI credential envelope",
            runtime.Healthy
            && runtime.Started);

        Check(
            "gateway from versioned envelope stops cleanly",
            GatewayLifecycle.StopOwned(
                baseDir,
                root));
    }

    private static void TestGatewayReadsLegacyRawDpapi(
        string baseDir,
        string tempRoot)
    {
        var root = Path.Combine(
            tempRoot,
            "gateway-legacy");
        Directory.CreateDirectory(root);

        OpenRouterCredentialStore.EnsureGatewayConfig(
            root);

        var legacyKey =
            "fixture-legacy-key-"
            + Guid.NewGuid().ToString("N");
        var plain =
            Encoding.UTF8.GetBytes(legacyKey);
        var protectedBytes =
            ProtectedData.Protect(
                plain,
                null,
                DataProtectionScope.CurrentUser);

        try
        {
            var path =
                OpenRouterCredentialStore.CredentialPath(
                    root);
            Directory.CreateDirectory(
                Path.GetDirectoryName(path));
            File.WriteAllText(
                path,
                Convert.ToBase64String(
                    protectedBytes),
                Encoding.ASCII);
        }
        finally
        {
            Array.Clear(
                plain,
                0,
                plain.Length);
            Array.Clear(
                protectedBytes,
                0,
                protectedBytes.Length);
        }

        var catalogue =
            new CatalogueStore(
                baseDir,
                Path.Combine(root, "catalogue"))
                .LoadBestAvailable();

        var runtime =
            GatewayLifecycle.EnsureHealthy(
                baseDir,
                root,
                catalogue.Catalogue.release,
                5000);

        Check(
            "gateway retains legacy raw-DPAPI compatibility",
            runtime.Healthy
            && runtime.Started);
        Check(
            "legacy-compatible gateway stops cleanly",
            GatewayLifecycle.StopOwned(
                baseDir,
                root));
    }
}
