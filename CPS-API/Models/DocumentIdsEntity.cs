using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class DocumentIdsEntity : TableEntity
    {
        public string ObjectId { get; set; }

        public string SiteId { get; set; }

        public string ListId { get; set; }

        public string ListItemId { get; set; }

        public string DriveId { get; set; }

        public string DriveItemId { get; set; }

        public DocumentIdsEntity()
        {

        }

        public DocumentIdsEntity(string objectId, ObjectIds ids)
        {
            PartitionKey = objectId;
            RowKey = ids.SiteId + ids.ListId + ids.ListItemId;
            ObjectId = objectId;
            SiteId = ids.SiteId;
            ListId = ids.ListId;
            ListItemId = ids.ListItemId;
            DriveId = ids.DriveId;
            DriveItemId = ids.DriveItemId;
        }
    }
}