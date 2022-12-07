namespace CPS_API.Models
{

    public class FileInformation
    {
        public ContentIds Ids { get; set; }

        public string MimeType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string FileExtension { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; }

        public string CreatedBy { get; set; } = string.Empty;

        public DateTime ModifiedOn { get; set; }

        public string ModifiedBy { get; set; } = string.Empty;

        public FileMetadata? AdditionalMetadata { get; set; }

        public List<ExternalReferences> ExternalReferences { get; set; } = new List<ExternalReferences>();
    }
}
