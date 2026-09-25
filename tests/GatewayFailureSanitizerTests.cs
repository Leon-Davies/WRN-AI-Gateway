using System;
using System.Text;
using WRN.AIGateway;
using WRN.AIGateway.Gateway;

internal static class GatewayFailureSanitizerTests
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
        var rawJson =
            @"{""type"":""error"",""error"":{""type"":""rate_limit_error"",""message"":""openai/gpt-6-luna is temporarily rate-limited upstream. Please retry shortly, or add your own key at https://openrouter.ai/settings/integrations""}}";

        RuntimeFailure jsonFailure;
        Check(
            "HTTP-200 JSON error detected",
            GatewayFailureSanitizer.TryMapJsonError(
                Encoding.UTF8.GetBytes(rawJson),
                out jsonFailure));

        Check(
            "JSON rate-limit error normalized",
            jsonFailure != null
            && jsonFailure.Code
                == "OPENROUTER_RATE_LIMITED"
            && jsonFailure.Message.IndexOf(
                "gpt-6-luna",
                StringComparison.OrdinalIgnoreCase) < 0
            && jsonFailure.Message.IndexOf(
                "openrouter.ai",
                StringComparison.OrdinalIgnoreCase) < 0);

        var rawSse =
            "data: "
            + rawJson;

        string sanitized;
        RuntimeFailure sseFailure;
        Check(
            "SSE error event detected",
            GatewayFailureSanitizer.TrySanitizeSseDataLine(
                rawSse,
                out sanitized,
                out sseFailure));

        Check(
            "SSE raw provider detail removed",
            sanitized.IndexOf(
                "gpt-6-luna",
                StringComparison.OrdinalIgnoreCase) < 0
            && sanitized.IndexOf(
                "openrouter.ai",
                StringComparison.OrdinalIgnoreCase) < 0);

        Check(
            "SSE friendly error retained",
            sanitized.IndexOf(
                "model service is busy",
                StringComparison.OrdinalIgnoreCase) >= 0
            && sanitized.IndexOf(
                "rate_limit_error",
                StringComparison.OrdinalIgnoreCase) >= 0);

        var normalJson =
            @"{""type"":""message"",""content"":[{""type"":""text"",""text"":""hello""}]}";
        RuntimeFailure normalFailure;
        Check(
            "normal JSON response not treated as error",
            !GatewayFailureSanitizer.TryMapJsonError(
                Encoding.UTF8.GetBytes(normalJson),
                out normalFailure));

        var normalSse =
            @"data: {""type"":""content_block_delta"",""delta"":{""type"":""text_delta"",""text"":""hello""}}";
        string normalSanitized;
        RuntimeFailure normalSseFailure;
        Check(
            "normal SSE event not altered",
            !GatewayFailureSanitizer.TrySanitizeSseDataLine(
                normalSse,
                out normalSanitized,
                out normalSseFailure)
            && normalSanitized == normalSse);

        Console.WriteLine(
            _failures == 0
                ? "ALL_GATEWAY_FAILURE_SANITIZER_TESTS_PASS"
                : "GATEWAY_FAILURE_SANITIZER_TESTS_FAILED="
                    + _failures);

        return _failures == 0 ? 0 : 1;
    }
}
