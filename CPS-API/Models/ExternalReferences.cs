using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class ExternalReferences
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExternalApplication? ExternalApplication { get; set; }

        public string ExternalReference { get; set; } = string.Empty;

        public string ExternalReferenceType { get; set; } = string.Empty;

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

                if (property.PropertyType == typeof(string))
                {
                    var stringValue = value?.ToString();
                    property.SetValue(this, stringValue, null);
                }
                else if (property.PropertyType == typeof(ExternalApplication?))
                {
                    var stringValue = value?.ToString();
                    if (stringValue == null)
                    {
                        property.SetValue(this, null, null);
                    }
                    else
                    {
                        stringValue = stringValue.Replace(".", "");
                        var succeeded = Enum.TryParse<ExternalApplication>(stringValue, true, out var enumValue);
                        if (succeeded)
                        {
                            property.SetValue(this, enumValue, null);
                        }
                        else
                        {
                            property.SetValue(this, null, null);
                        }
                    }
                }
                else
                {
                    property.SetValue(this, value, null);
                }
            }
        }
    }
}
