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
            if (fileInfo == null) return;

            MimeType = fileInfo.MimeType;
            FileName = fileInfo.FileName;
            FileExtension = fileInfo.FileExtension;
            CreatedOn = fileInfo.CreatedOn;
            ModifiedOn = fileInfo.ModifiedOn;
            SourceCreatedOn = fileInfo.SourceCreatedOn;
            SourceModifiedOn = fileInfo.SourceModifiedOn;
            AdditionalMetadata = new CallbackFileMetadata(fileInfo.AdditionalMetadata);
        }
    }
}
