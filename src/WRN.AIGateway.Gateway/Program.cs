using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using WRN.AIGateway;

namespace WRN.AIGateway.Gateway
{
    internal sealed class GatewayConfig
    {
        public string LocalApiKey { get; set; }
        public int Port { get; set; }
    }

    internal sealed class GatewayCredentialEnvelope
    {
        public int SchemaVersion { get; set; }
        public string Key { get; set; }
    }

    internal sealed class ParsedRequest
    {
        public string Method;
        public string Target;
        public Dictionary<string, string> Headers;
        public byte[] Body;
    }

    internal sealed class GatewayPolicyReport
    {
        public bool allowed { get; set; }
        public string error { get; set; }
        public string requestedModel { get; set; }
        public string upstreamModel { get; set; }
        public int catalogueRelease { get; set; }
        public string outboundJson { get; set; }
    }

    internal static class Program
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 512
        };

        private static string StateRoot;
        private static string LogPath;
        private static GatewayConfig Config;
        private static string OpenRouterKey;
        private static HttpClient Client;
        private static CatalogueLoadResult CatalogueLoad;

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (TryRunPolicyReport(args, baseDir))
                    return;

                StateRoot = GetStateRoot(args);
                var gatewayRoot = Path.Combine(StateRoot, "gateway");
                Directory.CreateDirectory(gatewayRoot);
                LogPath = Path.Combine(gatewayRoot, "gateway.log");

                Config = LoadConfig(Path.Combine(gatewayRoot, "gateway.json"));
                OpenRouterKey = LoadProtectedKey(
                    Path.Combine(StateRoot, "credentials", "openrouter.key.dpapi"));
                CatalogueLoad = new CatalogueStore(
                    baseDir,
                    Path.Combine(StateRoot, "catalogue"))
                    .LoadBestAvailable();

                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    UseProxy = true
                };
                Client = new HttpClient(handler);
                Client.Timeout = TimeSpan.FromMinutes(6);

                RunServer().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                try { Log("FATAL " + ex.GetType().Name + ": " + SafeMessage(ex.Message)); } catch { }
                Environment.ExitCode = 1;
            }
        }

        private static string GetStateRoot(string[] args)
        {
            for (var i = 0; args != null && i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--state-root", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(args[i + 1]);
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WRN-AI-Gateway");
        }

        private static bool TryRunPolicyReport(string[] args, string baseDir)
        {
            if (args == null) return false;

            string input = null;
            string output = null;
            for (var i = 0; i < args.Length - 2; i++)
            {
                if (string.Equals(args[i], "--gateway-policy-report", StringComparison.OrdinalIgnoreCase))
                {
                    input = args[i + 1];
                    output = args[i + 2];
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
                return false;

            var catalogue = new CatalogueStore(baseDir).LoadBestAvailable();
            var rewrite = GatewayPolicy.Rewrite(File.ReadAllBytes(input), catalogue.Catalogue);
            var report = new GatewayPolicyReport
            {
                allowed = rewrite.Allowed,
                error = rewrite.Error,
                requestedModel = rewrite.RequestedModel,
                upstreamModel = rewrite.UpstreamModel,
                catalogueRelease = catalogue.Catalogue.release,
                outboundJson = rewrite.OutboundBody == null ? null : Encoding.UTF8.GetString(rewrite.OutboundBody)
            };

            var parent = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(output, Json.Serialize(report), new UTF8Encoding(false));
            return true;
        }

        private static GatewayConfig LoadConfig(string path)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException("Missing gateway configuration.");

            var config = Json.Deserialize<GatewayConfig>(File.ReadAllText(path, Encoding.UTF8));
            if (config == null || string.IsNullOrWhiteSpace(config.LocalApiKey))
                throw new InvalidOperationException("Gateway local credential is missing.");
            if (config.Port < 1024 || config.Port > 65535)
                throw new InvalidOperationException("Gateway port is invalid.");

            return config;
        }

        private static string LoadProtectedKey(string path)
        {
            if (!File.Exists(path))
                throw new InvalidOperationException("OpenRouter credential is not configured.");

            var protectedBytes = Convert.FromBase64String(
                File.ReadAllText(path, Encoding.ASCII).Trim());
            var plain = ProtectedData.Unprotect(
                protectedBytes,
                null,
                DataProtectionScope.CurrentUser);

            try
            {
                var text = Encoding.UTF8.GetString(plain);
                const string envelopePrefix = "WRN-CRED-V1\n";
                string key;

                if (text.StartsWith(
                    envelopePrefix,
                    StringComparison.Ordinal))
                {
                    var envelope =
                        Json.Deserialize<GatewayCredentialEnvelope>(
                            text.Substring(
                                envelopePrefix.Length));
                    if (envelope == null
                        || envelope.SchemaVersion != 1
                        || string.IsNullOrWhiteSpace(envelope.Key))
                    {
                        throw new InvalidOperationException(
                            "OpenRouter credential envelope is invalid.");
                    }

                    key = envelope.Key.Trim();
                    envelope.Key = null;
                }
                else
                {
                    // Compatibility with the earlier development format
                    // where the DPAPI plaintext was the raw OpenRouter key.
                    key = text.Trim();
                }

                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidOperationException(
                        "OpenRouter credential is empty.");
                return key;
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
                Array.Clear(protectedBytes, 0, protectedBytes.Length);
            }
        }
        private static async Task RunServer()
        {
            var listener = new TcpListener(IPAddress.Loopback, Config.Port);
            listener.Start();

            Log(
                "Gateway started on 127.0.0.1:" + Config.Port +
                "; catalogue_release=" + CatalogueLoad.Catalogue.release +
                "; models=" + CatalogueLoad.Catalogue.models.Length);
            Log("Policy: provider.zdr=true; provider.data_collection=deny");

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                var background = Task.Run(() => HandleClientSafe(client));
            }
        }

        private static async Task HandleClientSafe(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    using (var stream = client.GetStream())
                    {
                        var request = await ReadRequest(stream).ConfigureAwait(false);
                        if (request == null) return;
                        await Dispatch(stream, request).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log("client_error=" + ex.GetType().Name + ": " + SafeMessage(ex.Message));
                }
            }
        }

        private static async Task Dispatch(NetworkStream stream, ParsedRequest request)
        {
            var path = request.Target.Split('?')[0];

            if (request.Method == "HEAD" && path == "/api/hello")
            {
                await WriteSimple(stream, 200, "OK", new byte[0]).ConfigureAwait(false);
                return;
            }

            if (request.Method == "GET" && (path == "/api/hello" || path == "/health"))
            {
                var health = Json.Serialize(new
                {
                    ok = true,
                    provider = "openrouter",
                    catalogueRelease = CatalogueLoad.Catalogue.release
                });
                await WriteSimple(stream, 200, "OK", Encoding.UTF8.GetBytes(health)).ConfigureAwait(false);
                return;
            }

            if (request.Method != "POST" || path != "/v1/messages")
            {
                await WriteSimple(
                    stream,
                    404,
                    "Not Found",
                    Encoding.UTF8.GetBytes("{\"error\":\"not_found\"}")).ConfigureAwait(false);
                return;
            }

            string authorization;
            request.Headers.TryGetValue("authorization", out authorization);
            if (authorization != "Bearer " + Config.LocalApiKey)
            {
                await WriteSimple(
                    stream,
                    401,
                    "Unauthorized",
                    Encoding.UTF8.GetBytes("{\"error\":\"invalid_local_gateway_key\"}")).ConfigureAwait(false);
                return;
            }

            await ProxyMessages(stream, request).ConfigureAwait(false);
        }

        private static async Task ProxyMessages(
            NetworkStream stream,
            ParsedRequest request)
        {
            var rewrite =
                GatewayPolicy.Rewrite(
                    request.Body,
                    CatalogueLoad.Catalogue);

            if (!rewrite.Allowed)
            {
                Log(
                    "rejected_request requested="
                    + (rewrite.RequestedModel ?? "<missing>")
                    + " reason="
                    + rewrite.Error);

                var failure =
                    new RuntimeFailure
                    {
                        Kind = RuntimeFailureKind.RequestRejected,
                        Code = "WRN_REQUEST_REJECTED",
                        Title = "WRN Claude could not send this request",
                        Message = rewrite.Error == "unsupported_model"
                            ? "This WRN model is not available. Refresh the model list and try again."
                            : "WRN Claude could not send this request. Try again.",
                        Retryable = false,
                        HttpStatus = 400,
                        AnthropicErrorType =
                            "invalid_request_error"
                    };

                await WriteAnthropicFailure(
                    stream,
                    failure).ConfigureAwait(false);
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            Log(
                "POST /v1/messages alias="
                + rewrite.RequestedModel
                + " upstream="
                + rewrite.UpstreamModel);

            HttpResponseMessage response = null;
            try
            {
                using (var outbound =
                    BuildOutboundRequest(
                        request,
                        rewrite.OutboundBody))
                {
                    response = await Client.SendAsync(
                        outbound,
                        HttpCompletionOption.ResponseHeadersRead)
                        .ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                var failure =
                    RuntimeFailureCatalog.TransportFailure();
                Log(
                    "upstream_failure code="
                    + failure.Code
                    + " latency_ms="
                    + stopwatch.ElapsedMilliseconds);

                await WriteAnthropicFailure(
                    stream,
                    failure).ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException)
            {
                stopwatch.Stop();
                var failure =
                    RuntimeFailureCatalog.TransportFailure();
                Log(
                    "upstream_failure code="
                    + failure.Code
                    + " latency_ms="
                    + stopwatch.ElapsedMilliseconds);

                await WriteAnthropicFailure(
                    stream,
                    failure).ConfigureAwait(false);
                return;
            }

            using (response)
            {
                stopwatch.Stop();
                Log(
                    "upstream_status="
                    + (int)response.StatusCode
                    + " latency_ms="
                    + stopwatch.ElapsedMilliseconds);

                if (!response.IsSuccessStatusCode)
                {
                    var failure =
                        RuntimeFailureCatalog.FromUpstreamStatus(
                            (int)response.StatusCode);

                    Log(
                        "upstream_failure code="
                        + failure.Code
                        + " status="
                        + (int)response.StatusCode);

                    await WriteAnthropicFailure(
                        stream,
                        failure).ConfigureAwait(false);
                    return;
                }

                await WriteResponse(
                    stream,
                    response).ConfigureAwait(false);
            }
        }

        private static HttpRequestMessage BuildOutboundRequest(
            ParsedRequest request,
            byte[] outboundBody)
        {
            var outbound =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://openrouter.ai/api/v1/messages");

            outbound.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    OpenRouterKey);
            outbound.Headers.TryAddWithoutValidation(
                "X-Title",
                "WRN AI Gateway");

            CopyHeader(
                request,
                outbound,
                "anthropic-version");
            CopyHeader(
                request,
                outbound,
                "anthropic-beta");

            outbound.Content =
                new ByteArrayContent(outboundBody);
            outbound.Content.Headers.ContentType =
                new MediaTypeHeaderValue(
                    "application/json");

            return outbound;
        }

        private static async Task WriteAnthropicFailure(
            NetworkStream stream,
            RuntimeFailure failure)
        {
            var body = Encoding.UTF8.GetBytes(
                Json.Serialize(
                    new
                    {
                        type = "error",
                        error = new
                        {
                            type =
                                failure.AnthropicErrorType
                                ?? "api_error",
                            message =
                                failure.Message
                        }
                    }));

            await WriteSimple(
                stream,
                failure.HttpStatus > 0
                    ? failure.HttpStatus
                    : 503,
                ReasonPhrase(
                    failure.HttpStatus),
                body).ConfigureAwait(false);
        }

        private static string ReasonPhrase(int status)
        {
            switch (status)
            {
                case 400:
                    return "Bad Request";
                case 401:
                    return "Unauthorized";
                case 402:
                    return "Payment Required";
                case 429:
                    return "Too Many Requests";
                default:
                    return "Service Unavailable";
            }
        }

        private static void CopyHeader(
            ParsedRequest request,
            HttpRequestMessage outbound,
            string name)
        {
            string value;
            if (request.Headers.TryGetValue(name, out value)
                && !string.IsNullOrWhiteSpace(value))
            {
                outbound.Headers.TryAddWithoutValidation(name, value);
            }
        }
        private static async Task<ParsedRequest> ReadRequest(NetworkStream stream)
        {
            var headerBytes = new List<byte>(4096);
            var one = new byte[1];

            while (headerBytes.Count < 65536)
            {
                var count = await stream.ReadAsync(one, 0, 1).ConfigureAwait(false);
                if (count == 0) return null;

                headerBytes.Add(one[0]);
                var current = headerBytes.Count;
                if (current >= 4
                    && headerBytes[current - 4] == 13
                    && headerBytes[current - 3] == 10
                    && headerBytes[current - 2] == 13
                    && headerBytes[current - 1] == 10)
                {
                    break;
                }
            }

            if (headerBytes.Count >= 65536)
                throw new InvalidOperationException("Request headers exceed the gateway limit.");

            var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
            var lines = headerText.Split(
                new[] { "\r\n" },
                StringSplitOptions.None);
            var first = lines[0].Split(' ');

            if (first.Length < 2)
                throw new InvalidOperationException("Malformed request line.");

            var headers = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i < lines.Length; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    headers[lines[i].Substring(0, colon).Trim()] =
                        lines[i].Substring(colon + 1).Trim();
                }
            }

            var contentLength = 0;
            string lengthText;
            if (headers.TryGetValue("content-length", out lengthText))
                int.TryParse(lengthText, out contentLength);

            if (contentLength < 0 || contentLength > 8 * 1024 * 1024)
                throw new InvalidOperationException("Request body exceeds the gateway limit.");

            var body = new byte[contentLength];
            var offset = 0;

            while (offset < contentLength)
            {
                var count = await stream.ReadAsync(
                    body,
                    offset,
                    contentLength - offset).ConfigureAwait(false);
                if (count == 0)
                    throw new EndOfStreamException();

                offset += count;
            }

            return new ParsedRequest
            {
                Method = first[0].ToUpperInvariant(),
                Target = first[1],
                Headers = headers,
                Body = body
            };
        }

        private static async Task WriteResponse(
            NetworkStream stream,
            HttpResponseMessage response)
        {
            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 ")
                .Append((int)response.StatusCode)
                .Append(' ')
                .Append(response.ReasonPhrase ?? "OK")
                .Append("\r\n");

            if (response.Content.Headers.ContentType != null)
            {
                builder.Append("Content-Type: ")
                    .Append(response.Content.Headers.ContentType)
                    .Append("\r\n");
            }

            foreach (var name in new[] { "x-request-id", "openrouter-generation-id" })
            {
                IEnumerable<string> values;
                if (response.Headers.TryGetValues(name, out values))
                {
                    foreach (var value in values)
                    {
                        builder.Append(name)
                            .Append(": ")
                            .Append(value)
                            .Append("\r\n");
                    }
                }
            }

            builder.Append("Cache-Control: no-cache\r\n")
                .Append("Connection: close\r\n\r\n");

            var responseHead = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(
                responseHead,
                0,
                responseHead.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);

            using (var upstream =
                await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                var buffer = new byte[16384];
                int count;
                while ((count = await upstream.ReadAsync(
                    buffer,
                    0,
                    buffer.Length).ConfigureAwait(false)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, count).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }
        }
        private static async Task WriteSimple(
            NetworkStream stream,
            int code,
            string reason,
            byte[] body)
        {
            var head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " + code + " " + reason +
                "\r\nContent-Type: application/json" +
                "\r\nContent-Length: " + body.Length +
                "\r\nConnection: close\r\n\r\n");

            await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);

            if (body.Length > 0)
            {
                await stream.WriteAsync(
                    body,
                    0,
                    body.Length).ConfigureAwait(false);
            }

            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static string JsonEscape(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static string SafeMessage(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value
                .Replace("\r", " ")
                .Replace("\n", " ");

            if (text.Length > 300)
                text = text.Substring(0, 300);

            return text;
        }

        private static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(LogPath))
                return;

            try
            {
                var line =
                    DateTimeOffset.Now.ToString("o") +
                    " " +
                    message +
                    Environment.NewLine;

                File.AppendAllText(
                    LogPath,
                    line,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}
