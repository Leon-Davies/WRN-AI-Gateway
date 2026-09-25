using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace WRN.AIGateway
{
    internal sealed class OpenRouterKeyValidation
    {
        public bool Valid { get; set; }
        public string Status { get; set; }
        public string KeySha256 { get; set; }
        public string ValidatedAtUtc { get; set; }
        public bool IsFreeTier { get; set; }
        public double? LimitRemaining { get; set; }
        public string LimitReset { get; set; }
        public string ExpiresAt { get; set; }
    }

    internal sealed class StoredOpenRouterCredential
    {
        public int SchemaVersion { get; set; }
        public string Key { get; set; }
        public string ValidatedAtUtc { get; set; }
        public bool IsFreeTier { get; set; }
        public double? LimitRemaining { get; set; }
        public string LimitReset { get; set; }
        public string ExpiresAt { get; set; }
    }

    internal sealed class CredentialStatus
    {
        public bool Configured { get; set; }
        public bool Decryptable { get; set; }
        public bool ValidationMetadataPresent { get; set; }
        public string ValidatedAtUtc { get; set; }
        public bool IsFreeTier { get; set; }
        public double? LimitRemaining { get; set; }
        public string LimitReset { get; set; }
        public string ExpiresAt { get; set; }
        public bool GatewayConfigured { get; set; }
        public int GatewayPort { get; set; }
        public string Status { get; set; }
    }

    internal static class OpenRouterKeyValidator
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer
            {
                MaxJsonLength = 2 * 1024 * 1024,
                RecursionLimit = 64
            };

        public static OpenRouterKeyValidation Validate(
            string apiKey)
        {
            var key = (apiKey ?? string.Empty).Trim();
            if (key.Length < 16)
                return Invalid("KEY_FORMAT_INVALID", key);

            try
            {
                var request =
                    (HttpWebRequest)WebRequest.Create(
                        "https://openrouter.ai/api/v1/key");
                request.Method = "GET";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.Headers[
                    HttpRequestHeader.Authorization] =
                    "Bearer " + key;
                request.UserAgent =
                    "WRN-AI-Gateway/0.4";

                using (var response =
                    (HttpWebResponse)request.GetResponse())
                using (var reader =
                    new StreamReader(
                        response.GetResponseStream(),
                        Encoding.UTF8))
                {
                    if (response.StatusCode
                        != HttpStatusCode.OK)
                    {
                        return Invalid(
                            "KEY_VALIDATION_HTTP_"
                            + (int)response.StatusCode,
                            key);
                    }

                    var root =
                        Json.DeserializeObject(
                            reader.ReadToEnd())
                        as Dictionary<string, object>;
                    Dictionary<string, object> data = null;
                    object dataValue;
                    if (root != null
                        && root.TryGetValue(
                            "data",
                            out dataValue))
                    {
                        data =
                            dataValue
                            as Dictionary<string, object>;
                    }

                    if (data == null)
                        return Invalid(
                            "KEY_VALIDATION_RESPONSE_INVALID",
                            key);

                    if (GetBool(
                        data,
                        "is_management_key")
                        || GetBool(
                            data,
                            "is_provisioning_key"))
                    {
                        return Invalid(
                            "INFERENCE_KEY_REQUIRED",
                            key);
                    }

                    return new OpenRouterKeyValidation
                    {
                        Valid = true,
                        Status = "KEY_VALID",
                        KeySha256 = Sha256(key),
                        ValidatedAtUtc =
                            DateTimeOffset.UtcNow.ToString("o"),
                        IsFreeTier =
                            GetBool(data, "is_free_tier"),
                        LimitRemaining =
                            GetNullableDouble(
                                data,
                                "limit_remaining"),
                        LimitReset =
                            GetString(data, "limit_reset"),
                        ExpiresAt =
                            GetString(data, "expires_at")
                    };
                }
            }
            catch (WebException ex)
            {
                var response =
                    ex.Response as HttpWebResponse;
                if (response != null)
                {
                    var code = (int)response.StatusCode;
                    return Invalid(
                        code == 401
                            ? "KEY_UNAUTHORIZED"
                            : "KEY_VALIDATION_HTTP_" + code,
                        key);
                }

                return Invalid(
                    "KEY_VALIDATION_NETWORK_ERROR",
                    key);
            }
            catch
            {
                return Invalid(
                    "KEY_VALIDATION_FAILED",
                    key);
            }
        }

        private static OpenRouterKeyValidation Invalid(
            string status,
            string key)
        {
            return new OpenRouterKeyValidation
            {
                Valid = false,
                Status = status,
                KeySha256 = Sha256(key ?? string.Empty),
                ValidatedAtUtc =
                    DateTimeOffset.UtcNow.ToString("o")
            };
        }

        internal static string Sha256(string value)
        {
            var bytes =
                Encoding.UTF8.GetBytes(
                    value ?? string.Empty);
            try
            {
                using (var sha = SHA256.Create())
                {
                    return BitConverter.ToString(
                        sha.ComputeHash(bytes))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                }
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static bool GetBool(
            Dictionary<string, object> data,
            string name)
        {
            object value;
            if (!data.TryGetValue(name, out value)
                || value == null)
                return false;
            try { return Convert.ToBoolean(value); }
            catch { return false; }
        }

        private static double? GetNullableDouble(
            Dictionary<string, object> data,
            string name)
        {
            object value;
            if (!data.TryGetValue(name, out value)
                || value == null)
                return null;
            try { return Convert.ToDouble(value); }
            catch { return null; }
        }

        private static string GetString(
            Dictionary<string, object> data,
            string name)
        {
            object value;
            if (!data.TryGetValue(name, out value)
                || value == null)
                return null;
            return Convert.ToString(value);
        }
    }

    internal static class OpenRouterCredentialStore
    {
        private const string EnvelopePrefix =
            "WRN-CRED-V1\n";

        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer
            {
                MaxJsonLength = 2 * 1024 * 1024,
                RecursionLimit = 64
            };

        public static CredentialStatus Inspect(
            string stateRoot)
        {
            if (string.IsNullOrWhiteSpace(stateRoot))
                throw new ArgumentNullException("stateRoot");

            var path = CredentialPath(stateRoot);
            var status = new CredentialStatus
            {
                Configured = File.Exists(path),
                Status = File.Exists(path)
                    ? "CREDENTIAL_PRESENT"
                    : "CREDENTIAL_NOT_CONFIGURED"
            };

            if (status.Configured)
            {
                StoredOpenRouterCredential stored;
                if (TryRead(
                    path,
                    out stored))
                {
                    status.Decryptable = true;
                    status.ValidationMetadataPresent =
                        !string.IsNullOrWhiteSpace(
                            stored.ValidatedAtUtc);
                    status.ValidatedAtUtc =
                        stored.ValidatedAtUtc;
                    status.IsFreeTier =
                        stored.IsFreeTier;
                    status.LimitRemaining =
                        stored.LimitRemaining;
                    status.LimitReset =
                        stored.LimitReset;
                    status.ExpiresAt =
                        stored.ExpiresAt;
                    status.Status =
                        "CREDENTIAL_READY";
                    stored.Key = null;
                }
                else
                {
                    status.Status =
                        "CREDENTIAL_UNREADABLE";
                }
            }

            ModeGatewayConfig config;
            string error;
            ModeCoordinator.ProbeGatewayConfig(
                stateRoot,
                out config,
                out error);
            status.GatewayConfigured =
                config != null;
            status.GatewayPort =
                config == null ? 0 : config.Port;

            return status;
        }

        public static OpenRouterKeyValidation
            ValidateAndSave(
                string apiKey,
                string stateRoot)
        {
            var validation =
                OpenRouterKeyValidator.Validate(apiKey);
            if (!validation.Valid)
                return validation;

            SaveValidated(
                apiKey,
                validation,
                stateRoot);
            return validation;
        }

        internal static void SaveValidated(
            string apiKey,
            OpenRouterKeyValidation validation,
            string stateRoot)
        {
            if (string.IsNullOrWhiteSpace(stateRoot))
                throw new ArgumentNullException("stateRoot");
            if (validation == null
                || !validation.Valid)
            {
                throw new InvalidOperationException(
                    "VALIDATED_KEY_REQUIRED");
            }

            var key =
                (apiKey ?? string.Empty).Trim();
            if (!string.Equals(
                validation.KeySha256,
                OpenRouterKeyValidator.Sha256(key),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "KEY_VALIDATION_MISMATCH");
            }

            var envelope =
                new StoredOpenRouterCredential
                {
                    SchemaVersion = 1,
                    Key = key,
                    ValidatedAtUtc =
                        validation.ValidatedAtUtc,
                    IsFreeTier =
                        validation.IsFreeTier,
                    LimitRemaining =
                        validation.LimitRemaining,
                    LimitReset =
                        validation.LimitReset,
                    ExpiresAt =
                        validation.ExpiresAt
                };

            var plaintext =
                Encoding.UTF8.GetBytes(
                    EnvelopePrefix
                    + Json.Serialize(envelope));
            byte[] protectedBytes = null;
            try
            {
                protectedBytes =
                    ProtectedData.Protect(
                        plaintext,
                        null,
                        DataProtectionScope.CurrentUser);
                WriteAtomic(
                    CredentialPath(stateRoot),
                    Convert.ToBase64String(
                        protectedBytes));
            }
            finally
            {
                envelope.Key = null;
                Array.Clear(
                    plaintext,
                    0,
                    plaintext.Length);
                if (protectedBytes != null)
                    Array.Clear(
                        protectedBytes,
                        0,
                        protectedBytes.Length);
            }

            EnsureGatewayConfig(stateRoot);
        }

        public static OpenRouterKeyValidation
            TestStored(string stateRoot)
        {
            StoredOpenRouterCredential stored;
            if (!TryRead(
                CredentialPath(stateRoot),
                out stored))
            {
                return new OpenRouterKeyValidation
                {
                    Valid = false,
                    Status =
                        "CREDENTIAL_NOT_CONFIGURED"
                };
            }

            try
            {
                var validation =
                    OpenRouterKeyValidator.Validate(
                        stored.Key);
                if (validation.Valid)
                {
                    SaveValidated(
                        stored.Key,
                        validation,
                        stateRoot);
                }
                return validation;
            }
            finally
            {
                stored.Key = null;
            }
        }

        public static void Remove(
            string stateRoot)
        {
            var path = CredentialPath(stateRoot);
            if (File.Exists(path))
                File.Delete(path);
        }

        internal static void EnsureGatewayConfig(
            string stateRoot)
        {
            ModeGatewayConfig existing;
            string error;
            ModeCoordinator.ProbeGatewayConfig(
                stateRoot,
                out existing,
                out error);
            if (existing != null)
                return;

            var config =
                new ModeGatewayConfig
                {
                    LocalApiKey =
                        RandomHex(32),
                    Port =
                        AllocateLoopbackPort()
                };
            var text =
                Json.Serialize(config);
            WriteAtomic(
                Path.Combine(
                    stateRoot,
                    "gateway",
                    "gateway.json"),
                text);
        }

        internal static bool TryRead(
            string path,
            out StoredOpenRouterCredential stored)
        {
            stored = null;
            if (!File.Exists(path))
                return false;

            byte[] protectedBytes = null;
            byte[] plain = null;
            try
            {
                protectedBytes =
                    Convert.FromBase64String(
                        File.ReadAllText(
                            path,
                            Encoding.ASCII).Trim());
                plain =
                    ProtectedData.Unprotect(
                        protectedBytes,
                        null,
                        DataProtectionScope.CurrentUser);

                var text =
                    Encoding.UTF8.GetString(plain);
                if (!text.StartsWith(
                    EnvelopePrefix,
                    StringComparison.Ordinal))
                {
                    return false;
                }

                stored =
                    Json.Deserialize<
                        StoredOpenRouterCredential>(
                        text.Substring(
                            EnvelopePrefix.Length));
                return stored != null
                    && stored.SchemaVersion == 1
                    && !string.IsNullOrWhiteSpace(
                        stored.Key);
            }
            catch
            {
                stored = null;
                return false;
            }
            finally
            {
                if (plain != null)
                    Array.Clear(
                        plain,
                        0,
                        plain.Length);
                if (protectedBytes != null)
                    Array.Clear(
                        protectedBytes,
                        0,
                        protectedBytes.Length);
            }
        }

        internal static string CredentialPath(
            string stateRoot)
        {
            return Path.Combine(
                stateRoot,
                "credentials",
                "openrouter.key.dpapi");
        }

        private static int AllocateLoopbackPort()
        {
            var listener =
                new TcpListener(
                    IPAddress.Loopback,
                    0);
            listener.Start();
            try
            {
                return ((IPEndPoint)
                    listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static string RandomHex(
            int byteCount)
        {
            var bytes =
                new byte[byteCount];
            using (var rng =
                RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            try
            {
                return BitConverter.ToString(bytes)
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static void WriteAtomic(
            string path,
            string text)
        {
            var parent =
                Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            var temp =
                path + ".tmp-"
                + Guid.NewGuid().ToString("N");
            File.WriteAllText(
                temp,
                text,
                new UTF8Encoding(false));

            try
            {
                if (File.Exists(path))
                {
                    var backup =
                        path + ".bak";
                    try
                    {
                        File.Replace(
                            temp,
                            path,
                            backup,
                            true);
                    }
                    finally
                    {
                        if (File.Exists(backup))
                            File.Delete(backup);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }
}
