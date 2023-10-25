using System;
using Newtonsoft.Json;

namespace CPS_Jobs.Models
{
    public class SubscriptionModel
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        [JsonProperty(PropertyName = "clientState", NullValueHandling = NullValueHandling.Ignore)]
        public string ClientState { get; set; }

        [JsonProperty(PropertyName = "expirationDateTime")]
        public DateTime ExpirationDateTime { get; set; }

        [JsonProperty(PropertyName = "notificationUrl")]
        public string NotificationUrl { get; set; }

        [JsonProperty(PropertyName = "resource", NullValueHandling = NullValueHandling.Ignore)]
        public string Resource { get; set; }
    }
}