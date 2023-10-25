namespace CPS_API.Models
{
    public class SharePointListItemsDelta
    {
        public bool ChangeTokenInvalid { get; set; }

        public string NewChangeToken { get; set; }

        public List<SharePointListItemDelta> Items { get; set; }

        public SharePointListItemsDelta()
        {
        }

        public SharePointListItemsDelta(
            bool changeTokenInvalid)
        {
            ChangeTokenInvalid = changeTokenInvalid;
        }
    }
}
