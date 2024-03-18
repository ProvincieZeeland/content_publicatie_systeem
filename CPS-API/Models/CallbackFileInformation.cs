namespace CPS_API.Models
{
    public class CallbackFileInformation
    {
        public string? MimeType { get; set; } = "";

        public string? FileName { get; set; } = "";

        public string? FileExtension { get; set; } = "";

        public DateTimeOffset? CreatedOn { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset? ModifiedOn { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset? SourceCreatedOn { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset? SourceModifiedOn { get; set; } = DateTimeOffset.MinValue;

        public CallbackFileMetadata? AdditionalMetadata { get; set; }

        public CallbackFileInformation()
        {

        }

        public CallbackFileInformation(FileInformation fileInfo)
        {
            MimeType = fileInfo.MimeType ?? string.Empty;
            FileName = fileInfo.FileName ?? string.Empty;
            FileExtension = fileInfo.FileExtension ?? string.Empty;
            CreatedOn = fileInfo.CreatedOn ?? DateTimeOffset.MinValue;
            ModifiedOn = fileInfo.ModifiedOn ?? DateTimeOffset.MinValue;
            SourceCreatedOn = fileInfo.SourceCreatedOn ?? DateTimeOffset.MinValue;
            SourceModifiedOn = fileInfo.SourceModifiedOn ?? DateTimeOffset.MinValue;
            AdditionalMetadata = new CallbackFileMetadata(fileInfo.AdditionalMetadata);
        }
    }
}
