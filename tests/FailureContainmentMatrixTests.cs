using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WRN.AIGateway;

internal static class FailureContainmentMatrixTests
{
    private static int _failures;

    private static readonly Regex RawStatusToken =
        new Regex(
            @"\b[A-Z][A-Z0-9]+_[A-Z0-9_]+\b",
            RegexOptions.Compiled);

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

    private static void CheckSafeFailure(
        string name,
        RuntimeFailure failure)
    {
        var combined =
            (failure == null
                ? string.Empty
                : (failure.Title ?? string.Empty)
                    + " "
                    + (failure.Message ?? string.Empty));

        Check(
            name + " has friendly text",
            failure != null
            && !string.IsNullOrWhiteSpace(
                failure.Title)
            && !string.IsNullOrWhiteSpace(
                failure.Message));

        Check(
            name + " hides internal status tokens",
            !RawStatusToken.IsMatch(
                combined));

        Check(
            name + " hides raw technical payloads",
            combined.IndexOf(
                "Exception",
                StringComparison.OrdinalIgnoreCase) < 0
            && combined.IndexOf(
                "HRESULT",
                StringComparison.OrdinalIgnoreCase) < 0
            && combined.IndexOf(
                "stack trace",
                StringComparison.OrdinalIgnoreCase) < 0
            && combined.IndexOf(
                "{",
                StringComparison.Ordinal) < 0
            && combined.IndexOf(
                "/v1/",
                StringComparison.OrdinalIgnoreCase) < 0);
    }

    private static void CheckSafeMessage(
        string name,
        string message)
    {
        Check(
            name + " has friendly text",
            !string.IsNullOrWhiteSpace(
                message));

        Check(
            name + " hides internal status tokens",
            !RawStatusToken.IsMatch(
                message ?? string.Empty));

        Check(
            name + " hides raw technical payloads",
            (message ?? string.Empty).IndexOf(
                "Exception",
                StringComparison.OrdinalIgnoreCase) < 0
            && (message ?? string.Empty).IndexOf(
                "HRESULT",
                StringComparison.OrdinalIgnoreCase) < 0
            && (message ?? string.Empty).IndexOf(
                "{",
                StringComparison.Ordinal) < 0);
    }

    public static int Main()
    {
        CredentialMatrix();
        GatewayMatrix();
        UpstreamMatrix();
        UpdateMatrix();
        RetryMatrix();

        Console.WriteLine(
            _failures == 0
                ? "ALL_FAILURE_CONTAINMENT_MATRIX_TESTS_PASS"
                : "FAILURE_CONTAINMENT_MATRIX_TESTS_FAILED="
                    + _failures);

        return _failures == 0 ? 0 : 1;
    }

    private static void CredentialMatrix()
    {
        var cases =
            new Dictionary<string, RuntimeFailureKind>
            {
                {
                    "KEY_UNAUTHORIZED",
                    RuntimeFailureKind.Authentication
                },
                {
                    "INFERENCE_KEY_REQUIRED",
                    RuntimeFailureKind.Authentication
                },
                {
                    "KEY_FORMAT_INVALID",
                    RuntimeFailureKind.Authentication
                },
                {
                    "CREDENTIAL_NOT_CONFIGURED",
                    RuntimeFailureKind.Configuration
                },
                {
                    "KEY_VALIDATION_NETWORK_ERROR",
                    RuntimeFailureKind.Network
                },
                {
                    "KEY_VALIDATION_HTTP_402",
                    RuntimeFailureKind.UsageLimit
                },
                {
                    "KEY_VALIDATION_HTTP_429",
                    RuntimeFailureKind.RateLimit
                },
                {
                    "KEY_VALIDATION_HTTP_408",
                    RuntimeFailureKind.ServiceUnavailable
                },
                {
                    "KEY_VALIDATION_HTTP_500",
                    RuntimeFailureKind.ServiceUnavailable
                },
                {
                    "KEY_VALIDATION_HTTP_502",
                    RuntimeFailureKind.ServiceUnavailable
                },
                {
                    "KEY_VALIDATION_HTTP_503",
                    RuntimeFailureKind.ServiceUnavailable
                },
                {
                    "KEY_VALIDATION_HTTP_504",
                    RuntimeFailureKind.ServiceUnavailable
                },
                {
                    "UNRECOGNISED_FIXTURE_STATUS",
                    RuntimeFailureKind.Unknown
                }
            };

        foreach (var item in cases)
        {
            var failure =
                RuntimeFailureCatalog
                    .FromCredentialStatus(
                        item.Key);

            Check(
                "credential " + item.Key
                    + " kind",
                failure.Kind
                    == item.Value);

            CheckSafeFailure(
                "credential " + item.Key,
                failure);
        }
    }

