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
            // Clone must equal current object.
            // When the metadata value is null it means that we don’t edit the property in SharePoint.
            // If de property contains a default value, we empty the property in SharePoint.
            // We set the default value null.
            var clone = new FileInformation();
            var properties = this.GetType().GetProperties();
            foreach (var propertyName in properties.Select(property => property.Name))
            {
                if (propertyName.Equals(Constants.ItemPropertyInfoName, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                clone[propertyName] = null;
            }

            foreach (var propertyInfo in properties)
            {
                if (propertyInfo.Name.Equals(Constants.ItemPropertyInfoName, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                var value = propertyInfo.GetValue(this);
                if (value == null)
                {
                    continue;
                }
                if (propertyInfo.PropertyType == typeof(FileMetadata))
                {
                    clone[propertyInfo.Name] = (value as FileMetadata)?.clone();
                }
                else if (propertyInfo.PropertyType == typeof(ObjectIdentifiers))
                {
                    clone[propertyInfo.Name] = (value as ObjectIdentifiers)?.clone();
                }
                else if (propertyInfo.PropertyType == typeof(List<ExternalReferences>))
                {
                    clone[propertyInfo.Name] = (value as List<ExternalReferences>)?.Select(item => item?.clone()).ToList();
                }
                else
                {
                    clone[propertyInfo.Name] = value;
                }
            }
            return clone;
        }
    }
}