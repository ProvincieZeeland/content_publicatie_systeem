using System.Text.Json.Serialization;

namespace CPS_API.Models
{

    public class FileInformation
    {
        public ObjectIdentifiers Ids { get; set; }

        public string MimeType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string FileExtension { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; }

        public string CreatedBy { get; set; } = string.Empty;

        public DateTime ModifiedOn { get; set; }

        public string ModifiedBy { get; set; } = string.Empty;

        public DateTime SourceCreatedOn { get; set; }

        public string SourceCreatedBy { get; set; } = string.Empty;

        public DateTime SourceModifiedOn { get; set; }

        public string SourceModifiedBy { get; set; } = string.Empty;

        public FileMetadata? AdditionalMetadata { get; set; }

        public List<ExternalReferences> ExternalReferences { get; set; } = new List<ExternalReferences>();

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
                var property = this.GetType().GetProperty(fieldname);
                if (property == null) throw new ArgumentException("Unknown property " + fieldname);

                if (property.PropertyType == typeof(DateTime))
                {
                    var stringValue = value?.ToString();
                    DateTime.TryParse(stringValue, out var dateValue);
                    property.SetValue(this, dateValue, null);
                }
                else if (property.PropertyType == typeof(string))
                {
                    var stringValue = value?.ToString();
                    property.SetValue(this, stringValue, null);
                }
                else
                {
                    property.SetValue(this, value, null);
                }
            }
        }
    }
}
