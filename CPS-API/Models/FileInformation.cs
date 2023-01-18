using System.Text.Json.Serialization;

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

        public List<ExternalReferences> ExternalReferences { get; set; }

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

                if (property.PropertyType == typeof(DateTime?))
                {
                    var stringValue = value?.ToString();
                    if (stringValue == null)
                    {
                        property.SetValue(this, null, null);
                    }
                    else
                    {
                        DateTime.TryParse(stringValue, out var dateValue);
                        property.SetValue(this, dateValue, null);
                    }
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
