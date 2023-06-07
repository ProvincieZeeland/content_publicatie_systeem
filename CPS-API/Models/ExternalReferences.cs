using CPS_API.Helpers;
using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class ExternalReferences
    {
        public string ExternalApplication { get; set; }

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
                FieldPropertyHelper.SetFieldValue(this, fieldname, value);
            }
        }
    }
}
