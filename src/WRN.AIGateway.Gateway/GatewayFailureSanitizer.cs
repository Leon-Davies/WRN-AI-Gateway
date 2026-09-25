using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;

namespace WRN.AIGateway.Gateway
{
    internal static class GatewayFailureSanitizer
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 128
            };

        public static bool TryMapJsonError(
            byte[] body,
            out RuntimeFailure failure)
        {
            failure = null;
            if (body == null || body.Length == 0)
                return false;

            try
            {
                var root =
                    Json.DeserializeObject(
                        Encoding.UTF8.GetString(body))
                    as Dictionary<string, object>;

                return TryMapErrorObject(
                    root,
                    out failure);
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySanitizeSseDataLine(
            string line,
            out string sanitized,
            out RuntimeFailure failure)
        {
            sanitized = line;
            failure = null;

            if (string.IsNullOrEmpty(line)
                || !line.StartsWith(
                    "data:",
                    StringComparison.Ordinal))
            {
                return false;
            }

            var payload =
                line.Substring(5).Trim();
            if (payload.Length == 0
                || payload == "[DONE]")
            {
                return false;
            }

            try
            {
                var root =
                    Json.DeserializeObject(payload)
                    as Dictionary<string, object>;

                if (!TryMapErrorObject(
                    root,
                    out failure))
                {
                    return false;
                }

                sanitized =
                    "data: "
                    + Json.Serialize(
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
                        });

                return true;
            }
            catch
            {
                failure = null;
                sanitized = line;
                return false;
            }
        }

        private static bool TryMapErrorObject(
            Dictionary<string, object> root,
            out RuntimeFailure failure)
        {
            failure = null;
            if (root == null)
                return false;

            object typeValue;
            if (!root.TryGetValue(
                "type",
                out typeValue)
                || !string.Equals(
                    Convert.ToString(typeValue),
                    "error",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            object errorValue;
            var error =
                root.TryGetValue(
                    "error",
                    out errorValue)
                    ? errorValue
                        as Dictionary<string, object>
                    : null;

            object errorTypeValue;
            var errorType =
                error != null
                && error.TryGetValue(
                    "type",
                    out errorTypeValue)
                    ? Convert.ToString(
                        errorTypeValue)
                    : null;

            failure =
                RuntimeFailureCatalog
                    .FromUpstreamStatus(
                        StatusFromErrorType(
                            errorType));

            return true;
        }

        private static int StatusFromErrorType(
            string errorType)
        {
            if (string.Equals(
                errorType,
                "authentication_error",
                StringComparison.OrdinalIgnoreCase))
                return 401;

            if (string.Equals(
                errorType,
                "permission_error",
                StringComparison.OrdinalIgnoreCase))
                return 403;

            if (string.Equals(
                errorType,
                "rate_limit_error",
                StringComparison.OrdinalIgnoreCase))
                return 429;

            if (string.Equals(
                errorType,
                "invalid_request_error",
                StringComparison.OrdinalIgnoreCase))
                return 400;

            if (string.Equals(
                errorType,
                "not_found_error",
                StringComparison.OrdinalIgnoreCase))
                return 404;

            return 503;
        }
    }
}
