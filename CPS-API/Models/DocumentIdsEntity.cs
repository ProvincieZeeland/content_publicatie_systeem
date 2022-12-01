using Microsoft.Graph;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class DocumentIdsEntity : TableEntity
    {
        public ContentIds ContentIds { get; set; }

        public DocumentIdsEntity()
        {

        }

        public DocumentIdsEntity(string contentId, ContentIds ids)
        {
            this.PartitionKey = contentId;
            this.RowKey = ids.SiteId + ids.WebId + ids.ListId + ids.ListItemId;
            this.ContentIds = ids;
        }
    }
}
