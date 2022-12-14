namespace CPS_API.Models
{
    public class ContentIds
    {
        public string ContentId { get; set; }

        public string SiteId { get; set; }
        public string WebId { get; set; }
        public string ListId { get; set; }
        public string ListItemId { get; set; }

        public string DriveId { get; set; }
        public string DriveItemId { get; set; }

        public ContentIds()
        {

        }

        public ContentIds(DocumentIdsEntity documentIdsEntity)
        {
            ContentId = documentIdsEntity.ContentId;
            SiteId = documentIdsEntity.SiteId;
            WebId = documentIdsEntity.WebId;
            ListId = documentIdsEntity.ListId;
            ListItemId = documentIdsEntity.ListItemId;
            DriveId = documentIdsEntity.DriveId;
            DriveItemId = documentIdsEntity.DriveItemId;
        }
    }
}
