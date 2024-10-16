using System.Globalization;
using System.Reflection;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Constants = CPS_API.Models.Constants;

namespace CPS_API.Helpers
{
    public static class MetadataHelper
    {
        public static PropertyInfo? GetMetadataPropertyInfo(FieldMapping fieldMapping, FileInformation? metadata = null)
        {
            if (metadata == null)
            {
                metadata = new FileInformation();
                metadata.Ids = new ObjectIdentifiers();
                metadata.AdditionalMetadata = new FileMetadata();
            }
            if (fieldMapping.FieldName.Equals(nameof(ObjectIdentifiers.ObjectId), StringComparison.InvariantCultureIgnoreCase))
            {
                return metadata.Ids?.GetType().GetProperty(fieldMapping.FieldName);
            }
            else if (FieldIsMainMetadata(fieldMapping.FieldName))
            {
                return metadata.GetType().GetProperty(fieldMapping.FieldName);
            }
            else if (metadata.AdditionalMetadata != null)
            {
                return metadata.AdditionalMetadata.GetType().GetProperty(fieldMapping.FieldName);
            }

            return null;
        }

        public static object? GetMetadataValue(FileInformation metadata, FieldMapping fieldMapping)
        {
            if (metadata == null) return null;
            if (fieldMapping.FieldName.Equals(nameof(ObjectIdentifiers.ObjectId), StringComparison.InvariantCultureIgnoreCase))
            {
                return metadata.Ids?.ObjectId;
            }
            else if (FieldIsMainMetadata(fieldMapping.FieldName))
            {
                return metadata[fieldMapping.FieldName];
            }

            if (metadata.AdditionalMetadata == null) return null;
            return metadata.AdditionalMetadata[fieldMapping.FieldName];
        }

        public static object? GetMetadataValue(ListItem? listItem, FieldMapping fieldMapping)
        {
            if (listItem == null) return null;

            listItem.Fields.AdditionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
            if (value == null)
            {
                // log warning to insights?
            }

            // Term to string
            if (value != null && !string.IsNullOrEmpty(fieldMapping.TermsetName))
            {
                var untypedObject = value as UntypedObject;
                if (untypedObject == null) return null;
                foreach (var (name, node) in untypedObject.GetValue())
                {
                    if (name != "Label") continue;
                    var untypedString = node as UntypedString;
                    if (untypedString == null) return null;
                    return untypedString.GetValue();
                }
            }

            return value;
        }

        public static bool SkipFieldForXml(PropertyInfo propertyInfo)
        {
            var fieldNamesToSkip = GetFieldNamesToSkipForXml();
            if (fieldNamesToSkip.Contains(propertyInfo.Name)) return true;

            if (propertyInfo.PropertyType == typeof(ObjectIdentifiers)) return true;

            if (propertyInfo.PropertyType == typeof(List<ExternalReferences>)) return true;

            return false;
        }

        public static List<string> GetFieldNamesToSkipForXml()
        {
            return new List<string>
            {
                Constants.ItemPropertyInfoName,
                nameof(FileInformation.CreatedBy),
                nameof(FileInformation.ModifiedBy),
                nameof(FileInformation.SourceCreatedBy),
                nameof(FileInformation.SourceModifiedBy)
            };
        }

        public static bool FieldIsMainMetadata(string name)
        {
            var mainMetadataFieldNames = GetMainMetadataFieldNames();
            return mainMetadataFieldNames.Contains(name);
        }

        public static List<string> GetMainMetadataFieldNames()
        {
            return new List<string>
            {
                nameof(FileInformation.SourceCreatedOn),
                nameof(FileInformation.SourceCreatedBy),
                nameof(FileInformation.SourceModifiedOn),
                nameof(FileInformation.SourceModifiedBy),
                nameof(FileInformation.MimeType),
                nameof(FileInformation.FileExtension),
                nameof(FileInformation.CreatedBy),
                nameof(FileInformation.CreatedOn),
                nameof(FileInformation.ModifiedBy),
                nameof(FileInformation.ModifiedOn)
            };
        }

