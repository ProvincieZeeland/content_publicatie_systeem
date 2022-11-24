namespace CPS_API.Models
{
    public class FileMetadata
    {
        public string Author { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string DocumentType { get; set; } = string.Empty;

        public string ZeesterReference { get; set; } = string.Empty;

        public Classification Classification { get; set; }

        public int RetentionPeriod { get; set; } = 0;

        public DateTime PublicationDate { get; set; } = DateTime.MinValue;

        public DateTime ArchiveDate { get; set; } = DateTime.MinValue;

        public string Source { get; set; } = string.Empty;
    }
}
