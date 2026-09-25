using System;
using System.IO;
using System.Linq;
using WRN.AIGateway;

internal static class CredentialLiveValidation
{
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine(
                "Expected: <key-env-file> <temp-state-root>");
            return 2;
        }

        var keyFile = args[0];
        var stateRoot = args[1];

        if (!File.Exists(keyFile))
        {
            Console.WriteLine("KEY_FILE_MISSING");
            return 3;
        }

        string key = null;
        foreach (var line in File.ReadAllLines(keyFile))
        {
            var trimmed = line.Trim();
            var keyOneMarker =
                "OPENROUTER_" + "API_KEY1=";
            var keyMarker =
                "OPENROUTER_" + "API_KEY=";

            if (trimmed.StartsWith(
                keyOneMarker,
                StringComparison.Ordinal)
                || trimmed.StartsWith(
                    keyMarker,
                    StringComparison.Ordinal))
            {
                var equals =
                    trimmed.IndexOf('=');
                key = trimmed.Substring(
                    equals + 1).Trim()
                    .Trim('"')
                    .Trim('\'');
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("KEY_ENTRY_MISSING");
            return 4;
        }

        try
        {
            var validation =
                OpenRouterKeyValidator.Validate(key);
            Console.WriteLine(
                "VALIDATION_STATUS="
                + validation.Status);

            if (!validation.Valid)
                return 5;

            if (Directory.Exists(stateRoot))
                Directory.Delete(
                    stateRoot,
                    true);
            Directory.CreateDirectory(stateRoot);

            OpenRouterCredentialStore.SaveValidated(
                key,
                validation,
                stateRoot);

            var status =
                OpenRouterCredentialStore.Inspect(
                    stateRoot);
            if (!status.Configured
                || !status.Decryptable
                || !status.ValidationMetadataPresent
                || !status.GatewayConfigured)
            {
                Console.WriteLine(
                    "DISPOSABLE_STORE_NOT_READY");
                return 6;
            }

            var retest =
                OpenRouterCredentialStore.TestStored(
                    stateRoot);
            Console.WriteLine(
                "RETEST_STATUS="
                + retest.Status);

            if (!retest.Valid)
                return 7;

            var credentialBytes =
                File.ReadAllText(
                    OpenRouterCredentialStore
                        .CredentialPath(stateRoot));
            if (credentialBytes.Contains(key))
            {
                Console.WriteLine(
                    "PLAINTEXT_KEY_LEAK");
                return 8;
            }

            Console.WriteLine(
                "LIVE_CREDENTIAL_VALIDATION_PASS");
            return 0;
        }
        finally
        {
            key = null;
            try
            {
                if (Directory.Exists(stateRoot))
                    Directory.Delete(
                        stateRoot,
                        true);
            }
            catch
            {
            }
        }
    }
}
