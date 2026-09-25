using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;
using WRN.AIGateway.Gateway;

internal static class GatewayPolicyTests
{
    private static int _failures;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    private static void Check(string name, bool condition)
    {
        if (condition)
            Console.WriteLine("PASS " + name);
        else
        {
            Console.WriteLine("FAIL " + name);
            _failures++;
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Expected: <catalogue.json> <catalogue.sig>");
            return 2;
        }

        var bytes = File.ReadAllBytes(args[0]);
        var sig = File.ReadAllText(args[1]);
        CatalogueDocument catalogue;
        string error;
        Check(
            "signed catalogue accepted",
            CatalogueVerifier.TryVerifyAndParse(bytes, sig, out catalogue, out error));

        if (catalogue == null)
            return 1;

        var visible = catalogue.models.Where(m => m.visible).ToArray();
        Check("four visible routes", visible.Length == 4);

        var selected = visible.First(m => m.key == catalogue.defaultModelKey);
        var request = new Dictionary<string, object>
        {
            { "model", selected.claudeAlias },
            {
                "provider",
                new Dictionary<string, object>
                {
                    { "zdr", false },
                    { "data_collection", "allow" }
                }
            },
            { "max_tokens", 32 },
            { "messages", new object[0] }
        };

        var result = GatewayPolicy.Rewrite(
            Encoding.UTF8.GetBytes(Json.Serialize(request)),
            catalogue);

        Check("catalogue alias allowed", result.Allowed);
        Check("alias resolves dynamically", result.UpstreamModel == selected.upstreamModel);

        var outbound = Json.DeserializeObject(
            Encoding.UTF8.GetString(result.OutboundBody))
            as Dictionary<string, object>;

        Check(
            "outbound model rewritten",
            Convert.ToString(outbound["model"]) == selected.upstreamModel);

        var provider = outbound["provider"] as Dictionary<string, object>;
        Check("ZDR forced true", provider != null && Convert.ToBoolean(provider["zdr"]));
        Check(
            "data collection forced deny",
            provider != null && Convert.ToString(provider["data_collection"]) == "deny");
        Check(
            "same-model provider failover explicitly enabled",
            provider != null
            && Convert.ToBoolean(provider["allow_fallbacks"]));

        var hostile = new Dictionary<string, object>(request);
        hostile["models"] = new object[]
        {
            selected.upstreamModel,
            "other/provider-model"
        };
        hostile["provider"] =
            new Dictionary<string, object>
            {
                { "zdr", false },
                { "data_collection", "allow" },
                { "allow_fallbacks", false },
                { "only", new object[] { "caller-provider" } },
                { "order", new object[] { "caller-provider" } },
                { "sort", "latency" }
            };

        var hostileResult = GatewayPolicy.Rewrite(
            Encoding.UTF8.GetBytes(Json.Serialize(hostile)),
            catalogue);
        Check(
            "hostile caller routing still allowed through WRN policy",
            hostileResult.Allowed);

        var hostileOutbound =
            Json.DeserializeObject(
                Encoding.UTF8.GetString(
                    hostileResult.OutboundBody))
            as Dictionary<string, object>;
        Check(
            "cross-model fallback removed",
            hostileOutbound != null
            && !hostileOutbound.ContainsKey("models"));

        var hostileProvider =
            hostileOutbound["provider"]
            as Dictionary<string, object>;
        Check(
            "caller provider allowlist removed",
            hostileProvider != null
            && !hostileProvider.ContainsKey("only")
            && !hostileProvider.ContainsKey("order")
            && !hostileProvider.ContainsKey("sort"));
        Check(
            "caller cannot disable provider fallback",
            hostileProvider != null
            && Convert.ToBoolean(
                hostileProvider["allow_fallbacks"]));
        Check(
            "caller cannot weaken privacy routing",
            hostileProvider != null
            && Convert.ToBoolean(hostileProvider["zdr"])
            && Convert.ToString(
                hostileProvider["data_collection"])
                == "deny");

        var direct = new Dictionary<string, object>(request);
        direct["model"] = selected.upstreamModel;
        var directResult = GatewayPolicy.Rewrite(
            Encoding.UTF8.GetBytes(Json.Serialize(direct)),
            catalogue);
        Check("direct upstream ID rejected", !directResult.Allowed && directResult.Error == "unsupported_model");

        var unknown = new Dictionary<string, object>(request);
        unknown["model"] = "anthropic/claude-wrn-does-not-exist";
        var unknownResult = GatewayPolicy.Rewrite(
            Encoding.UTF8.GetBytes(Json.Serialize(unknown)),
            catalogue);
        Check("unknown alias rejected", !unknownResult.Allowed && unknownResult.Error == "unsupported_model");

        var missing = new Dictionary<string, object>
        {
            { "messages", new object[0] }
        };
        var missingResult = GatewayPolicy.Rewrite(
            Encoding.UTF8.GetBytes(Json.Serialize(missing)),
            catalogue);
        Check("missing model rejected", !missingResult.Allowed && missingResult.Error == "missing_model");

        var invalidResult = GatewayPolicy.Rewrite(
            Encoding.UTF8.GetBytes("{bad-json"),
            catalogue);
        Check("invalid JSON rejected", !invalidResult.Allowed && invalidResult.Error == "invalid_json");

        Console.WriteLine(
            _failures == 0
                ? "ALL_GATEWAY_POLICY_TESTS_PASS"
                : "GATEWAY_POLICY_TESTS_FAILED=" + _failures);

        return _failures == 0 ? 0 : 1;
    }
}
