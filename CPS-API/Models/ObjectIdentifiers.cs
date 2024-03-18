using System.Text.Json.Serialization;
using CPS_API.Helpers;

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

        [JsonIgnore]
        public object? this[string fieldname]
        {
            get
            {
                var property = this.GetType().GetProperty(fieldname);
                if (property != null)
                    return property.GetValue(this);
                else
                    throw new ArgumentException("Unknown property " + fieldname);
            }

            set
            {
                FieldPropertyHelper.SetFieldValue(this, fieldname, value);
            }
        }

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
            return MetadataHelper.clone<ObjectIdentifiers>(this);
        }
    }
}