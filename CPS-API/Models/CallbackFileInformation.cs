namespace CPS_API.Models
{
    public class CallbackFileInformation
    {
        public string? MimeType { get; set; } = "";

        public string? FileName { get; set; } = "";

        public string? FileExtension { get; set; } = "";

        public DateTime? CreatedOn { get; set; } = DateTime.MinValue;

        public DateTime? ModifiedOn { get; set; } = DateTime.MinValue;

        public DateTime? SourceCreatedOn { get; set; } = DateTime.MinValue;

        public DateTime? SourceModifiedOn { get; set; } = DateTime.MinValue;

        public CallbackFileMetadata? AdditionalMetadata { get; set; }

        public CallbackFileInformation()
        {

        }

        public CallbackFileInformation(FileInformation fileInfo)
        {
            MimeType = fileInfo.MimeType ?? string.Empty;
            FileName = fileInfo.FileName ?? string.Empty;
            FileExtension = fileInfo.FileExtension ?? string.Empty;
            CreatedOn = fileInfo.CreatedOn ?? DateTime.MinValue;
            ModifiedOn = fileInfo.ModifiedOn ?? DateTime.MinValue;
            SourceCreatedOn = fileInfo.SourceCreatedOn ?? DateTime.MinValue;
            SourceModifiedOn = fileInfo.SourceModifiedOn ?? DateTime.MinValue;
            AdditionalMetadata = fileInfo.AdditionalMetadata == null ? new CallbackFileMetadata() : new CallbackFileMetadata(fileInfo.AdditionalMetadata);
        }
    }
}