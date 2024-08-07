﻿using System.Net;
using System.Reflection;
using System.Text.Json;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using PnP.Framework.Extensions;
using Constants = CPS_API.Models.Constants;
using FileInformation = CPS_API.Models.FileInformation;
using ListItem = Microsoft.Graph.ListItem;

namespace CPS_API.Repositories
{
    public interface IMetadataRepository
    {
        Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false);

        Task<FileInformation> GetMetadataWithoutExternalReferencesAsync(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false);

        DropOffFileMetadata GetDropOffMetadata(ListItem listItem);

        Task<bool> FileContainsMetadata(ObjectIdentifiers ids);

        Task UpdateAllMetadataAsync(FileInformation metadata, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateFileName(string objectId, string fileName, FileInformation? metadata = null, bool getAsUser = false);

        Task UpdateMetadataWithoutExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateDropOffMetadataAsync(bool isComplete, string status, FileInformation metadata, bool getAsUser = false);

        string MapAdditionalIds(FileInformation metadata);
    }

    public class MetadataRepository : IMetadataRepository
    {
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IDriveRepository _driveRepository;
        private readonly IListRepository _listRepository;
        private readonly TelemetryClient _telemetryClient;
        private readonly ISharePointRepository _sharePointRepository;

        public MetadataRepository(
            IObjectIdRepository objectIdRepository,
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IDriveRepository driveRepository,
            IListRepository listRepository,
            TelemetryClient telemetryClient,
            ISharePointRepository sharePointRepository)
        {
            _objectIdRepository = objectIdRepository;
            _globalSettings = settings.Value;
            _driveRepository = driveRepository;
            _listRepository = listRepository;
            _telemetryClient = telemetryClient;
            _sharePointRepository = sharePointRepository;
        }

        #region Get

        /// <summary>
        /// Get metadata including external references for document.
        /// </summary>
        public async Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false)
        {
            var objectIdentifiers = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (objectIdentifiers == null)
            {
                throw new CpsException("Error while getting objectIdentifiers");
            }
            var ids = new ObjectIdentifiers(objectIdentifiers);

            var listItem = await _listRepository.GetListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, getAsUser);

