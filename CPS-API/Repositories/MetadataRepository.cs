using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.Taxonomy;
using PnP.Core.Model.SharePoint;
using PnP.Framework.Provisioning.Model;
using Swashbuckle.AspNetCore.SwaggerGen;
using FileInformation = CPS_API.Models.FileInformation;
using ListItem = Microsoft.Graph.ListItem;
using FieldTaxonomyValue = Microsoft.SharePoint.Client.Taxonomy.TaxonomyFieldValue;

namespace CPS_API.Repositories
{
    public interface IMetadataRepository
    {
        Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false);

        Task<bool> FileContainsMetadata(ObjectIdentifiers ids);

        Task UpdateAllMetadataAsync(FileInformation metadata, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateFileName(string objectId, string fileName, bool getAsUser = false);

        Task UpdateMetadataWithoutExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateAdditionalIdentifiers(FileInformation metadata);
    }

    public class MetadataRepository : IMetadataRepository
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IDriveRepository _driveRepository;
        private readonly TelemetryClient _telemetryClient;

        public MetadataRepository(
            GraphServiceClient graphClient,
            IObjectIdRepository objectIdRepository,
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IDriveRepository driveRepository,
            TelemetryClient telemetryClient)
        {
            _graphClient = graphClient;
            _objectIdRepository = objectIdRepository;
            _globalSettings = settings.Value;
            _driveRepository = driveRepository;
            _telemetryClient = telemetryClient;
        }

