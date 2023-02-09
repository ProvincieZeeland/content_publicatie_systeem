using Microsoft.Graph;

namespace CPS_API.Models
{
    public class DeltaDriveItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DriveId { get; set; }
        public Folder Folder { get; set; }

        public Deleted Deleted { get; set; }

        public DateTimeOffset? CreatedDateTime { get; set; }
        public DateTimeOffset? LastModifiedDateTime { get; set; }
    }
}
