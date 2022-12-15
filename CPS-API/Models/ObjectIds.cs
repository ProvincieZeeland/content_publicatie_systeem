namespace CPS_API.Models
{
    public class ObjectIds
    {
        public string ObjectId { get; set; }

        public string SiteId { get; set; }

        public string ListId { get; set; }

        public string ListItemId { get; set; }

        public string DriveId { get; set; }

        public string DriveItemId { get; set; }

        public ObjectIds()
        {

        }

        public ObjectIds(DocumentIdsEntity documentIdsEntity)
        {
            ObjectId = documentIdsEntity.ObjectId;
            SiteId = documentIdsEntity.SiteId;
            ListId = documentIdsEntity.ListId;
            ListItemId = documentIdsEntity.ListItemId;
            DriveId = documentIdsEntity.DriveId;
            DriveItemId = documentIdsEntity.DriveItemId;
        }
    }
}
