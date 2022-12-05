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
                if (property != null)
                    property.SetValue(this, value, null);
                else
                    throw new ArgumentException("Unknown property " + fieldname);
            }
        }
    }
}
