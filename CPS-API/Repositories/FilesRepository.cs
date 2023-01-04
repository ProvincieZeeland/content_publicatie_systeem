using CPS_API.Models;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using System.Net;

namespace CPS_API.Repositories
{
    public interface IFilesRepository
    {
        Task<CpsFile> GetFileAsync(string objectId);

        Task<string> GetUrlAsync(string objectId, bool getAsUser = false);

        Task<ObjectIdentifiers> CreateFileAsync(CpsFile file);

        Task<ObjectIdentifiers> CreateFileAsync(CpsFile file, IFormFile formFile);

        Task<bool> UpdateContentAsync(HttpRequest Request, string objectId, byte[] content, bool getAsUser = false);

        Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false);

        Task<FieldValueSet?> UpdateMetadataAsync(FileInformation metadata, bool getAsUser = false);
    }

    public class FilesRepository : IFilesRepository
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IDriveRepository _driveRepository;

        public FilesRepository(GraphServiceClient graphClient, IObjectIdRepository objectIdRepository, Microsoft.Extensions.Options.IOptions<GlobalSettings> settings, IDriveRepository driveRepository)
        {
            _graphClient = graphClient;
            _objectIdRepository = objectIdRepository;
            _globalSettings = settings.Value;
            _driveRepository = driveRepository;
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

            // Get driveid or site matching classification & source
            var driveId = file.Metadata?.Ids?.DriveId;
            if (driveId.IsNullOrEmpty())
            {
                if (file.Metadata.AdditionalMetadata == null) throw new NullReferenceException(nameof(file.Metadata.AdditionalMetadata));

                var locationMapping = _globalSettings.LocationMapping.FirstOrDefault(item =>
                                        item.Classification == file.Metadata.AdditionalMetadata.Classification
                                        && item.Source == file.Metadata.AdditionalMetadata.Source
                                      );
                if (locationMapping == null) throw new Exception($"{nameof(locationMapping)} does not exist ({nameof(file.Metadata.AdditionalMetadata.Classification)}: \"{file.Metadata.AdditionalMetadata.Classification}\", {nameof(file.Metadata.AdditionalMetadata.Source)}: \"{file.Metadata.AdditionalMetadata.Source}\")");

                var drive = await _driveRepository.GetDriveAsync(locationMapping.SiteId, locationMapping.ListId);
                driveId = drive?.Id;
            }
            if (driveId == null) throw new NullReferenceException(nameof(driveId));

            var ids = new ObjectIdentifiers
            {
                DriveId = driveId
            };

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

            // Update ObjectId and metadata in Sharepoint with Graph
            try
            {
                file.Metadata.Ids = ids;
                await UpdateMetadataAsync(file.Metadata);
            }
            catch (Exception ex)
            {
                // TODO: Log error in App Insights

                // Remove file from Sharepoint
                await _driveRepository.DeleteFileAsync(ids.DriveId, driveItem.Id);

                throw new Exception("Error while updating metadata");
            }

            // Done
            return ids;
        }

        public async Task<bool> UpdateContentAsync(HttpRequest Request, string objectId, byte[] content, bool getAsUser = false)
        {
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

            return true;
        }

        #region Metadata

        public async Task<FileInformation> GetMetadataAsync(string objectId, bool getAsUser = false)
        {
            ListItem? file;
            try
            {
                file = await getListItem(objectId, getAsUser);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw ex;
            }
            catch (Exception)
            {
                throw new Exception("Error while getting listItem");
            }
            if (file == null) throw new FileNotFoundException($"LisItem (objectId = {objectId}) does not exist!");

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

            var fileName = file.Name.IsNullOrEmpty() ? driveItem.Name : file.Name;

            FileInformation metadata = new FileInformation();
            metadata.MimeType = "application/pdf";
            if (driveItem.File != null && driveItem.File.MimeType != null)
            {
                metadata.MimeType = driveItem.File.MimeType;
            }
            metadata.FileName = fileName;
            metadata.AdditionalMetadata = new FileMetadata();

            metadata.FileExtension = "pdf";
            if (!fileName.IsNullOrEmpty() && fileName.Contains('.'))
                metadata.FileExtension = fileName.Split('.')[1];

            if (file.CreatedDateTime.HasValue)
                metadata.CreatedOn = file.CreatedDateTime.Value.DateTime;

            if (file.CreatedBy.User != null)
                metadata.CreatedBy = file.CreatedBy.User.DisplayName;
            else if (file.CreatedBy.Application != null)
                metadata.CreatedBy = file.CreatedBy.Application.DisplayName;

            if (file.LastModifiedDateTime.HasValue)
                metadata.ModifiedOn = file.LastModifiedDateTime.Value.DateTime;

            if (file.LastModifiedBy.User != null)
                metadata.ModifiedBy = file.LastModifiedBy.User.DisplayName;
            else if (file.LastModifiedBy.Application != null)
                metadata.ModifiedBy = file.LastModifiedBy.Application.DisplayName;

            foreach (var fieldMapping in _globalSettings.MetadataSettings)
            {
                // create object with sharepoint fields metadata + url to item
                file.Fields.AdditionalData.TryGetValue(fieldMapping.SpoColumnName, out var value);
                if (value == null)
                {
                    // log warning to insights?
                    // TODO: If the property has no value in Sharepoint then the column is not present in AdditionalData, how do we handle this?
                }
                else if (fieldMapping.FieldName == nameof(metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(metadata.SourceModifiedBy))
                {
                    metadata[fieldMapping.FieldName] = value;
                }
                else
                {
                    metadata.AdditionalMetadata[fieldMapping.FieldName] = value;
                }
            }

            var ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (ids == null)
            {
                throw new Exception("Error while getting objectIdentifiers");
            }

            metadata.Ids = new ObjectIdentifiers(ids);
            return metadata;
        }

        public async Task<FieldValueSet?> UpdateMetadataAsync(FileInformation metadata, bool getAsUser = false)
        {
            if (metadata == null) throw new ArgumentNullException("metadata");
            if (metadata.Ids == null) throw new ArgumentNullException("metadata.Ids");

            ListItem? listItem;
            try
            {
                listItem = await getListItem(metadata.Ids.ObjectId);
            }
            catch (Exception ex)
            {
                throw new Exception("Error while getting listItem");
            }
            if (listItem == null) throw new FileNotFoundException();

            // map received metadata to SPO object
            var fields = mapMetadata(metadata);
            if (fields == null) throw new NullReferenceException(nameof(fields));

            // update sharepoint fields with metadata
            var request = _graphClient.Sites[metadata.Ids.SiteId].Lists[metadata.Ids.ListId].Items[metadata.Ids.ListItemId].Fields.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.UpdateAsync(fields);
        }

        private FieldValueSet mapMetadata(FileInformation metadata)
        {
            if (metadata.AdditionalMetadata == null) throw new ArgumentNullException("metadata.AdditionalMetadata");

            var fields = new FieldValueSet();
            fields.AdditionalData = new Dictionary<string, object>();
            foreach (var fieldMapping in _globalSettings.MetadataSettings)
            {
                try
                {
                    object? value;
                    if (fieldMapping.FieldName == nameof(metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(metadata.SourceModifiedBy))
                    {
                        value = metadata[fieldMapping.FieldName];
                    }
                    else
                    {
                        value = metadata.AdditionalMetadata[fieldMapping.FieldName];
                    }

                    if (value is DateTime dateValue)
                    {
                        if (dateValue == DateTime.MinValue)
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
                catch
                {
                    throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
                }
            }
            return fields;
        }

        #endregion

        #region Helpers

        private async Task<ListItem?> getListItem(string objectId, bool getAsUser = false)
        {
            // Find file info in documents table by objectId
            var ids = await _objectIdRepository.GetObjectIdentifiersAsync(objectId);
            if (ids == null) throw new FileNotFoundException($"ObjectIdentifiers (objectId = {objectId}) does not exist!");

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