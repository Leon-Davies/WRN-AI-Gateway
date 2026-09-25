using System;
using System.Net;

namespace WRN.AIGateway
{
    internal enum RuntimeFailureKind
    {
        None = 0,
        Configuration,
        Authentication,
        Policy,
        UsageLimit,
        RateLimit,
        Network,
        ServiceUnavailable,
        RequestRejected,
        RecoveryRequired,
        Unsupported,
        Unknown
    }

    internal sealed class RuntimeFailure
    {
        public RuntimeFailureKind Kind { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public bool Retryable { get; set; }
        public int HttpStatus { get; set; }
        public string AnthropicErrorType { get; set; }
    }

    internal static class RuntimeFailureCatalog
    {
        public static RuntimeFailure FromCredentialStatus(
            string status)
        {
            var value = status ?? string.Empty;

            if (value == "KEY_UNAUTHORIZED")
                return Failure(
                    RuntimeFailureKind.Authentication,
                    "OPENROUTER_AUTH_REQUIRED",
                    "OpenRouter connection needs attention",
                    "Reconnect OpenRouter in WRN AI Gateway Settings.",
                    false,
                    401,
                    "authentication_error");

            if (value == "INFERENCE_KEY_REQUIRED"
                || value == "KEY_FORMAT_INVALID")
            {
                return Failure(
                    RuntimeFailureKind.Authentication,
                    "OPENROUTER_KEY_INVALID",
                    "OpenRouter connection needs attention",
                    "Connect a valid OpenRouter account in WRN AI Gateway Settings.",
                    false,
                    401,
                    "authentication_error");
            }

            if (value == "CREDENTIAL_NOT_CONFIGURED")
                return Failure(
                    RuntimeFailureKind.Configuration,
                    "OPENROUTER_NOT_CONFIGURED",
                    "OpenRouter is not connected",
                    "Connect OpenRouter in WRN AI Gateway Settings.",
                    false,
                    503,
                    "api_error");

            if (value == "KEY_VALIDATION_NETWORK_ERROR")
                return Failure(
                    RuntimeFailureKind.Network,
                    "OPENROUTER_UNREACHABLE",
                    "OpenRouter is temporarily unavailable",
                    "Check your connection and try again.",
                    true,
                    503,
                    "api_error");

            if (value == "KEY_VALIDATION_HTTP_402")
                return Failure(
                    RuntimeFailureKind.UsageLimit,
                    "OPENROUTER_USAGE_LIMIT",
                    "WRN Claude usage limit reached",
                    "The OpenRouter usage limit has been reached. Contact WRN AI support.",
                    false,
                    402,
                    "api_error");

            if (value == "KEY_VALIDATION_HTTP_429")
                return Failure(
                    RuntimeFailureKind.RateLimit,
                    "OPENROUTER_RATE_LIMITED",
                    "OpenRouter is busy",
                    "Wait a moment and try again.",
                    false,
                    429,
                    "rate_limit_error");

            if (value == "KEY_VALIDATION_HTTP_408"
                || value == "KEY_VALIDATION_HTTP_500"
                || value == "KEY_VALIDATION_HTTP_502"
                || value == "KEY_VALIDATION_HTTP_503"
                || value == "KEY_VALIDATION_HTTP_504")
            {
                return Failure(
                    RuntimeFailureKind.ServiceUnavailable,
                    "OPENROUTER_TEMPORARY_FAILURE",
                    "OpenRouter is temporarily unavailable",
                    "Try again in a moment.",
                    true,
                    503,
                    "api_error");
            }

            return Failure(
                RuntimeFailureKind.Unknown,
                "OPENROUTER_VALIDATION_FAILED",
                "Could not check the connection",
                "Try again. Nothing was changed.",
                false,
                503,
                "api_error");
        }

        public static RuntimeFailure FromGatewayStatus(
            string status)
        {
            var value = status ?? string.Empty;

            if (value.StartsWith(
                "GATEWAY_CONFIG_INVALID",
                StringComparison.Ordinal))
            {
                return Failure(
                    RuntimeFailureKind.Configuration,
                    "GATEWAY_CONFIG_INVALID",
                    "WRN Claude is not ready",
                    "Open WRN AI Gateway and reconnect OpenRouter.",
                    false,
                    503,
                    "api_error");
            }

            if (value == "GATEWAY_BINARY_MISSING")
                return Failure(
                    RuntimeFailureKind.Configuration,
                    "GATEWAY_BINARY_MISSING",
                    "WRN Claude needs repair",
                    "Reinstall WRN AI Gateway.",
                    false,
                    503,
                    "api_error");

            if (value == "OWNED_GATEWAY_RUNNING_BUT_UNHEALTHY"
                || value == "GATEWAY_HEALTH_TIMEOUT"
                || value == "GATEWAY_EXITED_BEFORE_HEALTHY"
                || value == "GATEWAY_PROCESS_START_FAILED")
            {
                return Failure(
                    RuntimeFailureKind.ServiceUnavailable,
                    "GATEWAY_START_FAILED",
                    "WRN Claude could not start",
                    "Close WRN Claude and try again. If this keeps happening, contact WRN AI support.",
                    true,
                    503,
                    "api_error");
            }

            return Failure(
                RuntimeFailureKind.Unknown,
                "GATEWAY_FAILURE",
                "WRN Claude is unavailable",
                "Try again. If this keeps happening, contact WRN AI support.",
                false,
                503,
                "api_error");
        }

