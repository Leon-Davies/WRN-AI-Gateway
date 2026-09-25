using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using WRN.AIGateway;

namespace WRN.AIGateway.Gateway
{
    internal sealed class GatewayRewriteResult
    {
        public bool Allowed { get; set; }
        public string Error { get; set; }
        public string RequestedModel { get; set; }
        public string UpstreamModel { get; set; }
        public byte[] OutboundBody { get; set; }
    }

    internal static class GatewayPolicy
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 512
        };

        public static GatewayRewriteResult Rewrite(byte[] requestBody, CatalogueDocument catalogue)
        {
            if (catalogue == null || catalogue.models == null)
                return Reject("catalogue_unavailable");

            Dictionary<string, object> root;
            try
            {
                root = Json.DeserializeObject(Encoding.UTF8.GetString(requestBody ?? new byte[0]))
                    as Dictionary<string, object>;
            }
            catch
            {
                return Reject("invalid_json");
            }

            if (root == null)
                return Reject("invalid_json");

            object modelValue;
            if (!root.TryGetValue("model", out modelValue) || modelValue == null)
                return Reject("missing_model");

            var requested = Convert.ToString(modelValue);
            var model = catalogue.models.FirstOrDefault(delegate(CatalogueModel item)
            {
                return item.visible
                    && string.Equals(item.claudeAlias, requested, StringComparison.OrdinalIgnoreCase);
            });

            if (model == null)
                return Reject("unsupported_model", requested);

            root["model"] = model.upstreamModel;

            object providerValue;
            Dictionary<string, object> provider = null;
            if (root.TryGetValue("provider", out providerValue))
                provider = providerValue as Dictionary<string, object>;

            if (provider == null)
            {
                provider = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                root["provider"] = provider;
            }

            provider["zdr"] = true;
            provider["data_collection"] = "deny";

            return new GatewayRewriteResult
            {
                Allowed = true,
                Error = null,
                RequestedModel = requested,
                UpstreamModel = model.upstreamModel,
                OutboundBody = Encoding.UTF8.GetBytes(Json.Serialize(root))
            };
        }

        private static GatewayRewriteResult Reject(string error, string requested = null)
        {
            return new GatewayRewriteResult
            {
                Allowed = false,
                Error = error,
                RequestedModel = requested,
                OutboundBody = null
            };
        }
    }
}
