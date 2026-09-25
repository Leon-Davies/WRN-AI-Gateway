using System;
using WRN.AIGateway;

internal static class RuntimeFailureTests
{
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

    public static int Main()
    {
        var auth =
            RuntimeFailureCatalog
                .FromCredentialStatus(
                    "KEY_UNAUTHORIZED");
        Check(
            "credential auth failure normalized",
            auth.Kind
                == RuntimeFailureKind.Authentication
            && !auth.Retryable
            && auth.Message.IndexOf(
                "WRN AI Gateway Settings",
                StringComparison.Ordinal) >= 0);

        var network =
            RuntimeFailureCatalog
                .FromCredentialStatus(
                    "KEY_VALIDATION_NETWORK_ERROR");
        Check(
            "credential network failure retryable",
            network.Retryable
            && network.Kind
                == RuntimeFailureKind.Network);

        Check(
            "credential retry limited to transient statuses",
            RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_VALIDATION_NETWORK_ERROR")
            && RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_VALIDATION_HTTP_503")
            && !RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_UNAUTHORIZED")
            && !RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_VALIDATION_HTTP_429"));

        var noRoute =
            RuntimeFailureCatalog
                .FromUpstreamStatus(404);
        Check(
            "no eligible route is friendly policy failure",
            noRoute.Kind
                == RuntimeFailureKind.Policy
            && noRoute.Code
                == "NO_ELIGIBLE_MODEL_ROUTE"
            && noRoute.Message.IndexOf(
                "approved route",
                StringComparison.OrdinalIgnoreCase)
                >= 0);

        var busy =
            RuntimeFailureCatalog
                .FromUpstreamStatus(429);
        Check(
            "rate limit maps to Anthropic rate-limit error",
            busy.HttpStatus == 429
            && busy.AnthropicErrorType
                == "rate_limit_error");

        var service =
            RuntimeFailureCatalog
                .FromUpstreamStatus(503);
        Check(
            "service outage maps to generic friendly error",
            service.HttpStatus == 503
            && service.Message.IndexOf(
                "temporarily unavailable",
                StringComparison.OrdinalIgnoreCase)
                >= 0);

        var rejected =
            RuntimeFailureCatalog
                .FromUpstreamStatus(400);
        Check(
            "bad model request does not expose upstream details",
            rejected.Code
                == "MODEL_REQUEST_REJECTED"
            && rejected.AnthropicErrorType
                == "invalid_request_error");

        var gateway =
            RuntimeFailureCatalog
                .FromGatewayStatus(
                    "GATEWAY_HEALTH_TIMEOUT");
        Check(
            "gateway start failure normalized",
            gateway.Code
                == "GATEWAY_START_FAILED"
            && gateway.Retryable);

        var transport =
            RuntimeFailureCatalog
                .TransportFailure();
        Check(
            "inference transport failure is not auto-retried",
            !transport.Retryable
            && transport.HttpStatus == 503);

        Console.WriteLine(
            _failures == 0
                ? "ALL_RUNTIME_FAILURE_TESTS_PASS"
                : "RUNTIME_FAILURE_TESTS_FAILED="
                    + _failures);

        return _failures == 0 ? 0 : 1;
    }
}
