using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;

namespace CPS_API.Helpers
{
    public static class MetadataHelper
    {
        public static PropertyInfo? GetMetadataPropertyInfo(FileInformation metadata, FieldMapping fieldMapping)
        {
            if (fieldMapping.FieldName == nameof(ObjectIdentifiers.ObjectId))
            {
                return metadata?.Ids?.GetType().GetProperty(fieldMapping.FieldName);
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
            if (fieldMapping.FieldName == nameof(ObjectIdentifiers.ObjectId))
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
                var jsonString = value.ToString();
                if (jsonString == null) return null;
                var term = JsonSerializer.Deserialize<TaxonomyItemDto>(jsonString);
                value = term?.Label;
            }

            return value;
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

            if (isForNewFile && fieldMapping.DefaultValue != null && !fieldMapping.DefaultValue.ToString().IsNullOrEmpty())
            {
                if (fieldMapping.DefaultValue.ToString() == "DateTime.Now")
                {
                    return DateTime.Now;
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
            else if (propertyInfo.PropertyType == typeof(DateTime?))
            {
                var stringValue = value.ToString();
                DateTime.TryParse(stringValue, out var dateValue);
                if (dateValue == DateTime.MinValue)
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
            if (isForTermEdit == fieldMapping.TermsetName.IsNullOrEmpty())
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

        public static DateTime? GetDateTime(DateTimeOffset? dateTimeOffset)
        {
            if (!dateTimeOffset.HasValue)
            {
                return null;
            }
            return dateTimeOffset.Value.DateTime.ToLocalTime();
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
    }
}
