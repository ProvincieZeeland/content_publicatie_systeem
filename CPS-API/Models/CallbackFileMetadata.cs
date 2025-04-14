
namespace CPS_API.Models
{
    public class CallbackFileMetadata
    {
        public string? Author { get; set; } = "";

        public string? Title { get; set; } = "";

        public string? DocumentType { get; set; } = "";

        public string? ZeesterDocumentType { get; set; } = "";

        public string? ZeesterReference { get; set; } = "";

        public int? RetentionPeriod { get; set; } = 0;

        public string? Classification { get; set; } = "";

        public DateTime? PublicationDate { get; set; } = DateTime.MinValue;

        public DateTime? ArchiveDate { get; set; } = DateTime.MinValue;

        public string? WOOInformationCategoryPrimary { get; set; } = "";

        public string? WOOInformationCategorySecondary { get; set; } = "";

        public DateTime? DocumentDate { get; set; } = DateTime.MinValue;

        public CallbackFileMetadata()
        {

        }

        public CallbackFileMetadata(FileMetadata metadata)
        {
            Author = metadata.Author ?? string.Empty;
            Title = metadata.Title ?? string.Empty;
            DocumentType = metadata.DocumentType ?? string.Empty;
            ZeesterDocumentType = metadata.ZeesterDocumentType ?? string.Empty;
            ZeesterReference = metadata.ZeesterReference ?? string.Empty;
            RetentionPeriod = metadata.RetentionPeriod ?? 0;
            Classification = metadata.Classification ?? string.Empty;
            PublicationDate = metadata.PublicationDate ?? DateTime.MinValue;
            ArchiveDate = metadata.ArchiveDate ?? DateTime.MinValue;
            WOOInformationCategoryPrimary = metadata.WOOInformationCategoryPrimary ?? string.Empty;
            WOOInformationCategorySecondary = metadata.WOOInformationCategorySecondary ?? string.Empty;
            DocumentDate = metadata.DocumentDate ?? DateTime.MinValue;
        }
    }
}
