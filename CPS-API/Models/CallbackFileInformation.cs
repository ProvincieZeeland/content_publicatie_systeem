namespace CPS_API.Models
{
    public class CallbackFileInformation
    {
        public string? MimeType { get; set; }

        public string? FileName { get; set; }

        public string? FileExtension { get; set; }

        public DateTime? CreatedOn { get; set; }

        public DateTime? ModifiedOn { get; set; }

        public DateTime? SourceCreatedOn { get; set; }

        public DateTime? SourceModifiedOn { get; set; }

        public CallbackFileMetadata? AdditionalMetadata { get; set; }

        public CallbackFileInformation()
        {

        }

        public CallbackFileInformation(FileInformation fileInfo)
        {
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
