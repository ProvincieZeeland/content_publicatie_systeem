using Microsoft.Graph;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class DocumentsEntity : TableEntity
    {
        public Guid SiteId { get; set; }

        public Guid WebId { get; set; }

        public Guid ListId { get; set; }

        public int ListItemId { get; set; }

        public string DriveId { get; set; }

        public string DriveItemId { get; set; }

        public DocumentsEntity()
        {

        }

        public DocumentsEntity(string contentId, Drive? drive, DriveItem? driveItem, ContentIds ids)
        {
            this.PartitionKey = contentId;
            this.RowKey = contentId;
            this.SiteId = ids.SiteId;
            this.WebId = ids.WebId;
            this.ListId = ids.ListId;
            this.ListItemId = ids.ListItemId;
            this.DriveId = drive == null ? ids.DriveId.ToString() : drive.Id;
            this.DriveItemId = driveItem == null ? ids.DriveItemId.ToString() : driveItem.Id;
        }
    }
}
