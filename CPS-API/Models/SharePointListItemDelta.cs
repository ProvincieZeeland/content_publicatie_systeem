using Microsoft.SharePoint.Client;

namespace CPS_API.Models
{
    public class SharePointListItemDelta
    {
        public int ListItemId { get; set; }

        public ChangeType ChangeType { get; set; }

        public SharePointListItemDelta(int listItemId, ChangeType changeType)
        {
            ListItemId = listItemId;
            ChangeType = changeType;
        }
    }
}
