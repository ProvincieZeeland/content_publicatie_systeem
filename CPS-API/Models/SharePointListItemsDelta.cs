namespace CPS_API.Models
{
    public class SharePointListItemsDelta
    {
        public string NewChangeToken { get; set; }

        public List<SharePointListItemDelta> Items { get; set; }

        public SharePointListItemsDelta()
        {
        }
    }
}
