using System.Globalization;
using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class FileMetadata : CallbackFileMetadata
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public Source? Source { get; set; }

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

                if (property.PropertyType == typeof(Classification?))
                {
                    var stringValue = value?.ToString();
                    if (stringValue == null)
                    {
                        property.SetValue(this, null, null);
                    }
                    else
                    {
                        Enum.TryParse<Classification>(stringValue, out var enumValue);
                        property.SetValue(this, enumValue, null);
                    }
                }
                else if (property.PropertyType == typeof(Source?))
                {
                    var stringValue = value?.ToString();
                    if (stringValue == null)
                    {
                        property.SetValue(this, null, null);
                    }
                    else
                    {
                        Enum.TryParse<Source>(stringValue, out var enumValue);
                        property.SetValue(this, enumValue, null);
                    }
                }
                else if (property.PropertyType == typeof(int?))
                {
                    var stringValue = value?.ToString();
                    if (stringValue == null)
                    {
                        property.SetValue(this, null, null);
                    }
                    else
                    {
                        var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
                        if (decimalValue % 1 == 0)
                        {
                            property.SetValue(this, (int)decimalValue, null);
                        }
                    }
                }
                else if (property.PropertyType == typeof(DateTime?))
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
