using System;
using System.IO;
using WRN.AIGateway;

namespace WRN.AppUpdateCheck
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            if (args == null || args.Length < 1)
            {
                Console.Error.WriteLine(
                    "Expected command: public-key | verify <manifest> <signature> | self-check <app-root>");
                return 2;
            }

            var command =
                (args[0] ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (command == "public-key")
            {
                Console.Write(
                    AppUpdateTrust.PublicKeyXml);
                return 0;
            }

            if (command == "verify")
            {
                if (args.Length != 3)
                {
                    Console.Error.WriteLine(
                        "Expected: verify <manifest> <signature>");
                    return 2;
                }

                AppReleaseManifest manifest;
                string error;

                if (!AppUpdateVerifier.TryVerifyManifest(
                    File.ReadAllBytes(args[1]),
                    File.ReadAllText(args[2]),
                    out manifest,
                    out error))
                {
                    Console.Error.WriteLine(
                        error ?? "UPDATE_VERIFY_FAILED");
                    return 1;
                }

                Console.WriteLine(
                    "release="
                    + manifest.release
                    + " version="
                    + manifest.version);
                return 0;
            }

            if (command == "self-check")
            {
                if (args.Length != 2)
                {
                    Console.Error.WriteLine(
                        "Expected: self-check <app-root>");
                    return 2;
                }

                string error;
                if (!AppSelfCheck.ValidateDirectory(
                    args[1],
                    out error))
                {
                    Console.Error.WriteLine(
                        error ?? "SELF_CHECK_FAILED");
                    return 1;
                }

                Console.WriteLine(
                    "APP_SELF_CHECK_PASS");
                return 0;
            }

            Console.Error.WriteLine(
                "Unknown command.");
            return 2;
        }
    }
}
