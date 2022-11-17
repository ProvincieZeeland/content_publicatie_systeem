namespace CPS_API.Models
{
    public class ContentIds
    {
        public string ContentId { get; set; }

        public Guid SiteId { get; set; }
        public Guid WebId { get; set; }
        public Guid ListId { get; set; }        
        public int ListItemId { get; set; }

        public Guid DriveId { get; set; }
        public Guid DriveItemId { get; set; }
    }
}
