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

        public DateTimeOffset? PublicationDate { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset? ArchiveDate { get; set; } = DateTimeOffset.MinValue;

        public string? WOOInformationCategoryPrimary { get; set; } = "";

        public string? WOOInformationCategorySecondary { get; set; } = "";

        public DateTimeOffset? DocumentDate { get; set; } = DateTimeOffset.MinValue;

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
            PublicationDate = metadata.PublicationDate ?? DateTimeOffset.MinValue;
            ArchiveDate = metadata.ArchiveDate ?? DateTimeOffset.MinValue;
            WOOInformationCategoryPrimary = metadata.WOOInformationCategoryPrimary ?? string.Empty;
            WOOInformationCategorySecondary = metadata.WOOInformationCategorySecondary ?? string.Empty;
            DocumentDate = metadata.DocumentDate ?? DateTimeOffset.MinValue;
        }
    }
}
