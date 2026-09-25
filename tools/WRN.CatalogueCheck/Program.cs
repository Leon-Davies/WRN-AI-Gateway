using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;

namespace WRN.CatalogueCheck
{
    internal static class Program
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 256
        };

        private static int Main(string[] args)
        {
            if (args == null || args.Length == 0)
                return Usage();

            var command = args[0].Trim().ToLowerInvariant();

            if (command == "public-key")
            {
                Console.WriteLine(CatalogueTrust.PublicKeyXml);
                return 0;
            }

            if (command == "validate" && args.Length == 2)
                return ValidateUnsigned(args[1]);

            if (command == "verify" && args.Length == 3)
                return VerifySigned(args[1], args[2]);

            return Usage();
        }

        private static int ValidateUnsigned(string jsonPath)
        {
            try
            {
                var bytes = File.ReadAllBytes(jsonPath);
                CatalogueDocument catalogue;
                try
                {
                    catalogue = Json.Deserialize<CatalogueDocument>(Encoding.UTF8.GetString(bytes));
                }
                catch
                {
                    return Fail("CATALOGUE_JSON_INVALID");
                }

                string error;
                if (!CatalogueValidator.Validate(catalogue, out error))
                    return Fail(error);

                Console.WriteLine(
                    "VALID release=" + catalogue.release +
                    " models=" + catalogue.models.Length +
                    " sha256=" + CatalogueVerifier.Sha256Hex(bytes));
                return 0;
            }
            catch (Exception ex)
            {
                return Fail("CATALOGUE_CHECK_FAILED:" + ex.GetType().Name);
            }
        }

        private static int VerifySigned(string jsonPath, string signaturePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(jsonPath);
                var signature = File.ReadAllText(signaturePath, Encoding.ASCII);
                CatalogueDocument catalogue;
                string error;

                if (!CatalogueVerifier.TryVerifyAndParse(bytes, signature, out catalogue, out error))
                    return Fail(error);

                Console.WriteLine(
                    "VERIFIED release=" + catalogue.release +
                    " models=" + catalogue.models.Length +
                    " sha256=" + CatalogueVerifier.Sha256Hex(bytes));
                return 0;
            }
            catch (Exception ex)
            {
                return Fail("CATALOGUE_CHECK_FAILED:" + ex.GetType().Name);
            }
        }

        private static int Fail(string status)
        {
            Console.Error.WriteLine(status);
            return 1;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  WRN-CatalogueCheck.exe public-key");
            Console.Error.WriteLine("  WRN-CatalogueCheck.exe validate <catalogue.json>");
            Console.Error.WriteLine("  WRN-CatalogueCheck.exe verify <catalogue.json> <catalogue.sig>");
            return 2;
        }
    }
}
