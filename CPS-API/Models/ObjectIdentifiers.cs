using Microsoft.IdentityModel.Tokens;

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
        public List<string>? AdditionalObjectIds { get; set; }

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

            if (string.IsNullOrEmpty(entity.AdditionalObjectIds))
                AdditionalObjectIds = new List<string>();
            else
                AdditionalObjectIds = entity.AdditionalObjectIds.Split(';').ToList<string>();
        }
    }
}
