using System.Globalization;
using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class FileMetadata
    {
        public string Author { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string DocumentType { get; set; } = string.Empty;

        public string ZeesterDocumentType { get; set; } = string.Empty;

        public string ZeesterReference { get; set; } = string.Empty;

        public int RetentionPeriod { get; set; } = 0;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Classification Classification { get; set; }

        public DateTime PublicationDate { get; set; } = DateTime.MinValue;

        public DateTime ArchiveDate { get; set; } = DateTime.MinValue;

        public string Source { get; set; } = string.Empty;

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

                if (property.PropertyType == typeof(Classification))
                {
                    var stringValue = value?.ToString();
                    if (stringValue != null)
                    {
                        Enum.TryParse<Classification>(stringValue, out var enumValue);
                        property.SetValue(this, enumValue, null);
                    }
                }
                else if (property.PropertyType == typeof(int))
                {
                    var stringValue = value?.ToString();
                    var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
                    if (decimalValue % 1 == 0)
                    {
                        property.SetValue(this, (int)decimalValue, null);
                    }
                }
                else if (property.PropertyType == typeof(DateTime))
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
