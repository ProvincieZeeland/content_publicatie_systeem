using System.Text.Json.Serialization;
using CPS_API.Helpers;

namespace CPS_API.Models
{

    public class FileInformation : CallbackFileInformation
    {
        public ObjectIdentifiers? Ids { get; set; }

        public string? CreatedBy { get; set; }

        public string? ModifiedBy { get; set; }

        public string? SourceCreatedBy { get; set; }

        public string? SourceModifiedBy { get; set; }

        public new FileMetadata? AdditionalMetadata { get; set; }

        public List<ExternalReferences>? ExternalReferences { get; set; }

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

        public FileInformation clone()
        {
            var fileInformation = new FileInformation();
            fileInformation.Ids = Ids?.clone();
            fileInformation.CreatedBy = CreatedBy;
            fileInformation.ModifiedBy = ModifiedBy;
            fileInformation.SourceCreatedBy = SourceCreatedBy;
            fileInformation.SourceModifiedBy = SourceModifiedBy;
            fileInformation.MimeType = MimeType;
            fileInformation.FileName = FileName;
            fileInformation.FileExtension = FileExtension;
            fileInformation.CreatedOn = CreatedOn;
            fileInformation.ModifiedOn = ModifiedOn;
            fileInformation.SourceCreatedOn = SourceCreatedOn;
            fileInformation.SourceModifiedOn = SourceModifiedOn;
            fileInformation.AdditionalMetadata = AdditionalMetadata?.clone();
            fileInformation.ExternalReferences = ExternalReferences?.Select(reference => reference.clone()).ToList();
            return fileInformation;
        }
    }
}
