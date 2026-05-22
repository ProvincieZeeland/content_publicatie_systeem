namespace CPS_API.Models
{
    public enum WebhookType
    {
        DropOff = 0,
        PublicSync
    }

    public partial class WebhookSubscription
    {
        public long Id { get; set; }

        public string? LastChangeToken { get; set; }

        public DateTime SubscriptionExpirationDateTime { get; set; }

        public string SubscriptionId { get; set; } = string.Empty;

        public WebhookType WebhookType { get; set; }
    }
}