    private static void GatewayMatrix()
    {
        var cases =
            new[]
            {
                "GATEWAY_CONFIG_INVALID:fixture",
                "GATEWAY_BINARY_MISSING",
                "OWNED_GATEWAY_RUNNING_BUT_UNHEALTHY",
                "GATEWAY_HEALTH_TIMEOUT",
                "GATEWAY_EXITED_BEFORE_HEALTHY",
                "GATEWAY_PROCESS_START_FAILED",
                "UNRECOGNISED_GATEWAY_STATUS"
            };

        foreach (var status in cases)
        {
            CheckSafeFailure(
                "gateway " + status,
                RuntimeFailureCatalog
                    .FromGatewayStatus(
                        status));
        }

        Check(
            "gateway health failure is retryable",
            RuntimeFailureCatalog
                .FromGatewayStatus(
                    "GATEWAY_HEALTH_TIMEOUT")
                .Retryable);

        Check(
            "missing gateway binary asks for repair",
            RuntimeFailureCatalog
                .FromGatewayStatus(
                    "GATEWAY_BINARY_MISSING")
                .Message
                .IndexOf(
                    "Reinstall",
                    StringComparison.OrdinalIgnoreCase)
                >= 0);
    }

    private static void UpstreamMatrix()
    {
        foreach (var status in
            new[]
            {
                400,
                401,
                402,
                403,
                404,
                408,
                409,
                418,
                422,
                429,
                500,
                502,
                503,
                504
            })
        {
            CheckSafeFailure(
                "upstream HTTP "
                    + status,
                RuntimeFailureCatalog
                    .FromUpstreamStatus(
                        status));
        }

        Check(
            "no approved route suggests another model",
            RuntimeFailureCatalog
                .FromUpstreamStatus(404)
                .Message
                .IndexOf(
                    "another WRN model",
                    StringComparison.OrdinalIgnoreCase)
                >= 0);

        Check(
            "rate limit keeps Anthropic-compatible type",
            RuntimeFailureCatalog
                .FromUpstreamStatus(429)
                .AnthropicErrorType
                == "rate_limit_error");

        CheckSafeFailure(
            "transport failure",
            RuntimeFailureCatalog
                .TransportFailure());
    }

    private static void UpdateMatrix()
    {
        var cases =
            new Dictionary<string, string>
            {
                {
                    "UPDATE_NETWORK_UNAVAILABLE",
                    "Try again"
                },
                {
                    "UPDATE_CHECK_FAILED",
                    "Try again"
                },
                {
                    "UPDATE_DEFERRED_CLAUDE_RUNNING",
                    "Close Claude"
                },
                {
                    "UPDATE_DEFERRED_GATEWAY_RUNNING",
                    "Close WRN Claude"
                },
                {
                    "UPDATE_DEFERRED_RECOVERY_PENDING",
                    "finish recovery"
                },
                {
                    "UPDATE_SIGNATURE_INVALID",
                    "current version was kept"
                },
                {
                    "UPDATE_HASH_MISMATCH",
                    "current version was kept"
                },
                {
                    "UPDATE_MANIFEST_INVALID",
                    "current version was kept"
                },
                {
                    "UPDATE_IDENTITY_INVALID",
                    "current version was kept"
                },
                {
                    "UPDATE_ARCHIVE_INVALID",
                    "current version was kept"
                },
                {
                    "UPDATE_ROLLBACK_REJECTED",
                    "current version was kept"
                },
                {
                    "UPDATE_HELPER_COPY_FAILED",
                    "Try again"
                },
                {
                    "UNKNOWN_UPDATE_FIXTURE",
                    "current version was kept"
                }
            };

        foreach (var item in cases)
        {
            var message =
                UserFacingFailureMessages
                    .AppUpdate(
                        item.Key);

            CheckSafeMessage(
                "update " + item.Key,
                message);

            Check(
                "update " + item.Key
                    + " gives recovery action",
                message.IndexOf(
                    item.Value,
                    StringComparison.OrdinalIgnoreCase)
                >= 0);
        }
    }

    private static void RetryMatrix()
    {
        Check(
            "credential retry only covers safe transient validation",
            RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_VALIDATION_NETWORK_ERROR")
            && RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_VALIDATION_HTTP_408")
            && RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_VALIDATION_HTTP_503")
            && !RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_UNAUTHORIZED")
            && !RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_VALIDATION_HTTP_402")
            && !RuntimeFailureCatalog
                .IsSafeCredentialValidationRetry(
                    "KEY_VALIDATION_HTTP_429"));

        Check(
            "inference transport remains non-replayable",
            !RuntimeFailureCatalog
                .TransportFailure()
                .Retryable);
    }
}
