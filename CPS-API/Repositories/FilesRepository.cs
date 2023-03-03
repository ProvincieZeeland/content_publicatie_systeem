using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CPS_API.Models;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.Taxonomy;
using FileInformation = CPS_API.Models.FileInformation;
using ListItem = Microsoft.Graph.ListItem;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetFileAsync(string objectId);

        Task<string> GetUrlAsync(string objectId, bool getAsUser = false);

        Task<ObjectIdentifiers> CreateFileAsync(CpsFile file);

        Task<ObjectIdentifiers> CreateFileAsync(CpsFile file, IFormFile formFile);

        Task UpdateContentAsync(string objectId, byte[] content, bool getAsUser = false);

        Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false);

        Task UpdateMetadataAsync(FileInformation metadata, bool getAsUser = false);

        Task<bool> FileContainsMetadata(ObjectIdentifiers ids);
    }

    public class FilesRepository : IFilesRepository
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IDriveRepository _driveRepository;
        private readonly ILogger _logger;

        public FilesRepository(
            GraphServiceClient graphClient,
            IObjectIdRepository objectIdRepository,
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IDriveRepository driveRepository,
            ILogger<FilesRepository> logger)
        {
            _graphClient = graphClient;
            _objectIdRepository = objectIdRepository;
            _globalSettings = settings.Value;
            _driveRepository = driveRepository;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> GetUrlAsync(string objectId, bool getAsUser = false)
        {
            ObjectIdentifiersEntity? objectIdentifiers;
            try
            {
                objectIdentifiers = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            }
            catch (Exception ex) when (ex.InnerException is not UnauthorizedAccessException && ex is not FileNotFoundException)
            {
                throw new Exception("Error while getting objectIdentifiers");
            }
            if (objectIdentifiers == null) throw new FileNotFoundException($"ObjectIdentifiers (objectId = {objectId}) does not exist!");

            DriveItem? driveItem;
            try
            {
                driveItem = await _driveRepository.GetDriveItemAsync(objectIdentifiers.SiteId, objectIdentifiers.ListId, objectIdentifiers.ListItemId, getAsUser);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw ex;
            }
            catch (MsalUiRequiredException ex)
            {
                throw ex;
            }
            catch (Exception ex) when (ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                throw ex;
            }
            catch (Exception ex) when (ex.InnerException is not UnauthorizedAccessException && ex.Message != "Access denied")
            {
                throw new Exception("Error while getting driveItem");
            }
            if (driveItem == null) throw new FileNotFoundException($"DriveItem (objectId = {objectId}) does not exist!");

            // Get url
            return driveItem.WebUrl;
        }

        public async Task<CpsFile> GetFileAsync(string objectId)
        {
            FileInformation metadata;
            try
            {
                metadata = await GetMetadataAsync(objectId);
            }
            catch (Exception)
            {
                throw new Exception("Error while getting metadata");
            }
            if (metadata == null) throw new FileNotFoundException($"Metadata (objectId = {objectId}) does not exist!");

            return new CpsFile
            {
                Metadata = metadata
            };
        }

        public async Task<ObjectIdentifiers> CreateFileAsync(CpsFile file)
        {
            return await CreateFileAsync(file, null);
        }

        public async Task<ObjectIdentifiers> CreateFileAsync(CpsFile file, IFormFile formFile)
        {
            if (file.Metadata == null) throw new NullReferenceException(nameof(file.Metadata));

            var ids = new ObjectIdentifiers();

            // Get driveid or site matching classification & source           
            if (file.Metadata.AdditionalMetadata == null) throw new NullReferenceException(nameof(file.Metadata.AdditionalMetadata));

            var locationMapping = _globalSettings.LocationMapping.FirstOrDefault(item =>
                                    item.Classification.Equals(file.Metadata.AdditionalMetadata.Classification, StringComparison.OrdinalIgnoreCase)
                                    && item.Source.Equals(file.Metadata.AdditionalMetadata.Source, StringComparison.OrdinalIgnoreCase)
                                  );
            if (locationMapping == null) throw new Exception($"{nameof(locationMapping)} does not exist ({nameof(file.Metadata.AdditionalMetadata.Classification)}: \"{file.Metadata.AdditionalMetadata.Classification}\", {nameof(file.Metadata.AdditionalMetadata.Source)}: \"{file.Metadata.AdditionalMetadata.Source}\")");
            ids.DriveId = locationMapping.ExternalReferenceListId;

            var drive = await _driveRepository.GetDriveAsync(locationMapping.SiteId, locationMapping.ListId);
            if (drive == null) throw new Exception("Drive not found for new file.");
            ids.DriveId = drive.Id;

            // Add new file to SharePoint
            DriveItem driveItem;
            try
            {
                if (formFile != null)
                {
                    using (var fileStream = formFile.OpenReadStream())
                    {
                        if (fileStream.Length > 0)
                        {
                            driveItem = await _driveRepository.CreateAsync(ids.DriveId, file.Metadata.FileName, fileStream);
                        }
                        else
                        {
                            throw new Exception("File cannot be empty");
                        }
                    }
                }
                else
                {
                    using (var memorstream = new MemoryStream(file.Content))
                    {
                        if (memorstream.Length > 0)
                        {
                            memorstream.Position = 0;
                            driveItem = await _driveRepository.CreateAsync(ids.DriveId, file.Metadata.FileName, memorstream);
                        }
                        else
                        {
                            throw new Exception("File cannot be empty");
                        }
                    }
                }

                if (driveItem == null)
                {
                    throw new Exception("Error while adding new file");
                }

                ids.DriveItemId = driveItem.Id;
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                // TODO: Log error in App Insights
                throw;
            }
            // todo: handle file exists exception
            catch (Exception)
            {
                // TODO: Log error in App Insights
                throw new Exception("Error while adding new file");
            }

            // Generate objectId
            string objectId;
            try
            {
                objectId = await _objectIdRepository.GenerateObjectIdAsync(ids);
                if (objectId.IsNullOrEmpty()) throw new Exception("ObjectId is empty");

                ids.ObjectId = objectId;
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw new Exception("Error while generating ObjectId");
            }

            file.Metadata.Ids = ids;
            // Update ObjectId and metadata in Sharepoint with Graph
            try
            {
                await UpdateMetadataWithoutExternalReferencesAsync(file.Metadata, isForNewFile: true);
            }
            catch (FieldRequiredException)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw;
            }
            catch (Exception ex)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw new Exception("Error while updating metadata", ex);
            }

            // Update ExternalReferences in Sharepoint with Graph
            try
            {
                await UpdateExternalReferencesAsync(file.Metadata);
            }
            catch (FieldRequiredException)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw;
            }
            catch (Exception ex)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw new Exception("Error while updating external references");
            }

            // Done
            return ids;
        }

        public async Task UpdateContentAsync(string objectId, byte[] content, bool getAsUser = false)
        {
            // Get File metadata
            // When updating content, all metadata gets remove for the file.
            // After de content update, we perform a metadata update to keep the metadata.
            // TODO: Is there a better solution?
            var metadata = await this.GetMetadataAsync(objectId);

            // Get objectIdentifiers
            ObjectIdentifiersEntity? ids;
            try
            {
                ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            }
            catch (Exception)
            {
                // TODO: Log error in App Insights

                throw new Exception("Error while getting objectIdentifiers");
            }
            if (ids == null) throw new FileNotFoundException("ObjectIdentifiers not found");

            // Create new version
            try
            {
                using var stream = new MemoryStream(content);
                var request = _graphClient.Drives[ids.DriveId].Items[ids.DriveItemId].Content.Request();
                if (!getAsUser)
                {
                    request = request.WithAppOnly();
                }
                await request.PutAsync<DriveItem>(stream);
            }
            catch (Exception ex)
            {
                // TODO: Log error in App Insights

                throw new Exception("Error while updating driveItem", ex);
            }

            // Update metadata in Sharepoint with Graph
            if (metadata != null)
            {
                try
                {
                    await UpdateMetadataWithoutExternalReferencesAsync(metadata);
                }
                catch (FieldRequiredException)
                {
                    // TODO: Log error in App Insights

                    throw;
                }
                catch (Exception ex)
                {
                    // TODO: Log error in App Insights

                    throw new Exception("Error while updating metadata", ex);
                }
            }
        }

        #region Metadata

        public async Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false)
        {
            var ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (ids == null)
            {
                throw new Exception("Error while getting objectIdentifiers");
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
                throw ex;
            }
            catch (MsalUiRequiredException ex)
            {
                throw ex;
            }
            catch (Exception ex) when (ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                throw ex;
            }
            catch (Exception)
            {
                throw new Exception("Error while getting listItem");
            }
            if (listItem == null) throw new FileNotFoundException($"LisItem (objectId = {objectId}) does not exist!");

            DriveItem? driveItem;
            try
            {
                driveItem = await getDriveItem(objectId, getAsUser);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw ex;
            }
            catch (Exception)
            {
                throw new Exception("Error while getting driveItem");
            }
            if (driveItem == null) throw new FileNotFoundException($"DriveItem (objectId = {objectId}) does not exist!");

            var fileName = listItem.Name.IsNullOrEmpty() ? driveItem.Name : listItem.Name;

            metadata.MimeType = "application/pdf";
            if (driveItem.File != null && driveItem.File.MimeType != null)
            {
                metadata.MimeType = driveItem.File.MimeType;
            }
            metadata.FileName = fileName;
            metadata.AdditionalMetadata = new FileMetadata();

            metadata.FileExtension = "pdf";
            if (!fileName.IsNullOrEmpty() && fileName.Contains('.'))
                metadata.FileExtension = Path.GetExtension(fileName).Replace(".", "");

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
                if (value != null && (fieldMapping.FieldName == nameof(metadata.AdditionalMetadata.DocumentType) || fieldMapping.FieldName == nameof(metadata.AdditionalMetadata.Classification) || fieldMapping.FieldName == nameof(metadata.AdditionalMetadata.Source)))
                {
                    var jsonString = value.ToString();
                    var term = JsonSerializer.Deserialize<TaxonomyItemDto>(jsonString);
                    value = term?.Label;
                }

                if (fieldMapping.FieldName == nameof(metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(metadata.SourceModifiedBy))
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
                    if (value != null && fieldMapping.FieldName == nameof(externalReference.ExternalApplication))
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

        public async Task UpdateMetadataAsync(FileInformation metadata, bool getAsUser = false)
        {
            metadata.Ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
            await UpdateMetadataWithoutExternalReferencesAsync(metadata, getAsUser: getAsUser);
            await UpdateExternalReferencesAsync(metadata, getAsUser: getAsUser);
        }

        public async Task UpdateMetadataWithoutExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // map received metadata to SPO object
            var fields = mapMetadata(metadata, isForNewFile);
            if (fields == null) throw new NullReferenceException(nameof(fields));

            // update sharepoint fields with metadata
            var ids = await _objectIdRepository.FindMissingIds(metadata.Ids, getAsUser);
            var request = _graphClient.Sites[ids.SiteId].Lists[ids.ListId].Items[ids.ListItemId].Fields.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            await request.UpdateAsync(fields);

            // update terms
            await updateTermsForMetadataAsync(metadata, isForNewFile, getAsUser);
        }

        private FieldValueSet mapMetadata(FileInformation metadata, bool isForNewFile = false)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");

            var fields = new FieldValueSet();
            fields.AdditionalData = new Dictionary<string, object>();
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                try
                {
                    if (fieldMapping.ReadOnly)
                    {
                        continue;
                    }

                    var value = getMetadataValue(metadata, fieldMapping);

                    // Term is saved separately.
                    if (fieldMapping.FieldName == nameof(metadata.AdditionalMetadata.DocumentType) || fieldMapping.FieldName == nameof(metadata.AdditionalMetadata.Classification) || fieldMapping.FieldName == nameof(metadata.AdditionalMetadata.Source))
                    {
                        continue;
                    }

                    // Keep the existing value, if value equals null.
                    if (!isForNewFile && value == null)
                    {
                        continue;
                    }

                    var propertyInfo = getMetadataPropertyInfo(metadata, fieldMapping);
                    var defaultValue = getMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile);
                    if (defaultValue != null)
                    {
                        value = defaultValue;
                    }

                    if (propertyInfo.PropertyType == typeof(DateTime?))
                    {
                        var stringValue = value.ToString();
                        var dateParsed = DateTime.TryParse(stringValue, out var dateValue);
                        if (!dateParsed)
                        {
                            throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
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

        public async Task UpdateExternalReferencesAsync(FileInformation metadata, bool isForNewFile = false, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            // Keep the existing value, if value equals null.
            if (metadata.ExternalReferences == null)
            {
                return;
            }

            // map received metadata to SPO object
            var listItems = mapExternalReferences(metadata, getAsUser: getAsUser);
            if (listItems == null) throw new NullReferenceException(nameof(listItems));

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
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error while adding externalReference (Fields = {JsonSerializer.Serialize(listItem.Fields)})");
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
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error while updating externalReference (Id = {existingListItem.Id}, Fields = {JsonSerializer.Serialize(listItem.Fields)})");
                        throw;
                    }
                }
                i++;
            }

            // update terms
            await updateTermsForExternalReferencesAsync(metadata, newAndUpdatedLisItemIds, isForNewFile, getAsUser);
        }

        private List<ListItem> mapExternalReferences(FileInformation metadata, bool isForNewFile = false, bool getAsUser = false)
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

                        // Term is saved separately.
                        if (fieldMapping.FieldName == nameof(externalReference.ExternalApplication))
                        {
                            continue;
                        }

                        // Keep the existing value, if value equals null.
                        if (value == null)
                        {
                            continue;
                        }

                        var fieldIsEmpty = false;
                        if (value == null)
                        {
                            fieldIsEmpty = true;
                        }
                        else if (propertyInfo.PropertyType == typeof(string))
                        {
                            var stringValue = value.ToString();
                            fieldIsEmpty = (stringValue == string.Empty);
                        }

                        if (fieldMapping.Required && fieldIsEmpty)
                        {
                            if (isForNewFile && fieldMapping.DefaultValue != null && !fieldMapping.DefaultValue.ToString().IsNullOrEmpty())
                            {
                                value = fieldMapping.DefaultValue;
                            }
                            else
                            {
                                throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
                            }
                        }

                        fields.AdditionalData[fieldMapping.SpoColumnName] = value;
                    }
                    catch
                    {
                        throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                    }
                }
                listItems.Add(new ListItem { Fields = fields });
            }

            return listItems;
        }

        #region Terms

        private async Task updateTermsForMetadataAsync(FileInformation metadata, bool isForNewFile = false, bool getAsUser = false)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");

            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                try
                {
                    if (fieldMapping.ReadOnly)
                    {
                        continue;
                    }

                    // Only try to edit the terms.
                    if (fieldMapping.FieldName != nameof(metadata.AdditionalMetadata.DocumentType) && fieldMapping.FieldName != nameof(metadata.AdditionalMetadata.Classification) && fieldMapping.FieldName != nameof(metadata.AdditionalMetadata.Source))
                    {
                        continue;
                    }

                    var value = getMetadataValue(metadata, fieldMapping);

                    // Keep the existing value, if value equals null.
                    if (!isForNewFile && value == null)
                    {
                        continue;
                    }

                    var propertyInfo = getMetadataPropertyInfo(metadata, fieldMapping);
                    var defaultValue = getMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile);
                    if (defaultValue != null)
                    {
                        value = defaultValue;
                    }

                    await updateTermsForsListItem(metadata.Ids.SiteId, metadata.Ids.ListId, metadata.Ids.ListItemId, fieldMapping.SpoColumnName, value, getAsUser);
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
        }

        private object? getMetadataValue(FileInformation metadata, FieldMapping fieldMapping)
        {
            PropertyInfo propertyInfo;
            if (fieldMapping.FieldName == nameof(metadata.Ids.ObjectId))
            {
                return metadata.Ids.ObjectId;
            }
            else if (fieldMapping.FieldName == nameof(metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(metadata.SourceModifiedBy))
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
            else if (fieldMapping.FieldName == nameof(metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(metadata.SourceModifiedBy))
            {
                return metadata.GetType().GetProperty(fieldMapping.FieldName);
            }
            else
            {
                return metadata.AdditionalMetadata.GetType().GetProperty(fieldMapping.FieldName);
            }
        }

        private object? getMetadataDefaultValue(object? value, PropertyInfo propertyInfo, FieldMapping fieldMapping, bool isForNewFile)
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
                else
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

        private async Task updateTermsForExternalReferencesAsync(FileInformation metadata, List<string> listItemIds, bool isForNewFile = false, bool getAsUser = false)
        {
            if (metadata.ExternalReferences == null) throw new ArgumentNullException("metadata.ExternalReferences");

            var i = 0;
            foreach (var externalReference in metadata.ExternalReferences)
            {
                foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
                {
                    try
                    {
                        // Only try to edit the terms.
                        if (fieldMapping.FieldName != nameof(externalReference.ExternalApplication))
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

                        // Keep the existing value, if value equals null.
                        if (value == null)
                        {
                            continue;
                        }

                        var fieldIsEmpty = false;
                        if (value == null)
                        {
                            fieldIsEmpty = true;
                        }
                        else if (propertyInfo.PropertyType == typeof(string))
                        {
                            var stringValue = value.ToString();
                            fieldIsEmpty = (stringValue == string.Empty);
                        }

                        if (fieldMapping.Required && fieldIsEmpty)
                        {
                            if (isForNewFile && fieldMapping.DefaultValue != null && !fieldMapping.DefaultValue.ToString().IsNullOrEmpty())
                            {
                                value = fieldMapping.DefaultValue;
                            }
                            else
                            {
                                throw new FieldRequiredException($"The {fieldMapping.FieldName} field is required");
                            }
                        }

                        await updateTermsForsListItem(metadata.Ids.SiteId, metadata.Ids.ExternalReferenceListId, listItemIds[i], fieldMapping.SpoColumnName, value, getAsUser);
                    }
                    catch
                    {
                        throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                    }
                }
                i++;
            }
        }

        private async Task updateTermsForsListItem(string siteId, string listId, string listItemId, string SpoColumnName, object? value, bool getAsUser)
        {
            if (_globalSettings.CertificateThumbprint.IsNullOrEmpty())
            {
                return;
            }

            var site = await _driveRepository.GetSiteAsync(siteId, getAsUser);
            if (site == null) throw new Exception("Error while getting site");
            var displayName = site.DisplayName.Replace(" ", "");
            var baseUrl = $"{_globalSettings.RootSiteUrl}/sites/{displayName}";

            try
            {
                using (var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, StoreName.My, StoreLocation.CurrentUser, _globalSettings.CertificateThumbprint, _globalSettings.TenantId))
                using (ClientContext context = await authenticationManager.GetContextAsync(baseUrl))
                {
                    var listItem = await getTermsForListItem(context, listId, listItemId, SpoColumnName, value, getAsUser);
                    listItem.Update();
                    context.ExecuteQuery();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error while updating terms (SpoColumnName = {SpoColumnName}, baseUrl = {baseUrl}). Error: {ex.Message}");
                throw new Exception("Error while updating terms");
            }
        }

        private async Task<Microsoft.SharePoint.Client.ListItem> getTermsForListItem(ClientContext context, string listId, string listItemId, string spoColumnName, object value, bool getAsUser = false)
        {
            // Get TermId for value
            var taxonomySession = TaxonomySession.GetTaxonomySession(context);
            context.Load(taxonomySession,
                ts => ts.TermStores.Include(
                    store => store.Name,
                    store => store.Groups.Include(group => group.Name)
                )
            );
            await context.ExecuteQueryAsync();

            var term = await getTermSet(context, spoColumnName, value);
            if (term == null) throw new Exception("Term not found");

            var list = context.Web.Lists.GetById(Guid.Parse(listId));
            var listItem = list.GetItemById(listItemId);

            var fields = listItem.ParentList.Fields;
            context.Load(fields);
            context.ExecuteQuery();

            var field = context.CastTo<TaxonomyField>(fields.GetByInternalNameOrTitle(spoColumnName));
            context.Load(field);

            var termValue = new TaxonomyFieldValue()
            {
                Label = term.Name,
                TermGuid = term.Id.ToString(),
                WssId = -1
            };
            field.SetFieldValueByValue(listItem, termValue);

            return listItem;
        }

        private async Task<Term> getTermSet(ClientContext context, string spoColumnName, object value)
        {
            var taxonomySession = TaxonomySession.GetTaxonomySession(context);
            context.Load(taxonomySession,
                ts => ts.TermStores.Include(
                    store => store.Name,
                    store => store.Groups.Include(group => group.Name)
                )
            );
            await context.ExecuteQueryAsync();
            if (taxonomySession == null)
            {
                return null;
            }

            var termStore = taxonomySession.GetDefaultSiteCollectionTermStore();
            if (termStore == null)
            {
                return null;
            }

            var termGroup = termStore.GetTermGroupByName("Content Publicatiesysteem");
            await context.ExecuteQueryAsync();
            if (termGroup == null)
            {
                return null;
            }

            context.Load(termGroup.TermSets);
            await context.ExecuteQueryAsync();

            var termSet = termGroup.TermSets.FirstOrDefault(item => item.Name == spoColumnName);
            if (termSet == null)
            {
                return null;
            }

            var terms = termSet.GetAllTerms();
            await context.ExecuteQueryAsync();

            var term = terms.GetByName(value.ToString());
            context.Load(term);
            await context.ExecuteQueryAsync();
            return term;
        }

        #endregion

        #endregion

        public async Task<bool> FileContainsMetadata(ObjectIdentifiers ids)
        {
            var listItem = await getListItem(ids);
            if (listItem == null) throw new Exception($"Error while getting listItem (SiteId = \"{ids.SiteId}\", ListId = \"{ids.ListId}\", ListItemId = \"{ids.ListItemId}\")");

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
                    if (propertyInfo == null) throw new Exception("Error while getting type of metadata");

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

        #region Helpers

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