            var metadata = await GetMetadataWithoutExternalReferencesAsync(listItem, ids, getAsUser);
            metadata.ExternalReferences = await GetExternalReferencesAsync(ids, getAsUser);
            return metadata;
        }

        /// <summary>
        /// Get metadata excluding external references for document.
        /// External references are stored in a different list with a different listItem.
        /// </summary>
        public async Task<FileInformation> GetMetadataWithoutExternalReferencesAsync(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false)
        {
            var metadata = new FileInformation();
            metadata.Ids = ids;
            metadata.FileName = await GetFileNameAsync(listItem, ids, getAsUser);
            metadata.CreatedOn = listItem.CreatedDateTime;
            metadata.CreatedBy = MetadataHelper.GetUserName(listItem.CreatedBy);
            metadata.ModifiedOn = listItem.LastModifiedDateTime;
            metadata.ModifiedBy = MetadataHelper.GetUserName(listItem.LastModifiedBy);

            metadata.AdditionalMetadata = new FileMetadata();
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                // ObjectId is stored in Ids
                if (fieldMapping.FieldName.Equals(nameof(metadata.Ids.ObjectId), StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }

                var value = MetadataHelper.GetMetadataValue(listItem, fieldMapping);
                if (MetadataHelper.FieldIsMainMetadata(fieldMapping.FieldName))
                {
                    metadata[fieldMapping.FieldName] = value;
                }
                else
                {
                    metadata.AdditionalMetadata[fieldMapping.FieldName] = value;
                }
            }
            return metadata;
        }

        private async Task<string> GetFileNameAsync(ListItem listItem, ObjectIdentifiers ids, bool getAsUser = false)
        {
            try
            {
                listItem.Fields.AdditionalData.TryGetValue("FileLeafRef", out var fileRef);
                var itemName = fileRef?.ToString();
                if (!string.IsNullOrEmpty(itemName)) return itemName;
            }
            catch { /* do nothing > we will use drive item name instead */ }

            var driveItem = await _driveRepository.GetDriveItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, getAsUser);
            return driveItem.Name;
        }

        /// <summary>
        /// Get external references for document.
        /// </summary>
        private async Task<List<ExternalReferences>> GetExternalReferencesAsync(ObjectIdentifiers ids, bool getAsUser = false)
        {
            var externalReferenceListItems = await GetExternalReferenceListItems(ids, getAsUser);
            var externalReferences = new List<ExternalReferences>();
            foreach (var externalReferenceListItem in externalReferenceListItems)
            {
                var externalReference = new ExternalReferences();
                foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
                {
                    if (fieldMapping.FieldName.Equals(nameof(ids.ObjectId), StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }
                    externalReference[fieldMapping.FieldName] = MetadataHelper.GetMetadataValue(externalReferenceListItem, fieldMapping);
                }
                externalReferences.Add(externalReference);
            }
            return externalReferences;
        }

        /// <summary>
        /// Get metadata for DropOff document.
        /// </summary>
        public DropOffFileMetadata GetDropOffMetadata(ListItem listItem)
        {
            var metadata = new DropOffFileMetadata();
            foreach (var fieldMapping in _globalSettings.DropOffMetadataMapping)
            {
                // Create object with sharepoint fields metadata + url to item
                listItem.Fields.AdditionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
                metadata[fieldMapping.FieldName] = value;
            }
            return metadata;
        }

        #endregion Get

        #region Update

        public async Task<ObjectIdentifiers> CopyAndDeleteFileAsync(FileInformation metadata, LocationMapping locationMapping)
        {
            ArgumentNullException.ThrowIfNull(nameof(metadata));
            ArgumentNullException.ThrowIfNull(nameof(locationMapping));
            if (metadata.Ids == null) throw new CpsException($"No {nameof(FileInformation.Ids)} found for {nameof(metadata)}");

            var oldDriveId = metadata.Ids.DriveId;
            var oldDriveItemId = metadata.Ids.DriveItemId;
            string? driveItemId;
            try
            {
                var drive = await _driveRepository.GetDriveAsync(locationMapping.SiteId, locationMapping.ListId);
                driveItemId = await _driveRepository.CopyFileAsync(metadata.Ids.DriveId, metadata.Ids.DriveItemId, drive.Id, drive.ParentReference.Path);
                if (string.IsNullOrEmpty(driveItemId)) throw new CpsException("Error while copying file");

                metadata.Ids = new ObjectIdentifiers
                {
                    SiteId = locationMapping.SiteId,
                    ListId = locationMapping.ListId,
                    ObjectId = metadata.Ids.ObjectId,
                    DriveItemId = driveItemId,
                    ExternalReferenceListId = locationMapping.ExternalReferenceListId,
                    AdditionalObjectId = metadata.Ids.AdditionalObjectId
                };
                // Get new listItemId
                metadata.Ids = await _objectIdRepository.FindMissingIds(metadata.Ids);

                await UpdateExternalReferencesAsync(metadata);
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while copying file", ex);
            }

            await _driveRepository.DeleteFileAsync(oldDriveId, oldDriveItemId);

            await _objectIdRepository.SaveObjectIdentifiersAsync(metadata.Ids.ObjectId, metadata.Ids);

            return metadata.Ids;
        }

        public async Task<ObjectIdentifiers> MoveFileAsync(FileInformation metadata, LocationMapping locationMapping)
        {
            ArgumentNullException.ThrowIfNull(nameof(metadata));
            ArgumentNullException.ThrowIfNull(nameof(locationMapping));

            var driveItemId = await _sharePointRepository.MoveFileAsync(metadata.Ids.SiteId, metadata.Ids.ListId, metadata.Ids.ListItemId, locationMapping.SiteId, locationMapping.ListId);
            var ids = new ObjectIdentifiers
            {
                ObjectId = metadata.Ids.ObjectId,
                SiteId = locationMapping.SiteId,
                ListId = locationMapping.ListId,
                DriveItemId = driveItemId,
                ExternalReferenceListId = locationMapping.ExternalReferenceListId,
                AdditionalObjectId = metadata.Ids.AdditionalObjectId
            };
            ids = await _objectIdRepository.FindMissingIds(ids);

            if (ids == null) throw new CpsException("Error while moving file");

            // Get new listItemId
            ids = await _objectIdRepository.FindMissingIds(ids);

            await _objectIdRepository.SaveObjectIdentifiersAsync(metadata.Ids.ObjectId, ids);
            return ids;
        }

        public async Task UpdateAllMetadataAsync(FileInformation metadata, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            ArgumentNullException.ThrowIfNull(nameof(metadata));
            if (metadata.Ids == null) throw new CpsException($"No {nameof(FileInformation.Ids)} found for {nameof(metadata)}");
            if (metadata.Ids.ObjectId == null) throw new CpsException($"No {nameof(ObjectIdentifiers.ObjectId)} found for {nameof(FileInformation.Ids)}");

            metadata.Ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);

            var updateMetadataData = await GetUpdateMetadataAction(metadata);
            if (updateMetadataData.Action == UpdateMetadataAction.Move)
            {
                metadata.Ids = await MoveFileAsync(metadata, updateMetadataData.NewLocation);
            }

            if (metadata.AdditionalMetadata != null)
            {
                var additionalObjectIds = MapAdditionalIds(metadata);
                if (!string.IsNullOrEmpty(additionalObjectIds)) await _objectIdRepository.SaveAdditionalIdentifiersAsync(metadata.Ids.ObjectId, additionalObjectIds);
            }

            await UpdateMetadataWithoutExternalReferencesAsync(metadata, ignoreRequiredFields: ignoreRequiredFields, getAsUser: getAsUser);

            if (metadata.ExternalReferences != null)
            {
                await UpdateExternalReferencesAsync(metadata, ignoreRequiredFields: ignoreRequiredFields, getAsUser: getAsUser);
            }

            if (!string.IsNullOrEmpty(metadata.FileName))
            {
                await UpdateFileName(metadata.Ids.ObjectId, metadata.FileName, metadata);
            }
        }

        private async Task<UpdateMetadataModel> GetUpdateMetadataAction(FileInformation metadata)
        {
            ArgumentNullException.ThrowIfNull(nameof(metadata));
            if (metadata.Ids == null) throw new CpsException($"No {nameof(FileInformation.Ids)} found for {nameof(metadata)}");
            if (metadata.Ids.ObjectId == null) throw new CpsException($"No {nameof(ObjectIdentifiers.ObjectId)} found for {nameof(FileInformation.Ids)}");
            if (metadata.AdditionalMetadata == null) throw new CpsException($"No {nameof(FileInformation.AdditionalMetadata)} found for {nameof(metadata)}");

            var currentMetadata = await GetMetadataAsync(metadata.Ids.ObjectId);
            if (currentMetadata.AdditionalMetadata == null) throw new CpsException($"No {nameof(FileInformation.AdditionalMetadata)} found for {nameof(currentMetadata)}");
            if (currentMetadata.AdditionalMetadata.Source == metadata.AdditionalMetadata.Source && currentMetadata.AdditionalMetadata.Classification == metadata.AdditionalMetadata.Classification)
            {
                return new UpdateMetadataModel(UpdateMetadataAction.Update);
            }

            var currentLocation = MetadataHelper.GetLocationMapping(_globalSettings.LocationMapping, currentMetadata);

            // No location change?
            if (string.IsNullOrWhiteSpace(metadata.AdditionalMetadata.Source) && string.IsNullOrWhiteSpace(metadata.AdditionalMetadata.Classification))
            {
                return new UpdateMetadataModel(UpdateMetadataAction.Update);
            }

            // Use current source or classification, when it does not change.
            if (string.IsNullOrWhiteSpace(metadata.AdditionalMetadata.Source))
            {
                metadata.AdditionalMetadata.Source = currentMetadata.AdditionalMetadata.Source;
            }
            if (string.IsNullOrWhiteSpace(metadata.AdditionalMetadata.Classification))
            {
                metadata.AdditionalMetadata.Classification = currentMetadata.AdditionalMetadata.Classification;
            }
            var newLocation = MetadataHelper.GetLocationMapping(_globalSettings.LocationMapping, metadata);

            // No location change, different source or location in same documentlibrary?
            if (currentLocation.SiteId == newLocation.SiteId && currentLocation.ListId == newLocation.ListId)
            {
                return new UpdateMetadataModel(UpdateMetadataAction.Update);
            }

            return new UpdateMetadataModel(UpdateMetadataAction.Move, newLocation);
        }

        public async Task UpdateMetadataWithoutExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // map received metadata to SPO object
            var fields = MapMetadata(metadata, isForNewFile, ignoreRequiredFields);
            if (fields == null) throw new CpsException("Failed to map fields for metadata");

            // update sharepoint fields with metadata
            if (fields.AdditionalData.Count > 0)
            {
                var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
                await _listRepository.UpdateListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, fields, getAsUser);
            }

            // update terms
            await _sharePointRepository.UpdateTermsForMetadataAsync(metadata, isForNewFile, ignoreRequiredFields, getAsUser);
        }

        /// <summary>
        /// Update external references for document.
        ///  - Create or update existing item in external references list
        ///     - Check existing on same application
        ///  - Update terms for created/updated item in external references list
        /// </summary>
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
            var externalReferenceItems = MapExternalReferences(metadata);
            if (externalReferenceItems == null) throw new CpsException("Failed to map external references");

            // Get existing sharepoint fields with metadata
            var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
            var existingListItems = await GetExternalReferenceListItems(metadata.Ids, getAsUser);

            // Check if we need to update the external references.
            externalReferenceItems = await AddOrUpdateListItems(externalReferenceItems, existingListItems, ids, getAsUser);

            // update terms
            await _sharePointRepository.UpdateTermsForExternalReferencesAsync(metadata.Ids.SiteId, metadata.Ids.ExternalReferenceListId, externalReferenceItems, isForNewFile, ignoreRequiredFields, getAsUser);
        }

        /// <summary>
        /// Create or update existing item in external references list
        ///  - Check existing on same application
        /// </summary>
        private async Task<List<ExternalReferenceItem>> AddOrUpdateListItems(List<ExternalReferenceItem> externalReferenceItems, List<ListItem> existingListItems, ObjectIdentifiers ids, bool getAsUser = false)
        {
            var newAndUpdatedListItemIds = new List<ExternalReferenceItem>();
            foreach (var externalReferenceItem in externalReferenceItems)
            {
                var externalReference = externalReferenceItem.ExternalReference;
                var newOrUpdatedListItem = await AddOrUpdateListItem(externalReferenceItem.ListItem, externalReference, existingListItems, ids, getAsUser);
                newAndUpdatedListItemIds.Add(new ExternalReferenceItem(newOrUpdatedListItem, externalReference));
            }
            return newAndUpdatedListItemIds;
        }

        private async Task<ListItem> AddOrUpdateListItem(ListItem listItem, ExternalReferences externalReference, List<ListItem> existingListItems, ObjectIdentifiers ids, bool getAsUser = false)
        {
            var existingListItem = GetExistingListItem(externalReference, existingListItems);
            if (existingListItem == null)
            {
                // Add new external reference
                try
                {
                    var newListItem = await _listRepository.AddListItemAsync(ids.SiteId, ids.ExternalReferenceListId, listItem, getAsUser);
                    return newListItem;
                }
                catch (Exception)
                {
                    _telemetryClient.TrackEvent($"Error while adding externalReference (Fields = {JsonSerializer.Serialize(listItem.Fields)})");
                    throw;
                }
            }

            // Update existing external reference
            try
            {
                await _listRepository.UpdateListItemAsync(ids.SiteId, ids.ExternalReferenceListId, existingListItem.Id, listItem.Fields, getAsUser);
                return existingListItem;
            }
            catch (Exception)
            {
                _telemetryClient.TrackEvent($"Error while updating externalReference (Id = {existingListItem.Id}, Fields = {JsonSerializer.Serialize(listItem.Fields)})");
                throw;
            }
        }

        private ListItem? GetExistingListItem(ExternalReferences externalReference, List<ListItem> existingListItems)
        {
            var externalApplicationSpoColumnName = _globalSettings.ExternalReferencesMapping.Find(mapping => mapping.FieldName.Equals(nameof(ExternalReferences.ExternalApplication), StringComparison.InvariantCultureIgnoreCase))?.SpoColumnName;
            return existingListItems.Find(item =>
                item.Fields.AdditionalData.TryGetValue(externalApplicationSpoColumnName, out var value)
                && value != null
                && externalReference.ExternalApplication == JsonSerializer.Deserialize<TaxonomyItemDto>(value.ToString())?.Label
            );
        }

        public async Task UpdateFileName(string objectId, string fileName, FileInformation? metadata = null, bool getAsUser = false)
        {
            if (objectId == null) throw new ArgumentNullException("objectId");
            if (fileName == null) throw new ArgumentNullException("fileName");

            // Get SharePoint ID's
            var ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (ids == null) throw new CpsException("Error while getting sharePointIds");

            // Update fileName
            try
            {
                await _driveRepository.UpdateFileNameAsync(ids.DriveId, ids.DriveItemId, fileName, getAsUser);
            }

            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"DriveItem (objectId = {objectId}) does not exist!");
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while updating driveItem", ex);
            }

            var fields = new FieldValueSet();
            fields.AdditionalData = new Dictionary<string, object>();

            // Update mimetype
            if (metadata == null || metadata.MimeType.IsNullOrEmpty())
            {
                new FileExtensionContentTypeProvider().TryGetContentType(fileName, out var mimeType);
                if (mimeType != null)
                {
                    var mapping = _globalSettings.MetadataMapping.Find(mapping => mapping.FieldName == nameof(FileInformation.MimeType));
                    if (mapping != null)
                    {
                        fields.AdditionalData[mapping.SpoColumnName] = mimeType;
                    }
                }
            }

            // Update fileExtension
            if (metadata == null || metadata.FileExtension.IsNullOrEmpty())
            {
                var fileExtension = Path.GetExtension(fileName).Replace(".", "");

                var fieldMapping = _globalSettings.MetadataMapping.Find(mapping => mapping.FieldName == nameof(FileInformation.FileExtension));
                if (fieldMapping != null)
                {
                    fields.AdditionalData[fieldMapping.SpoColumnName] = fileExtension;
                }
            }

            // Update title
            if (metadata == null || metadata.AdditionalMetadata == null || metadata.AdditionalMetadata.Title.IsNullOrEmpty())
            {
                var title = Path.GetFileNameWithoutExtension(fileName);

                var fieldMapping = _globalSettings.MetadataMapping.Find(mapping => mapping.FieldName == nameof(FileMetadata.Title));
                if (fieldMapping != null)
                {
                    fields.AdditionalData[fieldMapping.SpoColumnName] = title;
                }
            }

            // update sharepoint fields with metadata
            if (fields.AdditionalData.Count > 0)
            {
                await _listRepository.UpdateListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, fields, getAsUser);
            }
        }

        public async Task UpdateDropOffMetadataAsync(bool isComplete, string status, FileInformation metadata, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // map received metadata to SPO object
            var fields = MapMetadata(metadata, true, true);
            if (fields == null) throw new CpsException("Failed to map fields for metadata");
            var dropOffMetadata = new DropOffFileMetadata(isComplete, status);
            var dropOffFields = MapDropOffMetadata(dropOffMetadata);
            if (dropOffFields == null) throw new CpsException("Failed to map fields for metadata");
            fields.AdditionalData.AddRange(dropOffFields.AdditionalData);

            // update sharepoint fields with metadata
            if (fields.AdditionalData.Count > 0)
            {
                var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
                await _listRepository.UpdateListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId, fields, getAsUser);
            }
        }

        public async Task UpdateAdditionalIdentifiers(FileInformation metadata)
        {
            var additionalObjectIds = MapAdditionalIds(metadata);
            if (!string.IsNullOrEmpty(additionalObjectIds)) await _objectIdRepository.SaveAdditionalIdentifiersAsync(metadata.Ids.ObjectId, additionalObjectIds);
        }

        #endregion Update

        public async Task<bool> FileContainsMetadata(ObjectIdentifiers ids)
        {
            var listItem = await _listRepository.GetListItemAsync(ids.SiteId, ids.ListId, ids.ListItemId);
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

                    if (fieldMapping.FieldName.Equals(nameof(ids.ObjectId), StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    var propertyInfo = MetadataHelper.GetMetadataPropertyInfo(fieldMapping);
                    if (propertyInfo == null) throw new CpsException("Error while getting type of metadata");

                    if (FieldPropertyHelper.PropertyContainsData(value, defaultValue, propertyInfo))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        #region Map data

        private FieldValueSet MapMetadata(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            var fields = new FieldValueSet
            {
                AdditionalData = new Dictionary<string, object>()
            };
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                try
                {
                    if (!MetadataHelper.IsEditFieldAllowed(fieldMapping, isForNewFile))
                    {
                        continue;
                    }

                    var value = MetadataHelper.GetMetadataValue(metadata, fieldMapping);
                    var propertyInfo = MetadataHelper.GetMetadataPropertyInfo(fieldMapping, metadata);
                    if (MetadataHelper.KeepExistingValue(value, propertyInfo, isForNewFile, ignoreRequiredFields))
                    {
                        continue;
                    }

                    fields.AdditionalData[fieldMapping.SpoColumnName] = GetValue(fieldMapping, value, propertyInfo, isForNewFile, ignoreRequiredFields);
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

        private FieldValueSet MapDropOffMetadata(DropOffFileMetadata dropOffMetadata)
        {
            var fields = new FieldValueSet
            {
                AdditionalData = new Dictionary<string, object>()
            };
            foreach (var fieldMapping in _globalSettings.DropOffMetadataMapping)
            {
                try
                {
                    if (!MetadataHelper.IsEditFieldAllowed(fieldMapping, isForNewFile: false))
                    {
                        continue;
                    }

                    var value = dropOffMetadata[fieldMapping.FieldName];
                    var propertyInfo = dropOffMetadata.GetType().GetProperty(fieldMapping.FieldName);

                    fields.AdditionalData[fieldMapping.SpoColumnName] = GetValue(fieldMapping, value, propertyInfo);
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

        private object? GetValue(FieldMapping fieldMapping, object? value, PropertyInfo? propertyInfo, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            // Get default value
            var defaultValue = MetadataHelper.GetMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile, ignoreRequiredFields);
            if (defaultValue != null)
            {
                value = defaultValue;
            }

            // Default format for DateTimeOffset for 
            if (propertyInfo.PropertyType == typeof(DateTimeOffset?))
            {
                var stringValue = value?.ToString();
                var dateParsed = DateTimeOffset.TryParse(stringValue, out DateTimeOffset dateValue);
                if (!dateParsed && !ignoreRequiredFields && fieldMapping.Required)
                {
                    throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
                }

                if (dateValue == DateTimeOffset.MinValue)
                {
                    return null;
                }
                return dateValue.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz");
            }
            return value;
        }

        private List<ExternalReferenceItem> MapExternalReferences(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");
            if (metadata.ExternalReferences == null) throw new ArgumentNullException("metadata.ExternalReferences");

            var externalReferenceItems = new List<ExternalReferenceItem>();
            foreach (var externalReference in metadata.ExternalReferences)
            {
                var fields = new FieldValueSet
                {
                    AdditionalData = new Dictionary<string, object>()
                };
                foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
                {
                    try
                    {
                        if (!MetadataHelper.IsEditFieldAllowed(fieldMapping, isForNewFile))
                        {
                            continue;
                        }

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

                        if (MetadataHelper.KeepExistingValue(value, propertyInfo, isForNewFile, ignoreRequiredFields))
                        {
                            continue;
                        }

                        var defaultValue = MetadataHelper.GetMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile, ignoreRequiredFields);
                        if (defaultValue != null)
                        {
                            value = defaultValue;
                        }
                        if (MetadataHelper.IsMetadataFieldEmpty(value, propertyInfo))
                        {
                            continue;
                        }

                        fields.AdditionalData[fieldMapping.SpoColumnName] = value;
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName, ex);
                    }
                }
                externalReferenceItems.Add(new ExternalReferenceItem(new ListItem { Fields = fields }, externalReference));
            }
            return externalReferenceItems;
        }

        public string MapAdditionalIds(FileInformation metadata)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");
            if (string.IsNullOrEmpty(_globalSettings.AdditionalObjectId) || metadata.AdditionalMetadata[_globalSettings.AdditionalObjectId] == null) return string.Empty;

            try
            {
                var id = metadata.AdditionalMetadata[_globalSettings.AdditionalObjectId].ToString();
                return id ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Cannot parse additional ids", _globalSettings.AdditionalObjectId, ex);
            }
        }

        #endregion Map data

        #region Get data / Fields

        private async Task<List<ListItem>?> GetExternalReferenceListItems(ObjectIdentifiers ids, bool getAsUser = false)
        {
            var field = _globalSettings.ExternalReferencesMapping.Find(s => s.FieldName.Equals(Constants.ObjectIdSpoColumnName, StringComparison.InvariantCultureIgnoreCase));
            if (field == null) throw new CpsException("Object ID field not found in external reference mapping");

            return await _listRepository.GetListItemsAsync(ids.SiteId, ids.ExternalReferenceListId, field.SpoColumnName, ids.ObjectId, getAsUser);
        }

        #endregion Get data / Fields
    }
}