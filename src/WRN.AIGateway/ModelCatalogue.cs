using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WRN.AIGateway
{
    internal sealed class CatalogueDocument
    {
        public int schemaVersion { get; set; }
        public int release { get; set; }
        public string publishedAt { get; set; }
        public string minimumAppVersion { get; set; }
        public string defaultModelKey { get; set; }
        public string benchmarkSnapshot { get; set; }
        public string benchmarkNote { get; set; }
        public string announcement { get; set; }
        public CatalogueModel[] models { get; set; }
        public CatalogueChange[] changelog { get; set; }
    }

    internal sealed class CatalogueModel
    {
        public string key { get; set; }
        public string label { get; set; }
        public string maker { get; set; }
        public string upstreamModel { get; set; }
        public string claudeAlias { get; set; }
        public bool visible { get; set; }
        public bool recommended { get; set; }
        public string status { get; set; }
        public bool zdrRequired { get; set; }
        public bool streaming { get; set; }
        public bool tools { get; set; }
        public string cowork { get; set; }
        public string defaultEffort { get; set; }
        public string minimumGatewayVersion { get; set; }
        public string qualifiedAt { get; set; }
        public string qualificationVersion { get; set; }
        public string initial { get; set; }
        public string accent { get; set; }
        public string tagline { get; set; }
        public string description { get; set; }
        public string goodFor { get; set; }
        public string limitations { get; set; }
        public int aaIndex { get; set; }
        public int aaRank { get; set; }
        public int aaClassSize { get; set; }
        public string aaEffort { get; set; }
        public double aaTaskCostUsd { get; set; }
        public double inputUsdPerMillion { get; set; }
        public double outputUsdPerMillion { get; set; }
        public double shortMessageUsd { get; set; }
        public string aaUrl { get; set; }
    }

    internal sealed class CatalogueChange
    {
        public string date { get; set; }
        public string title { get; set; }
        public string body { get; set; }
    }

    internal sealed class CatalogueLoadResult
    {
        public CatalogueDocument Catalogue { get; set; }
        public byte[] RawBytes { get; set; }
        public string SignatureText { get; set; }
        public string Source { get; set; }
        public string Status { get; set; }
    }

    internal sealed class CatalogueRefreshResult
    {
        public bool Success { get; set; }
        public bool Changed { get; set; }
        public int PreviousRelease { get; set; }
        public int CandidateRelease { get; set; }
        public string Status { get; set; }
        public CatalogueLoadResult Current { get; set; }
    }

    internal static class CatalogueTrust
    {
        public const int SupportedSchemaVersion = 1;
        public static readonly Version AppVersion = new Version(0, 6, 0);

        public const string PublicKeyXml =
            "<RSAKeyValue><Modulus>uLH0GnXRTXazZ00EViCD7sqlWKavj3ikbsbnjz66cgSgvlAJ7kBrj5LiXYHxmo4APiIFGmMF3jOhrP1jwboOIk7SV/U2tpth/4ughGKFf3UbOfojyfU3/lVQTKj76c+8l30HSc48+CjsPA+rFFGSJhJEcvVW0PpCnDWuE+sEqgCjWqW1RF+1eFAhsQTxN9ZK1jtk5OtglanFuqSjZTRiv35kCQMtaDRg+wzY+Fz6Q9qLf/kbiwCPjMYq9fsCC3m7d6UXd2BY18TIBfLBrInfJo8kRp7KPboG7KeUcfjKIGORPJUJpj7grrx+d6Ki+gXTxPFdgJO5pfB8y7GO/JVtGMrQ532T9WT5mkEF/zcjsM5T37rtrj6ehSDgc+r/hQvBNkVl/7qkQ4x6dk8URdoH0+YiyZWVaejvS9hwjYGB2OGahq7qmFqIyXcR4RIEVykp+13PDVmgnGtY48j4/ruEFPJxPVkihIuMmcZdT2dC1WBJYWum3G8mqhj9IhJ9cP1Z</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public const string RemoteCatalogueUrl =
            "https://raw.githubusercontent.com/Leon-Davies/WRN-AI-Gateway/catalogue-beta/catalogue/catalogue.json";
        public const string RemoteSignatureUrl =
            "https://raw.githubusercontent.com/Leon-Davies/WRN-AI-Gateway/catalogue-beta/catalogue/catalogue.sig";
    }

    internal static class CatalogueVerifier
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 256
        };

        public static bool TryVerifyAndParse(byte[] bytes, string signatureText, out CatalogueDocument catalogue, out string error)
        {
            catalogue = null;
            error = null;

            if (bytes == null || bytes.Length == 0)
            {
                error = "CATALOGUE_EMPTY";
                return false;
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String((signatureText ?? string.Empty).Trim());
            }
            catch
            {
                error = "CATALOGUE_SIGNATURE_FORMAT_INVALID";
                return false;
            }

            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(CatalogueTrust.PublicKeyXml);
                    if (!rsa.VerifyData(bytes, CryptoConfig.MapNameToOID("SHA256"), signature))
                    {
                        error = "CATALOGUE_SIGNATURE_INVALID";
                        return false;
                    }
                }
            }
            catch
            {
                error = "CATALOGUE_SIGNATURE_CHECK_FAILED";
                return false;
            }
            finally
            {
                if (signature != null) Array.Clear(signature, 0, signature.Length);
            }

            try
            {
                catalogue = Json.Deserialize<CatalogueDocument>(Encoding.UTF8.GetString(bytes));
            }
            catch
            {
                error = "CATALOGUE_JSON_INVALID";
                return false;
            }

            return CatalogueValidator.Validate(catalogue, out error);
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(bytes).Select(delegate(byte b) { return b.ToString("x2"); }));
            }
        }
    }

    internal static class CatalogueValidator
    {
        private static readonly Regex ClaudeDesktopRejectedRoutePattern =
            new Regex(
                @"ark-code|astron|command-r|deepseek|doubao|gemini|gemma|glm|gpt|grok|hermes|hy3|kimi|lfm|\bling\b|llama|longcat|mimo|minimax|mistral|mixtral|moonshot|nemotron|openai|phi-|qianfan|qwen|tc-code|\bunic\b|yi-|stepfun|step-3|seed-|bytedance|hunyuan|granite|amazon\.nova|nova-|devstral|ministral|ernie|codex|arcee|trinity|abab|phi\d|\bk2\.|\bm2\.|jamba|arctic|solar|mercury|zamba|kat-coder|\bds-|dpsk",
                RegexOptions.IgnoreCase
                | RegexOptions.CultureInvariant);

        public static bool Validate(CatalogueDocument catalogue, out string error)
        {
            error = null;
            if (catalogue == null)
                return Fail("CATALOGUE_NULL", out error);
            if (catalogue.schemaVersion != CatalogueTrust.SupportedSchemaVersion)
                return Fail("CATALOGUE_SCHEMA_UNSUPPORTED", out error);
            if (catalogue.release < 1)
                return Fail("CATALOGUE_RELEASE_INVALID", out error);

            DateTimeOffset published;
            if (string.IsNullOrWhiteSpace(catalogue.publishedAt)
                || !DateTimeOffset.TryParse(catalogue.publishedAt, out published))
                return Fail("CATALOGUE_PUBLISHED_AT_INVALID", out error);

            Version minimum;
            if (string.IsNullOrWhiteSpace(catalogue.minimumAppVersion)
                || !Version.TryParse(catalogue.minimumAppVersion, out minimum))
                return Fail("CATALOGUE_MIN_APP_INVALID", out error);
            if (minimum > CatalogueTrust.AppVersion)
                return Fail("CATALOGUE_APP_TOO_OLD", out error);

            if (catalogue.models == null || catalogue.models.Length == 0 || catalogue.models.Length > 25)
                return Fail("CATALOGUE_MODELS_INVALID", out error);

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var upstream = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recommended = 0;

            foreach (var model in catalogue.models)
            {
                if (model == null
                    || string.IsNullOrWhiteSpace(model.key)
                    || string.IsNullOrWhiteSpace(model.label)
                    || string.IsNullOrWhiteSpace(model.maker)
                    || string.IsNullOrWhiteSpace(model.upstreamModel)
                    || string.IsNullOrWhiteSpace(model.claudeAlias))
                    return Fail("CATALOGUE_MODEL_REQUIRED_FIELD_MISSING", out error);

                if (!keys.Add(model.key))
                    return Fail("CATALOGUE_MODEL_KEY_DUPLICATE", out error);
                if (!aliases.Add(model.claudeAlias))
                    return Fail("CATALOGUE_ALIAS_DUPLICATE", out error);
                if (!upstream.Add(model.upstreamModel))
                    return Fail("CATALOGUE_UPSTREAM_DUPLICATE", out error);

                var legacyAlias = model.claudeAlias.StartsWith(
                    "anthropic/claude-wrn-",
                    StringComparison.OrdinalIgnoreCase);
                var desktopCompatibleAlias = model.claudeAlias.StartsWith(
                    "claude-wrn-",
                    StringComparison.OrdinalIgnoreCase);
                if (!legacyAlias && !desktopCompatibleAlias)
                    return Fail("CATALOGUE_ALIAS_INVALID", out error);

                if (catalogue.release >= 6
                    && ClaudeDesktopRejectedRoutePattern.IsMatch(
                        model.claudeAlias))
                {
                    return Fail(
                        "CATALOGUE_ALIAS_CLAUDE_DESKTOP_INCOMPATIBLE",
                        out error);
                }

                if (!model.zdrRequired)
                    return Fail("CATALOGUE_ZDR_REQUIRED_FALSE", out error);

                if (model.visible)
                {
                    if (model.aaIndex < 0 || model.aaRank < 1 || model.aaClassSize < model.aaRank)
                        return Fail("CATALOGUE_AA_METADATA_INVALID", out error);
                    if (string.IsNullOrWhiteSpace(model.aaUrl)
                        || !model.aaUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return Fail("CATALOGUE_AA_URL_INVALID", out error);
                }

                if (model.recommended)
                    recommended++;
            }

            if (string.IsNullOrWhiteSpace(catalogue.defaultModelKey))
                return Fail("CATALOGUE_DEFAULT_MISSING", out error);

            var defaultModel = catalogue.models.FirstOrDefault(delegate(CatalogueModel m)
            {
                return string.Equals(m.key, catalogue.defaultModelKey, StringComparison.OrdinalIgnoreCase);
            });
            if (defaultModel == null || !defaultModel.visible)
                return Fail("CATALOGUE_DEFAULT_INVALID", out error);
            if (!defaultModel.recommended)
                return Fail("CATALOGUE_DEFAULT_NOT_RECOMMENDED", out error);
            if (recommended != 1)
                return Fail("CATALOGUE_RECOMMENDED_COUNT_INVALID", out error);

            return true;
        }

        private static bool Fail(string value, out string error)
        {
            error = value;
            return false;
        }
    }

    internal sealed class CatalogueStore
    {
        private readonly string _baseDir;
        private readonly string _storeRoot;

        public CatalogueStore(string baseDir)
            : this(baseDir, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WRN-AI-Gateway",
                "catalogue"))
        {
        }

        internal CatalogueStore(string baseDir, string storeRoot)
        {
            _baseDir = baseDir;
            _storeRoot = storeRoot;
        }

        public CatalogueLoadResult LoadBestAvailable()
        {
            var bundled = LoadPair(
                Path.Combine(_baseDir, "catalogue", "catalogue.json"),
                Path.Combine(_baseDir, "catalogue", "catalogue.sig"),
                "bundled");

            var currentRelease = ReadPointer("current.txt");
            var current = currentRelease.HasValue
                ? LoadRelease(currentRelease.Value, "cached-current")
                : null;

            var previousRelease = ReadPointer("previous.txt");
            var previous = previousRelease.HasValue
                ? LoadRelease(previousRelease.Value, "cached-previous")
                : null;

            var best = new[]
                {
                    current,
                    previous,
                    bundled
                }
                .Where(delegate(CatalogueLoadResult candidate)
                {
                    return candidate != null;
                })
                .OrderByDescending(delegate(CatalogueLoadResult candidate)
                {
                    return candidate.Catalogue.release;
                })
                .FirstOrDefault();

            if (best != null)
                return best;

            throw new InvalidOperationException(
                "No valid signed WRN model catalogue is available.");
        }

        public CatalogueRefreshResult AcceptCandidate(byte[] bytes, string signatureText)
        {
            CatalogueDocument candidate;
            string error;
            if (!CatalogueVerifier.TryVerifyAndParse(bytes, signatureText, out candidate, out error))
                return FailRefresh(error);

            CatalogueLoadResult current;
            try { current = LoadBestAvailable(); }
            catch { current = null; }

            var previousRelease = current == null ? 0 : current.Catalogue.release;
            if (current != null)
            {
                if (candidate.release < previousRelease)
                    return FailRefresh("CATALOGUE_ROLLBACK_REJECTED", previousRelease, candidate.release, current);

                if (candidate.release == previousRelease)
                {
                    var existingHash = CatalogueVerifier.Sha256Hex(current.RawBytes);
                    var candidateHash = CatalogueVerifier.Sha256Hex(bytes);
                    if (string.Equals(existingHash, candidateHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return new CatalogueRefreshResult
                        {
                            Success = true,
                            Changed = false,
                            PreviousRelease = previousRelease,
                            CandidateRelease = candidate.release,
                            Status = "CATALOGUE_ALREADY_CURRENT",
                            Current = current
                        };
                    }

                    return FailRefresh("CATALOGUE_RELEASE_REUSE_REJECTED", previousRelease, candidate.release, current);
                }
            }

            Promote(candidate.release, bytes, signatureText, previousRelease);
            var promoted = LoadRelease(candidate.release, "cached-current");
            if (promoted == null)
                return FailRefresh("CATALOGUE_PROMOTION_VERIFY_FAILED", previousRelease, candidate.release, current);

            return new CatalogueRefreshResult
            {
                Success = true,
                Changed = true,
                PreviousRelease = previousRelease,
                CandidateRelease = candidate.release,
                Status = "CATALOGUE_PROMOTED",
                Current = promoted
            };
        }

        private CatalogueRefreshResult FailRefresh(
            string status,
            int previousRelease = 0,
            int candidateRelease = 0,
            CatalogueLoadResult current = null)
        {
            return new CatalogueRefreshResult
            {
                Success = false,
                Changed = false,
                PreviousRelease = previousRelease,
                CandidateRelease = candidateRelease,
                Status = status,
                Current = current
            };
        }

        private void Promote(int release, byte[] bytes, string signatureText, int previousRelease)
        {
            Directory.CreateDirectory(_storeRoot);
            var releasesRoot = Path.Combine(_storeRoot, "releases");
            Directory.CreateDirectory(releasesRoot);

            var finalDir = Path.Combine(releasesRoot, release.ToString("D8"));
            var stagingDir = finalDir + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stagingDir);

            try
            {
                WriteAllBytesFlush(Path.Combine(stagingDir, "catalogue.json"), bytes);
                WriteAllTextFlush(Path.Combine(stagingDir, "catalogue.sig"), (signatureText ?? string.Empty).Trim());

                var staged = LoadPair(
                    Path.Combine(stagingDir, "catalogue.json"),
                    Path.Combine(stagingDir, "catalogue.sig"),
                    "staged");
                if (staged == null || staged.Catalogue.release != release)
                    throw new InvalidOperationException("Staged catalogue failed verification.");

                if (Directory.Exists(finalDir))
                    Directory.Delete(finalDir, true);
                Directory.Move(stagingDir, finalDir);

                if (previousRelease > 0)
                    WritePointer("previous.txt", previousRelease);
                WritePointer("current.txt", release);
            }
            finally
            {
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, true);
            }
        }

        private void WritePointer(string name, int release)
        {
            Directory.CreateDirectory(_storeRoot);
            var path = Path.Combine(_storeRoot, name);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            WriteAllTextFlush(temp, release.ToString());

            if (File.Exists(path))
            {
                var backup = path + ".bak";
                try { File.Replace(temp, path, backup, true); }
                finally
                {
                    if (File.Exists(backup)) File.Delete(backup);
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private int? ReadPointer(string name)
        {
            try
            {
                var path = Path.Combine(_storeRoot, name);
                if (!File.Exists(path)) return null;
                int value;
                return int.TryParse(File.ReadAllText(path).Trim(), out value) && value > 0
                    ? (int?)value
                    : null;
            }
            catch { return null; }
        }

        private CatalogueLoadResult LoadRelease(int release, string source)
        {
            var dir = Path.Combine(_storeRoot, "releases", release.ToString("D8"));
            return LoadPair(
                Path.Combine(dir, "catalogue.json"),
                Path.Combine(dir, "catalogue.sig"),
                source);
        }

        private static CatalogueLoadResult LoadPair(string jsonPath, string sigPath, string source)
        {
            try
            {
                if (!File.Exists(jsonPath) || !File.Exists(sigPath)) return null;
                var bytes = File.ReadAllBytes(jsonPath);
                var sig = File.ReadAllText(sigPath, Encoding.ASCII);
                CatalogueDocument catalogue;
                string error;
                if (!CatalogueVerifier.TryVerifyAndParse(bytes, sig, out catalogue, out error))
                    return null;

                return new CatalogueLoadResult
                {
                    Catalogue = catalogue,
                    RawBytes = bytes,
                    SignatureText = sig.Trim(),
                    Source = source,
                    Status = "CATALOGUE_VALID"
                };
            }
            catch { return null; }
        }

        private static void WriteAllBytesFlush(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void WriteAllTextFlush(string path, string text)
        {
            WriteAllBytesFlush(path, new UTF8Encoding(false).GetBytes(text));
        }
    }

    internal static class CatalogueRemote
    {
        public static CatalogueRefreshResult Refresh(string baseDir)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            byte[] bytes;
            string signature;

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "WRN-AI-Gateway/" + CatalogueTrust.AppVersion);
                    client.Headers.Add("Cache-Control", "no-cache");
                    client.Headers.Add("Pragma", "no-cache");
                    var nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                    bytes = client.DownloadData(CatalogueTrust.RemoteCatalogueUrl + "?wrn=" + nonce);
                    signature = client.DownloadString(CatalogueTrust.RemoteSignatureUrl + "?wrn=" + nonce);
                }
            }
            catch
            {
                return new CatalogueRefreshResult
                {
                    Success = false,
                    Changed = false,
                    Status = "CATALOGUE_REMOTE_UNAVAILABLE"
                };
            }

            return new CatalogueStore(baseDir).AcceptCandidate(bytes, signature);
        }
    }

    internal sealed class CatalogueReport
    {
        public string appVersion { get; set; }
        public int release { get; set; }
        public string source { get; set; }
        public string defaultModelKey { get; set; }
        public string[] visibleModels { get; set; }
        public string benchmarkSnapshot { get; set; }
        public string status { get; set; }
        public bool refreshAttempted { get; set; }
        public bool refreshChanged { get; set; }
    }

    internal static class CatalogueCommand
    {
        public static bool TryRun(string[] args, string baseDir)
        {
            if (args == null) return false;
            string reportPath = null;
            var refresh = false;

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--catalogue-report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    reportPath = args[++i];
                else if (string.Equals(args[i], "--catalogue-refresh-report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    reportPath = args[++i];
                    refresh = true;
                }
            }

            if (string.IsNullOrWhiteSpace(reportPath)) return false;

            CatalogueRefreshResult refreshResult = null;
            if (refresh)
                refreshResult = CatalogueRemote.Refresh(baseDir);

            var loaded = new CatalogueStore(baseDir).LoadBestAvailable();
            var report = new CatalogueReport
            {
                appVersion = CatalogueTrust.AppVersion.ToString(),
                release = loaded.Catalogue.release,
                source = loaded.Source,
                defaultModelKey = loaded.Catalogue.defaultModelKey,
                visibleModels = loaded.Catalogue.models.Where(delegate(CatalogueModel m) { return m.visible; })
                    .Select(delegate(CatalogueModel m) { return m.label; }).ToArray(),
                benchmarkSnapshot = loaded.Catalogue.benchmarkSnapshot,
                status = refreshResult == null ? loaded.Status : refreshResult.Status,
                refreshAttempted = refresh,
                refreshChanged = refreshResult != null && refreshResult.Changed
            };

            var json = new JavaScriptSerializer().Serialize(report);
            var full = Path.GetFullPath(reportPath);
            var parent = Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(full, json, new UTF8Encoding(false));
            return true;
        }
    }
}
