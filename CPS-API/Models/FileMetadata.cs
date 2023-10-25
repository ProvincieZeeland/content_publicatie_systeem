using System.Text.Json.Serialization;
using CPS_API.Helpers;

namespace CPS_API.Models
{
    public class FileMetadata : CallbackFileMetadata
    {
        public string Source { get; set; } = "";

        [JsonIgnore]
        public object? this[string fieldname]
        {
            get
            {
                var property = this.GetType().GetProperty(fieldname);
                if (property != null)
                    return property.GetValue(this);
                else
                    throw new ArgumentException("Unknown property " + fieldname);
            }

            set
            {
                FieldPropertyHelper.SetFieldValue(this, fieldname, value);
            }
        }

        public FileMetadata clone()
        {
            var fileMetadata = new FileMetadata();
            fileMetadata.Source = Source;
            fileMetadata.Author = Author;
            fileMetadata.Title = Title;
            fileMetadata.DocumentType = DocumentType;
            fileMetadata.ZeesterDocumentType = ZeesterDocumentType;
            fileMetadata.ZeesterReference = ZeesterReference;
            fileMetadata.RetentionPeriod = RetentionPeriod;
            fileMetadata.Classification = Classification;
            fileMetadata.PublicationDate = PublicationDate;
            fileMetadata.ArchiveDate = ArchiveDate;
            return fileMetadata;
        }
    }
}
