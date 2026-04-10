using CPS_API.Helpers;

namespace CPS_API.Models
{
    public partial class ObjectIdentifiers
    {
        public Guid? Id { get; set; }

        public string ObjectId { get; set; } = string.Empty;

        public string? AdditionalObjectId { get; set; }

        public string DriveId { get; set; } = string.Empty;

        public string DriveItemId { get; set; } = string.Empty;

        public string SiteId { get; set; } = string.Empty;

        public string ListId { get; set; } = string.Empty;

        public string ListItemId { get; set; } = string.Empty;

        public ObjectIdentifiers clone()
        {
            return MetadataHelper.clone<ObjectIdentifiers>(this);
        }
    }
}
