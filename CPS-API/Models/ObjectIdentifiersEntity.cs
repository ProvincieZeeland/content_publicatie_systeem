using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class ObjectIdentifiersEntity : TableEntity
    {
        public string ObjectId { get; set; } = string.Empty;

        public string SiteId { get; set; } = string.Empty;

        public string ListId { get; set; } = string.Empty;

        public string ListItemId { get; set; } = string.Empty;

        public string DriveId { get; set; } = string.Empty;

        public string DriveItemId { get; set; } = string.Empty;

        public string ExternalReferenceListId { get; set; } = string.Empty;

        public string? AdditionalObjectId { get; set; }

        public ObjectIdentifiersEntity()
        {

        }

        public ObjectIdentifiersEntity(string objectId, ObjectIdentifiers ids)
        {
            PartitionKey = objectId;
            RowKey = ids.SiteId + ids.ListId + ids.ListItemId;
            ObjectId = objectId;
            SiteId = ids.SiteId ?? string.Empty;
            ListId = ids.ListId ?? string.Empty;
            ListItemId = ids.ListItemId ?? string.Empty;
            DriveId = ids.DriveId ?? string.Empty;
            DriveItemId = ids.DriveItemId ?? string.Empty;
            ExternalReferenceListId = ids.ExternalReferenceListId ?? string.Empty;
            AdditionalObjectId = ids.AdditionalObjectId;
        }
    }
}