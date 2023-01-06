using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class ExternalReferences
    {
        public string ExternalApplication { get; set; } = string.Empty;

        public string ExternalReference { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExternalReferenceType ExternalReferenceType { get; set; }

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
                else if (property.PropertyType == typeof(ExternalReferenceType))
                {
                    var stringValue = value?.ToString();
                    var externalReferenceType = (ExternalReferenceType)Enum.Parse(typeof(ExternalReferenceType), stringValue, true);
                    property.SetValue(this, externalReferenceType, null);
                }
                else
                {
                    property.SetValue(this, value, null);
                }
            }
        }
    }
}
