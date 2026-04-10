using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class WebHookNotification
    {
        [JsonPropertyName("subscriptionId")]
        public string SubscriptionId { get; set; } = string.Empty;

        [JsonPropertyName("clientState")]
        public string? ClientState { get; set; }

        [JsonPropertyName("expirationDateTime")]
        public DateTime ExpirationDateTime { get; set; }

        [JsonPropertyName("resource")]
        public string Resource { get; set; } = string.Empty;

        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; } = string.Empty;

        [JsonPropertyName("siteUrl")]
        public string SiteUrl { get; set; } = string.Empty;

        [JsonPropertyName("webId")]
        public string WebId { get; set; } = string.Empty;
    }
}