        public static RuntimeFailure FromUpstreamStatus(
            int statusCode)
        {
            if (statusCode == 401 || statusCode == 403)
                return Failure(
                    RuntimeFailureKind.Authentication,
                    "OPENROUTER_AUTH_REQUIRED",
                    "OpenRouter connection needs attention",
                    "Open WRN AI Gateway Settings and reconnect OpenRouter.",
                    false,
                    401,
                    "authentication_error");

            if (statusCode == 402)
                return Failure(
                    RuntimeFailureKind.UsageLimit,
                    "OPENROUTER_USAGE_LIMIT",
                    "WRN Claude usage limit reached",
                    "The OpenRouter usage limit has been reached. Contact WRN AI support.",
                    false,
                    402,
                    "api_error");

            if (statusCode == 404)
                return Failure(
                    RuntimeFailureKind.Policy,
                    "NO_ELIGIBLE_MODEL_ROUTE",
                    "This model is temporarily unavailable",
                    "No approved route is available for this model. Try another WRN model or try again later.",
                    false,
                    503,
                    "api_error");

            if (statusCode == 408)
                return Failure(
                    RuntimeFailureKind.Network,
                    "OPENROUTER_TIMEOUT",
                    "The model service timed out",
                    "Try again.",
                    false,
                    503,
                    "api_error");

            if (statusCode == 429)
                return Failure(
                    RuntimeFailureKind.RateLimit,
                    "OPENROUTER_RATE_LIMITED",
                    "The model service is busy",
                    "The model service is busy. Wait a moment and try again.",
                    false,
                    429,
                    "rate_limit_error");

            if (statusCode == 500
                || statusCode == 502
                || statusCode == 503
                || statusCode == 504)
            {
                return Failure(
                    RuntimeFailureKind.ServiceUnavailable,
                    "OPENROUTER_TEMPORARY_FAILURE",
                    "The model service is temporarily unavailable",
                    "The model service is temporarily unavailable. Try again in a moment.",
                    false,
                    503,
                    "api_error");
            }

            if (statusCode == 400
                || statusCode == 409
                || statusCode == 422)
            {
                return Failure(
                    RuntimeFailureKind.RequestRejected,
                    "MODEL_REQUEST_REJECTED",
                    "The selected model could not accept this request",
                    "Try again or choose another WRN model.",
                    false,
                    400,
                    "invalid_request_error");
            }

            return Failure(
                RuntimeFailureKind.Unknown,
                "OPENROUTER_REQUEST_FAILED",
                "WRN Claude could not complete the request",
                "Try again. If this keeps happening, contact WRN AI support.",
                false,
                503,
                "api_error");
        }

        public static RuntimeFailure TransportFailure()
        {
            return Failure(
                RuntimeFailureKind.Network,
                "OPENROUTER_UNREACHABLE",
                "The model service could not be reached",
                "Check your connection and try again.",
                false,
                503,
                "api_error");
        }

        public static bool IsSafeCredentialValidationRetry(
            string status)
        {
            return status == "KEY_VALIDATION_NETWORK_ERROR"
                || status == "KEY_VALIDATION_HTTP_408"
                || status == "KEY_VALIDATION_HTTP_500"
                || status == "KEY_VALIDATION_HTTP_502"
                || status == "KEY_VALIDATION_HTTP_503"
                || status == "KEY_VALIDATION_HTTP_504";
        }

        private static RuntimeFailure Failure(
            RuntimeFailureKind kind,
            string code,
            string title,
            string message,
            bool retryable,
            int httpStatus,
            string anthropicErrorType)
        {
            return new RuntimeFailure
            {
                Kind = kind,
                Code = code,
                Title = title,
                Message = message,
                Retryable = retryable,
                HttpStatus = httpStatus,
                AnthropicErrorType = anthropicErrorType
            };
        }
    }
}
