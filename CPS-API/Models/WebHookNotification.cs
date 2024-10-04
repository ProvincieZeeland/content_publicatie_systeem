using Newtonsoft.Json;

namespace CPS_API.Models
{
    public class WebHookNotification
    {
        [JsonProperty(PropertyName = "subscriptionId")]
        public string SubscriptionId { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "clientState")]
        public string? ClientState { get; set; }

        [JsonProperty(PropertyName = "expirationDateTime")]
        public DateTime ExpirationDateTime { get; set; }

        [JsonProperty(PropertyName = "resource")]
        public string Resource { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "tenantId")]
        public string TenantId { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "siteUrl")]
        public string SiteUrl { get; set; } = string.Empty;

        [JsonProperty(PropertyName = "webId")]
        public string WebId { get; set; } = string.Empty;
    }
}