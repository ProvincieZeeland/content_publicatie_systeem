﻿using Microsoft.Graph;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class DocumentsEntity : TableEntity
    {
        public string SiteId { get; set; }

        public string WebId { get; set; }

        public string ListId { get; set; }

        public string ListItemId { get; set; }

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
            this.ListItemId = ids.ListItemId.ToString();
            this.DriveId = drive == null ? ids.DriveId.ToString() : drive.Id;
            this.DriveItemId = driveItem == null ? ids.DriveItemId.ToString() : driveItem.Id;
        }
    }
}
