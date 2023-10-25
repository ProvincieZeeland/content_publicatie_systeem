namespace CPS_API.Models
{
    public class ObjectIdentifiers
    {
        public string? ObjectId { get; set; }

        public string? SiteId { get; set; }

        public string? ListId { get; set; }

        public string? ListItemId { get; set; }

        public string? DriveId { get; set; }

        public string? DriveItemId { get; set; }

        public string? ExternalReferenceListId { get; set; }

        public string? AdditionalObjectId { get; set; }

        public ObjectIdentifiers()
        {

        }

        public ObjectIdentifiers(ObjectIdentifiersEntity entity)
        {
            ObjectId = entity.ObjectId;
            SiteId = entity.SiteId;
            ListId = entity.ListId;
            ListItemId = entity.ListItemId;
            DriveId = entity.DriveId;
            DriveItemId = entity.DriveItemId;
            ExternalReferenceListId = entity.ExternalReferenceListId;
            AdditionalObjectId = entity.AdditionalObjectId;
        }

        public ObjectIdentifiers clone()
        {
            var objectIdentifiers = new ObjectIdentifiers();
            objectIdentifiers.ObjectId = ObjectId;
            objectIdentifiers.SiteId = SiteId;
            objectIdentifiers.ListId = ListId;
            objectIdentifiers.ListItemId = ListItemId;
            objectIdentifiers.DriveId = DriveId;
            objectIdentifiers.DriveItemId = DriveItemId;
            objectIdentifiers.ExternalReferenceListId = ExternalReferenceListId;
            objectIdentifiers.AdditionalObjectId = AdditionalObjectId;
            return objectIdentifiers;
        }
    }
}
