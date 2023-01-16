using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class CallbackFileMetadata
    {
        public string Author { get; set; }

        public string Title { get; set; }

        public string DocumentType { get; set; }

        public string ZeesterDocumentType { get; set; }

        public string ZeesterReference { get; set; }

        public int RetentionPeriod { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Classification Classification { get; set; }

        public DateTime PublicationDate { get; set; }

        public DateTime ArchiveDate { get; set; }

        public CallbackFileMetadata()
        {
            Author = string.Empty;
            Title = string.Empty;
            DocumentType = string.Empty;
            ZeesterDocumentType = string.Empty;
            ZeesterReference = string.Empty;
            RetentionPeriod = 0;
            PublicationDate = DateTime.MinValue;
            ArchiveDate = DateTime.MinValue;
        }

        public CallbackFileMetadata(FileMetadata metadata)
        {
            Author = metadata.Author;
            Title = metadata.Title;
            DocumentType = metadata.DocumentType;
            ZeesterDocumentType = metadata.ZeesterDocumentType;
            ZeesterReference = metadata.ZeesterReference;
            RetentionPeriod = metadata.RetentionPeriod;
            Classification = metadata.Classification;
            PublicationDate = metadata.PublicationDate;
            ArchiveDate = metadata.ArchiveDate;
        }
    }
}