        public static object? GetMetadataDefaultValue(object? value, PropertyInfo propertyInfo, FieldMapping fieldMapping, bool isForNewFile, bool ignoreRequiredFields)
        {
            var fieldIsEmpty = IsMetadataFieldEmpty(value, propertyInfo);
            if (!fieldMapping.Required || !fieldIsEmpty) return null;

            if (isForNewFile && fieldMapping.DefaultValue != null && !string.IsNullOrEmpty(fieldMapping.DefaultValue.ToString()))
            {
                var defaultValue = fieldMapping.DefaultValue.ToString();
                if (defaultValue != null && defaultValue.Equals(Constants.DateTimeOffsetNow, StringComparison.InvariantCultureIgnoreCase))
                {
                    return DateTimeOffset.Now;
                }
                return fieldMapping.DefaultValue;
            }
            else if (!ignoreRequiredFields)
            {
                throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
            }
            return null;
        }

        public static bool IsMetadataFieldEmpty(object? value, PropertyInfo propertyInfo)
        {
            if (value == null || propertyInfo == null)
            {
                return true;
            }
            else if (propertyInfo.PropertyType == typeof(DateTimeOffset?))
            {
                var stringValue = value.ToString();
                if (!DateTimeOffset.TryParse(stringValue, new CultureInfo("nl-NL"), out var dateTimeValue))
                {
                    return true;
                }
                // Nullable DateTime is set tot MinValue in metadata.
                if (dateTimeValue.Equals(DateTimeOffset.MinValue))
                {
                    return true;
                }
            }
            else if (propertyInfo.PropertyType == typeof(int?))
            {
                var stringValue = value.ToString();
                var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
                if (decimalValue == 0)
                {
                    return true;
                }
            }
            else if (propertyInfo.PropertyType == typeof(string))
            {
                var stringValue = value.ToString();
                return (stringValue == string.Empty);
            }
            return false;
        }

        public static bool IsEditFieldAllowed(FieldMapping fieldMapping, bool isForNewFile, bool isForTermEdit = false)
        {
            // Edit is not allowed when the field is read only.
            if (fieldMapping.ReadOnly)
            {
                return false;
            }
            // When updating fields, update must be allowed.
            if (!fieldMapping.AllowUpdate && !isForNewFile)
            {
                return false;
            }
            // When editing terms, only edit term fields.
            // Terms are edited separately.
            if (isForTermEdit == string.IsNullOrEmpty(fieldMapping.TermsetName))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Keep the existing value, if new value is empty.
        /// </summary>
        public static bool KeepExistingValue(object? value, PropertyInfo propertyInfo, bool isForNewFile, bool ignoreRequiredFields)
        {
            // Skip for a new file unless we need to ignore the required fields.
            if (isForNewFile && !ignoreRequiredFields)
            {
                return false;
            }
            // New value is not empty.
            if (!MetadataHelper.IsMetadataFieldEmpty(value, propertyInfo))
            {
                return false;
            }
            return true;
        }

        public static string? GetUserName(IdentitySet identitySet)
        {
            var user = identitySet.User;
            if (user == null)
            {
                var application = identitySet.Application;
                if (application == null)
                {
                    return null;
                }
                return application.DisplayName;
            }
            return user.DisplayName;
        }

        public static T clone<T>(object metadata) where T : new()
        {
            var clone = new T();
            foreach (var propertyInfo in metadata.GetType().GetProperties())
            {
                if (propertyInfo.Name.Equals(Constants.ItemPropertyInfoName, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                propertyInfo.SetValue(clone, propertyInfo.GetValue(metadata));
            }
            return clone;
        }

        /// <summary>
        /// Get driveid or site matching classification & source
        /// </summary>
        public static LocationMapping? GetLocationMapping(List<LocationMapping> locationMapping, FileInformation metadata)
        {
            ArgumentNullException.ThrowIfNull(nameof(metadata));
            if (metadata.AdditionalMetadata == null) throw new CpsException($"No {nameof(FileInformation.AdditionalMetadata)} found for {nameof(metadata)}");

            return locationMapping.Find(item =>
                item.Classification.Equals(metadata.AdditionalMetadata.Classification, StringComparison.OrdinalIgnoreCase)
                && item.Source.Equals(metadata.AdditionalMetadata.Source, StringComparison.OrdinalIgnoreCase)
            );
        }
    }
}