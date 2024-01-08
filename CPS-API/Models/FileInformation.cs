﻿using System.Text.Json.Serialization;
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
            var clone = new FileInformation();
            var properties = this.GetType().GetProperties();
            foreach (var propertyName in properties.Select(property => property.Name))
            {
                if (propertyName == "Item")
                {
                    continue;
                }
                clone[propertyName] = null;
            }

            foreach (var propertyInfo in properties)
            {
                if (propertyInfo.Name == "Item")
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