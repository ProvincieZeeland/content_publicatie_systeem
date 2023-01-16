﻿using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class ExternalReferences
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ExternalApplication ExternalApplication { get; set; }

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
                else if (property.PropertyType == typeof(ExternalApplication))
                {
                    var stringValue = value?.ToString();
                    stringValue = stringValue.Replace(".", "");
                    var externalApplication = (ExternalApplication)Enum.Parse(typeof(ExternalApplication), stringValue, true);
                    property.SetValue(this, externalApplication, null);
                }
                else
                {
                    property.SetValue(this, value, null);
                }
            }
        }
    }
}