        public async Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false)
        {
            var ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (ids == null)
            {
                throw new CpsException("Error while getting objectIdentifiers");
            }
            var metadata = new FileInformation();
            metadata.Ids = new ObjectIdentifiers(ids);

            ListItem? listItem;
            try
            {
                listItem = await getListItem(metadata.Ids, getAsUser);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (Exception ex) when (ex is MsalUiRequiredException || ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting listItem", ex);
            }
            if (listItem == null) throw new FileNotFoundException($"LisItem (objectId = {objectId}) does not exist!");

            DriveItem? driveItem;
            try
            {
                driveItem = await getDriveItem(objectId, getAsUser);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting driveItem", ex);
            }
            if (driveItem == null) throw new FileNotFoundException($"DriveItem (objectId = {objectId}) does not exist!");

            var fileName = listItem.Name.IsNullOrEmpty() ? driveItem.Name : listItem.Name;

            metadata.FileName = fileName;
            metadata.AdditionalMetadata = new FileMetadata();

            if (listItem.CreatedDateTime.HasValue)
                metadata.CreatedOn = listItem.CreatedDateTime.Value.DateTime;

            if (listItem.CreatedBy.User != null)
                metadata.CreatedBy = listItem.CreatedBy.User.DisplayName;
            else if (listItem.CreatedBy.Application != null)
                metadata.CreatedBy = listItem.CreatedBy.Application.DisplayName;

            if (listItem.LastModifiedDateTime.HasValue)
                metadata.ModifiedOn = listItem.LastModifiedDateTime.Value.DateTime;

            if (listItem.LastModifiedBy.User != null)
                metadata.ModifiedBy = listItem.LastModifiedBy.User.DisplayName;
            else if (listItem.LastModifiedBy.Application != null)
                metadata.ModifiedBy = listItem.LastModifiedBy.Application.DisplayName;

            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                // create object with sharepoint fields metadata + url to item
                listItem.Fields.AdditionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
                if (value == null)
                {
                    // log warning to insights?
                }
                if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
                {
                    continue;
                }

                // Term to string
                if (value != null && !string.IsNullOrEmpty(fieldMapping.TermsetName))
                {
                    var jsonString = value.ToString();
                    var term = JsonSerializer.Deserialize<TaxonomyItemDto>(jsonString);
                    value = term?.Label;
                }

                if (fieldMapping.FieldName == nameof(metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(metadata.SourceModifiedBy) || fieldMapping.FieldName == nameof(metadata.MimeType) || fieldMapping.FieldName == nameof(metadata.FileExtension))
                {
                    metadata[fieldMapping.FieldName] = value;
                }
                else
                {
                    metadata.AdditionalMetadata[fieldMapping.FieldName] = value;
                }
            }

            var externalReferenceListItems = await getExternalReferenceListItems(metadata.Ids, getAsUser);
            metadata.ExternalReferences = new List<ExternalReferences>();
            foreach (var externalReferenceListItem in externalReferenceListItems)
            {
                var externalReference = new ExternalReferences();
                foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
                {
                    // create object with sharepoint fields metadata + url to item
                    externalReferenceListItem.Fields.AdditionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
                    if (value == null)
                    {
                        // log warning to insights?
                    }
                    if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
                    {
                        continue;
                    }

                    // Term to string
                    if (value != null && !string.IsNullOrEmpty(fieldMapping.TermsetName))
                    {
                        var jsonString = value.ToString();
                        var term = JsonSerializer.Deserialize<TaxonomyItemDto>(jsonString);
                        value = term?.Label;
                    }

                    externalReference[fieldMapping.FieldName] = value;

                }
                metadata.ExternalReferences.Add(externalReference);
            }

            return metadata;
        }

        public async Task UpdateAllMetadataAsync(FileInformation metadata, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            metadata.Ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);

            await UpdateAdditionalIdentifiers(metadata);
            await UpdateMetadataWithoutExternalReferencesAsync(metadata, ignoreRequiredFields: ignoreRequiredFields, getAsUser: getAsUser);
            await UpdateExternalReferencesAsync(metadata, ignoreRequiredFields: ignoreRequiredFields, getAsUser: getAsUser);

            if (!string.IsNullOrEmpty(metadata.FileName)) await UpdateFileName(metadata.Ids.ObjectId, metadata.FileName);
        }

        public async Task UpdateMetadataWithoutExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // map received metadata to SPO object
            var fields = mapMetadata(metadata, isForNewFile, ignoreRequiredFields);
            if (fields == null) throw new CpsException("Failed to map fields for metadata");

            // update sharepoint fields with metadata
            var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
            var request = _graphClient.Sites[ids.SiteId].Lists[ids.ListId].Items[ids.ListItemId].Fields.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            await request.UpdateAsync(fields);

            // update terms
            await updateTermsForMetadataAsync(metadata, isForNewFile, ignoreRequiredFields, getAsUser);
        }

        public async Task UpdateExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // Keep the existing value, if value equals null.
            if (metadata.ExternalReferences == null)
            {
                return;
            }

            // map received metadata to SPO object
            var listItems = mapExternalReferences(metadata);
            if (listItems == null) throw new CpsException("Failed to map external references");

            // Get existing sharepoint fields with metadata
            var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
            var existingListItems = await getExternalReferenceListItems(metadata.Ids, getAsUser);

            // Check if we need to update the external references.
            var newAndUpdatedLisItemIds = new List<string>();
            var tempExternalReference = new ExternalReferences();
            var i = 0;
            foreach (var listItem in listItems)
            {
                var externalApplicationSpoColumnName = _globalSettings.ExternalReferencesMapping.FirstOrDefault(mapping => mapping.FieldName == nameof(tempExternalReference.ExternalApplication))?.SpoColumnName;
                var existingListItem = existingListItems.FirstOrDefault(item =>
                    item.Fields.AdditionalData.TryGetValue(externalApplicationSpoColumnName, out var value)
                    && metadata.ExternalReferences[i].ExternalApplication == JsonSerializer.Deserialize<TaxonomyItemDto>(value.ToString())?.Label
                );
                if (existingListItem == null)
                {
                    // Add new external reference
                    try
                    {
                        var request = _graphClient.Sites[ids.SiteId].Lists[ids.ExternalReferenceListId].Items.Request();
                        if (!getAsUser)
                        {
                            request = request.WithAppOnly();
                        }
                        var newListItem = await request.AddAsync(listItem);
                        newAndUpdatedLisItemIds.Add(newListItem.Id);
                    }
                    catch (Exception)
                    {
                        _telemetryClient.TrackEvent($"Error while adding externalReference (Fields = {JsonSerializer.Serialize(listItem.Fields)})");
                        throw;
                    }
                }
                else
                {
                    // Update existing external reference
                    try
                    {
                        var request = _graphClient.Sites[ids.SiteId].Lists[ids.ExternalReferenceListId].Items[existingListItem.Id].Fields.Request();
                        if (!getAsUser)
                        {
                            request = request.WithAppOnly();
                        }
                        await request.UpdateAsync(listItem.Fields);
                        newAndUpdatedLisItemIds.Add(existingListItem.Id);
                    }
                    catch (Exception)
                    {
                        _telemetryClient.TrackEvent($"Error while updating externalReference (Id = {existingListItem.Id}, Fields = {JsonSerializer.Serialize(listItem.Fields)})");
                        throw;
                    }
                }
                i++;
            }

            // update terms
            await updateTermsForExternalReferencesAsync(metadata, newAndUpdatedLisItemIds, isForNewFile, ignoreRequiredFields, getAsUser);
        }

        public async Task UpdateFileName(string objectId, string fileName, bool getAsUser = false)
        {
            if (objectId == null) throw new ArgumentNullException("objectId");
            if (fileName == null) throw new ArgumentNullException("fileName");

            // Get SharePoint ID's
            var ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (ids == null) throw new CpsException("Error while getting sharePointIds");

            // Update fileName
            var driveItem = new DriveItem
            {
                Name = fileName
            };
            var request = _graphClient.Drives[ids.DriveId].Items[ids.DriveItemId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            await request.UpdateAsync(driveItem);

            // Update mimetype
            new FileExtensionContentTypeProvider().TryGetContentType(fileName, out var mimeType);

            var tempMetadata = new FileInformation();
            tempMetadata.AdditionalMetadata = new FileMetadata();
            var fields = new FieldValueSet();
            fields.AdditionalData = new Dictionary<string, object>();
            if (mimeType != null)
            {
                var mapping = _globalSettings.MetadataMapping.FirstOrDefault(mapping => mapping.FieldName == nameof(tempMetadata.MimeType));
                fields.AdditionalData[mapping.SpoColumnName] = mimeType;
            }

            // Update fileExtension
            var fileExtension = Path.GetExtension(fileName).Replace(".", "");

            var fieldMapping = _globalSettings.MetadataMapping.FirstOrDefault(mapping => mapping.FieldName == nameof(tempMetadata.FileExtension));
            fields.AdditionalData[fieldMapping.SpoColumnName] = fileExtension;

            // Update title
            var title = Path.GetFileNameWithoutExtension(fileName);

            fieldMapping = _globalSettings.MetadataMapping.FirstOrDefault(mapping => mapping.FieldName == nameof(tempMetadata.AdditionalMetadata.Title));
            fields.AdditionalData[fieldMapping.SpoColumnName] = title;

            // update sharepoint fields with metadata
            var request2 = _graphClient.Sites[ids.SiteId].Lists[ids.ListId].Items[ids.ListItemId].Fields.Request();
            if (!getAsUser)
            {
                request2 = request2.WithAppOnly();
            }
            await request2.UpdateAsync(fields);
        }

        public async Task UpdateAdditionalIdentifiers(FileInformation metadata)
        {
            try
            {
                var additionalObjectIds = mapAdditionalIds(metadata);
                if (!string.IsNullOrEmpty(additionalObjectIds))
                {
                    await _objectIdRepository.SaveAdditionalIdentifiersAsync(metadata.Ids.ObjectId, additionalObjectIds);
                }
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while updating additional IDs", ex);
            }
        }

        public async Task<bool> FileContainsMetadata(ObjectIdentifiers ids)
        {
            var listItem = await getListItem(ids);
            if (listItem == null) throw new CpsException($"Error while getting listItem (SiteId = \"{ids.SiteId}\", ListId = \"{ids.ListId}\", ListItemId = \"{ids.ListItemId}\")");

            // When metadata is unknown, we skip the synchronisation.
            // The file is a new incomplete file or something went wrong while adding the file.
            var additionalData = listItem.Fields.AdditionalData;
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                var succeeded = additionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
                if (!succeeded)
                {
                    continue;
                }
                if (value != null)
                {
                    var defaultValue = fieldMapping.DefaultValue;
                    if (defaultValue == null)
                    {
                        return true;
                    }

                    if (fieldMapping.FieldName == nameof(ids.ObjectId))
                    {
                        continue;
                    }

                    var tempMetadata = new FileMetadata();
                    var propertyInfo = tempMetadata.GetType().GetProperty(fieldMapping.FieldName);
                    if (propertyInfo == null)
                    {
                        var tempFileInfo = new FileInformation();
                        propertyInfo = tempFileInfo.GetType().GetProperty(fieldMapping.FieldName);
                    }
                    if (propertyInfo == null) throw new CpsException("Error while getting type of metadata");

                    var stringValue = value.ToString();
                    var stringDefaultValue = defaultValue.ToString();
                    if (propertyInfo.PropertyType == typeof(int))
                    {
                        var decimalValue = Convert.ToDecimal(stringValue, new CultureInfo("en-US"));
                        var decimalDefaultValue = Convert.ToDecimal(stringDefaultValue, new CultureInfo("en-US"));
                        if (decimalValue != decimalDefaultValue)
                        {
                            return true;
                        }
                    }
                    else if (propertyInfo.PropertyType == typeof(DateTime))
                    {
                        var dateParsed = DateTime.TryParse(stringValue, out var dateTimeValue);
                        DateTime? nullableDateValue = null;
                        if (dateParsed)
                        {
                            nullableDateValue = dateTimeValue;
                        }
                        dateParsed = DateTime.TryParse(stringDefaultValue, out var dateTimeDefaultValue);
                        DateTime? nullableDateDefaultValue = null;
                        if (dateParsed)
                        {
                            nullableDateDefaultValue = dateTimeDefaultValue;
                        }
                        if (nullableDateValue != nullableDateDefaultValue)
                        {
                            return true;
                        }
                    }
                    else if (propertyInfo.PropertyType == typeof(string))
                    {
                        if (stringValue != stringDefaultValue)
                        {
                            return true;
                        }
                    }
                    else if (value != defaultValue)
                    {
                        return true;
                    }
                }
            }
            return false;
        }


        #region Map data

        private FieldValueSet mapMetadata(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");

            var fields = new FieldValueSet();
            fields.AdditionalData = new Dictionary<string, object>();
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                try
                {
                    // Only allow updatable fields & Term is saved separately.
                    if (fieldMapping.ReadOnly || (!fieldMapping.AllowUpdate && !isForNewFile) || !string.IsNullOrEmpty(fieldMapping.TermsetName))
                    {
                        continue;
                    }


                    var value = getMetadataValue(metadata, fieldMapping);
                    var propertyInfo = getMetadataPropertyInfo(metadata, fieldMapping);

                    // Keep the existing value, if value equals null/min value.
                    if ((!isForNewFile || ignoreRequiredFields) && isMetadataFieldEmpty(value, propertyInfo))
                    {
                        continue;
                    }

                    // Get default value
                    var defaultValue = getMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile, ignoreRequiredFields);
                    if (defaultValue != null)
                    {
                        value = defaultValue;
                    }

                    if (propertyInfo.PropertyType == typeof(DateTime?))
                    {
                        var stringValue = value?.ToString();
                        var dateParsed = DateTime.TryParse(stringValue, out var dateValue);
                        if (!dateParsed)
                        {
                            if (!ignoreRequiredFields)
                            {
                                throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
                            }
                        }
                        else if (dateValue == DateTime.MinValue)
                        {
                            fields.AdditionalData[fieldMapping.SpoColumnName] = null;
                        }
                        else
                        {
                            fields.AdditionalData[fieldMapping.SpoColumnName] = dateValue.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
                        }
                    }
                    else
                    {
                        fields.AdditionalData[fieldMapping.SpoColumnName] = value;
                    }
                }
                catch (FieldRequiredException)
                {
                    throw;
                }
                catch
                {
                    throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                }
            }
            return fields;
        }

        private List<ListItem> mapExternalReferences(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            if (metadata.ExternalReferences == null) throw new ArgumentNullException("metadata.ExternalReferences");

            var listItems = new List<ListItem>();
            foreach (var externalReference in metadata.ExternalReferences)
            {
                var fields = new FieldValueSet();
                fields.AdditionalData = new Dictionary<string, object>();
                foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
                {
                    try
                    {
                        // Term is saved separately.
                        if (fieldMapping.ReadOnly || (!fieldMapping.AllowUpdate && !isForNewFile) || !string.IsNullOrEmpty(fieldMapping.TermsetName)) continue;

                        object? value;
                        PropertyInfo propertyInfo;
                        if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
                        {
                            value = metadata.Ids.ObjectId;
                            propertyInfo = metadata.Ids.GetType().GetProperty(fieldMapping.FieldName);
                        }
                        else
                        {
                            value = externalReference[fieldMapping.FieldName];
                            propertyInfo = externalReference.GetType().GetProperty(fieldMapping.FieldName);
                        }

                        // Keep the existing value, if value equals null.
                        var fieldIsEmpty = isMetadataFieldEmpty(value, propertyInfo);
                        if ((!isForNewFile || ignoreRequiredFields) && fieldIsEmpty)
                        {
                            continue;
                        }

                        if (fieldMapping.Required && fieldIsEmpty)
                        {
                            if (isForNewFile && fieldMapping.DefaultValue != null && !fieldMapping.DefaultValue.ToString().IsNullOrEmpty())
                            {
                                value = fieldMapping.DefaultValue;
                            }
                            else if (!ignoreRequiredFields)
                            {
                                throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
                            }
                        }

                        if (value != null) fields.AdditionalData[fieldMapping.SpoColumnName] = value;
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName, ex);
                    }
                }
                listItems.Add(new ListItem { Fields = fields });
            }
            return listItems;
        }

        private string mapAdditionalIds(FileInformation metadata)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");

            try
            {
                if (!string.IsNullOrEmpty(_globalSettings.AdditionalObjectId) &&
                    metadata.AdditionalMetadata[_globalSettings.AdditionalObjectId] != null)
                {
                    string id = metadata.AdditionalMetadata[_globalSettings.AdditionalObjectId].ToString();
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Cannot parse additional ids", _globalSettings.AdditionalObjectId, ex);
            }

            return string.Empty;
        }

        #endregion

        #region Update functions

        private async Task updateTermsForMetadataAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");

            var site = await _driveRepository.GetSiteAsync(metadata.Ids.SiteId, getAsUser);

            // Graph does not support full Term management yet, using PnP for SPO API instead
            using (var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, StoreName.My, StoreLocation.CurrentUser, _globalSettings.CertificateThumbprint, _globalSettings.TenantId))
            {
                using (ClientContext context = await authenticationManager.GetContextAsync(site.WebUrl))
                {
                    var termStore = getAllTerms(context);
                    if (termStore == null) throw new CpsException("Term store not found!");

                    Dictionary<string, FieldTaxonomyValue> newValues = new Dictionary<string, FieldTaxonomyValue>();
                    foreach (var fieldMapping in _globalSettings.MetadataMapping)
                    {
                        try
                        {
                            // Only try to edit the terms.
                            if (fieldMapping.ReadOnly || (!fieldMapping.AllowUpdate && !isForNewFile) || string.IsNullOrEmpty(fieldMapping.TermsetName)) continue;

                            var propertyInfo = getMetadataPropertyInfo(metadata, fieldMapping);
                            var value = getMetadataValue(metadata, fieldMapping);

                            // Keep the existing value, if value equals null.
                            if ((!isForNewFile || ignoreRequiredFields) && isMetadataFieldEmpty(value, propertyInfo))
                            {
                                continue;
                            }

                            var defaultValue = getMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile, ignoreRequiredFields);
                            if (defaultValue != null)
                            {
                                value = defaultValue;
                            }

                            if (value != null)
                            {
                                var mappedTermSet = termStore.TermSets.Where(s => s.Name == fieldMapping.TermsetName).FirstOrDefault();
                                if (mappedTermSet != null)
                                {
                                    var mappedTerm = mappedTermSet.Terms.Where(t => t.Name.Equals(value.ToString(), StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                                    if (mappedTerm != null)
                                    {
                                        var termValue = new FieldTaxonomyValue
                                        {
                                            TermGuid = mappedTerm.Id.ToString(),
                                            Label = mappedTerm.Name,
                                            WssId = -1
                                        };
                                        newValues.Add(fieldMapping.SpoColumnName, termValue);
                                    }
                                    else
                                    {
                                        throw new CpsException("Term not found by value " + value.ToString());
                                    }
                                }
                                else
                                {
                                    throw new CpsException("Termset not found by name " + fieldMapping.TermsetName);
                                }
                            }
                        }
                        catch (FieldRequiredException)
                        {
                            throw;
                        }
                        catch
                        {
                            throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                        }
                    }

                    // actually update fields
                    updateTermFields(metadata.Ids.ListId, metadata.Ids.ListItemId, context, newValues);
                }
            }
        }

        private async Task updateTermsForExternalReferencesAsync(FileInformation metadata, List<string> listItemIds, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata.ExternalReferences == null) throw new ArgumentNullException("metadata.ExternalReferences");

            var site = await _driveRepository.GetSiteAsync(metadata.Ids.SiteId, getAsUser);

            // Graph does not support full Term management yet, using PnP for SPO API instead
            using (var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, StoreName.My, StoreLocation.CurrentUser, _globalSettings.CertificateThumbprint, _globalSettings.TenantId))
            {
                using (ClientContext context = await authenticationManager.GetContextAsync(site.WebUrl))
                {
                    var termStore = getAllTerms(context);
                    if (termStore == null) throw new CpsException("Term store not found!");

                    var i = 0;
                    foreach (var externalReference in metadata.ExternalReferences)
                    {
                        Dictionary<string, FieldTaxonomyValue> newValues = new Dictionary<string, FieldTaxonomyValue>();
                        foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
                        {
                            try
                            {
                                // Only try to edit the terms.
                                if (fieldMapping.ReadOnly || (!fieldMapping.AllowUpdate && !isForNewFile) || string.IsNullOrEmpty(fieldMapping.TermsetName)) continue;

                                object? value;
                                PropertyInfo propertyInfo;
                                if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
                                {
                                    value = metadata.Ids.ObjectId;
                                    propertyInfo = metadata.Ids.GetType().GetProperty(fieldMapping.FieldName);
                                }
                                else
                                {
                                    value = externalReference[fieldMapping.FieldName];
                                    propertyInfo = externalReference.GetType().GetProperty(fieldMapping.FieldName);
                                }

                                // Keep the existing value, if value equals null.
                                var fieldIsEmpty = isMetadataFieldEmpty(value, propertyInfo);
                                if ((!isForNewFile || ignoreRequiredFields) && fieldIsEmpty)
                                {
                                    continue;
                                }

                                if (fieldMapping.Required && fieldIsEmpty)
                                {
                                    if (isForNewFile && fieldMapping.DefaultValue != null && !fieldMapping.DefaultValue.ToString().IsNullOrEmpty())
                                    {
                                        value = fieldMapping.DefaultValue;
                                    }
                                    else if (!ignoreRequiredFields)
                                    {
                                        throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
                                    }
                                }

                                if (value != null)
                                {
                                    var mappedTermSet = termStore.TermSets.Where(s => s.Name == fieldMapping.TermsetName).FirstOrDefault();
                                    if (mappedTermSet != null)
                                    {
                                        var mappedTerm = mappedTermSet.Terms.Where(t => t.Name.Equals(value.ToString(), StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
                                        if (mappedTerm != null)
                                        {
                                            var termValue = new FieldTaxonomyValue
                                            {
                                                TermGuid = mappedTerm.Id.ToString(),
                                                Label = mappedTerm.Name,
                                                WssId = -1
                                            };
                                            newValues.Add(fieldMapping.SpoColumnName, termValue);
                                        }
                                        else
                                        {
                                            throw new CpsException("Term not found by value " + value.ToString());
                                        }
                                    }
                                    else
                                    {
                                        throw new CpsException("Termset not found by name " + fieldMapping.TermsetName);
                                    }
                                }
                            }
                            catch
                            {
                                throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                            }
                        }

                        // actually update fields
                        updateTermFields(metadata.Ids.ExternalReferenceListId, listItemIds[i], context, newValues);
                        i++;
                    }
                }
            }
        }

        private static void updateTermFields(string listId, string listItemId, ClientContext context, Dictionary<string, FieldTaxonomyValue> newValues)
        {
            var list = context.Web.Lists.GetById(Guid.Parse(listId));
            var listItem = list.GetItemById(listItemId);

            var fields = listItem.ParentList.Fields;
            context.Load(fields);
            context.ExecuteQuery();

            foreach (var newTerm in newValues)
            {
                var field = context.CastTo<TaxonomyField>(fields.GetByInternalNameOrTitle(newTerm.Key));
                context.Load(field);
                field.SetFieldValueByValue(listItem, newTerm.Value);
            }
            listItem.Update();
            context.ExecuteQuery();
        }

        #endregion

        #region Get data / Fields

        private object? getMetadataValue(FileInformation metadata, FieldMapping fieldMapping)
        {
            if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
            {
                return metadata.Ids.ObjectId;
            }
            else if (fieldMapping.FieldName == nameof(metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(metadata.SourceModifiedBy) || fieldMapping.FieldName == nameof(metadata.MimeType) || fieldMapping.FieldName == nameof(metadata.FileExtension))
            {
                return metadata[fieldMapping.FieldName];
            }
            else
            {
                return metadata.AdditionalMetadata[fieldMapping.FieldName];
            }
        }

        private PropertyInfo getMetadataPropertyInfo(FileInformation metadata, FieldMapping fieldMapping)
        {
            if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
            {
                return metadata.Ids.GetType().GetProperty(fieldMapping.FieldName);
            }
            else if (fieldMapping.FieldName == nameof(metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(metadata.SourceModifiedBy) || fieldMapping.FieldName == nameof(metadata.MimeType) || fieldMapping.FieldName == nameof(metadata.FileExtension))
            {
                return metadata.GetType().GetProperty(fieldMapping.FieldName);
            }
            else
            {
                return metadata.AdditionalMetadata.GetType().GetProperty(fieldMapping.FieldName);
            }
        }

        private object? getMetadataDefaultValue(object? value, PropertyInfo propertyInfo, FieldMapping fieldMapping, bool isForNewFile, bool ignoreRequiredFields)
        {
            var fieldIsEmpty = isMetadataFieldEmpty(value, propertyInfo);
            if (fieldMapping.Required && fieldIsEmpty)
            {
                if (isForNewFile && fieldMapping.DefaultValue != null && !fieldMapping.DefaultValue.ToString().IsNullOrEmpty())
                {
                    if (fieldMapping.DefaultValue.ToString() == "DateTime.Now")
                    {
                        return DateTime.Now;
                    }
                    else
                    {
                        return fieldMapping.DefaultValue;
                    }
                }
                else if (!ignoreRequiredFields)
                {
                    throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
                }
            }
            return null;
        }

        private bool isMetadataFieldEmpty(object? value, PropertyInfo propertyInfo)
        {
            if (value == null)
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


        private async Task<ListItem?> getListItem(ObjectIdentifiers ids, bool getAsUser = false)
        {
            // Find file in SharePoint using ids
            var queryOptions = new List<QueryOption>()
            {
                new QueryOption("expand", "fields")
            };

            var request = _graphClient.Sites[ids.SiteId].Lists[ids.ListId].Items[ids.ListItemId].Request(queryOptions);
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        
        private Microsoft.SharePoint.Client.Taxonomy.TermGroup getAllTerms(ClientContext context)
        {
            var taxonomySession = TaxonomySession.GetTaxonomySession(context);
            var termStore = taxonomySession.GetDefaultSiteCollectionTermStore();
            string name = _globalSettings.TermStoreName;
            context.Load(termStore,
                            store => store.Name,
                            store => store.Groups.Where(g => g.Name == name && g.IsSystemGroup == false && g.IsSiteCollectionGroup == false)
                                .Include(
                                group => group.Id,
                                group => group.Name,
                                group => group.Description,
                                group => group.TermSets.Include(
                                    termSet => termSet.Id,
                                    termSet => termSet.Name,
                                    termSet => termSet.Description,
                                    termSet => termSet.CustomProperties,
                                    termSet => termSet.Terms.Include(
                                        t => t.Id,
                                        t => t.Description,
                                        t => t.Name,
                                        t => t.IsDeprecated,
                                        t => t.Parent,
                                        t => t.Labels,
                                        t => t.LocalCustomProperties,
                                        t => t.IsSourceTerm,
                                        t => t.IsRoot,
                                        t => t.IsKeyword))));
            context.ExecuteQuery();

            return termStore.Groups.FirstOrDefault();
        }

        private async Task<List<ListItem>> getExternalReferenceListItems(ObjectIdentifiers ids, bool getAsUser = false)
        {
            var request = _graphClient.Sites[ids.SiteId].Lists[ids.ExternalReferenceListId].Items.Request().Expand("Fields").Filter($"Fields/ObjectID eq '{ids.ObjectId}'");
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            var listItemsPage = await request.GetAsync();
            return listItemsPage?.CurrentPage?.ToList();
        }

        private async Task<DriveItem?> getDriveItem(string objectId, bool getAsUser = false)
        {
            // Find file info in documents table by objectId
            var ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (ids == null) throw new FileNotFoundException($"ObjectIdentifiers (objectId = {objectId}) does not exist!");

            return await _driveRepository.GetDriveItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, getAsUser);
        }

        #endregion
    }
}
