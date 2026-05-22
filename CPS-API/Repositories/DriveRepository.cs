using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;
using Constants = CPS_API.Models.Constants;

namespace CPS_API.Repositories
{
    public interface IDriveRepository
    {
        Task<string?> GetDriveIdAsync(string siteId, string listId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemIdsAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<DriveItem> CreateAsync(string driveId, string fileName, Stream fileStream, bool getAsUser = false);

        Task<DriveItem?> UpdateContentAsync(string driveId, string driveItemId, Stream fileStream, bool getAsUser = false);

        Task<DriveItem?> UpdateFileNameAsync(string driveId, string driveItemId, string fileName, bool getAsUser = false);

        Task DeleteFileAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<Stream> GetStreamAsync(string driveId, string driveItemId, bool getAsUser = false);
    }

    public class DriveRepository : IDriveRepository
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly GraphServiceClient _graphAppServiceClient;

        public DriveRepository(
            GraphServiceClient graphServiceClient,
            IOptions<GlobalSettings> settings,
            ITokenAcquisition tokenAcquisition)
        {
            _graphServiceClient = graphServiceClient;
            _graphAppServiceClient = new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(new AppOnlyAuthenticationProvider(tokenAcquisition, settings)));
        }

        private GraphServiceClient GetGraphServiceClient(bool getAsUser)
        {
            if (getAsUser) return _graphServiceClient;
            return _graphAppServiceClient;
        }

        public async Task<string?> GetDriveIdAsync(string siteId, string listId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var drive = await graphServiceClient.Sites[siteId].Lists[listId].Drive.GetAsync(x =>
            {
                x.QueryParameters.Select = [Constants.Selectors.Id];
            });
            if (drive == null) throw new CpsException($"Error while getting drive (siteId={siteId}, listId={listId})");
            return drive.Id;
        }

        public async Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false)
        {
            if (string.IsNullOrEmpty(siteId))
            {
                throw new CpsException("Error while getting driveItem, unkown SiteId");
            }
            else if (string.IsNullOrEmpty(listId))
            {
                throw new CpsException("Error while getting driveItem, unkown ListId");
            }
            else if (string.IsNullOrEmpty(listItemId))
            {
                throw new CpsException("Error while getting driveItem, unkown ListItemId");
            }

            try
            {
                var graphServiceClient = GetGraphServiceClient(getAsUser);
                var driveItem = await graphServiceClient.Sites[siteId].Lists[listId].Items[listItemId].DriveItem.GetAsync();
                if (driveItem == null) throw new FileNotFoundException($"DriveItem (SiteId = {siteId}, ListId = {listId}, ListItemId = {listItemId}) does not exist!");
                return driveItem;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"DriveItem (SiteId = {siteId}, ListId = {listId}, ListItemId = {listItemId}) does not exist!");
            }
            catch (Exception ex) when (ex is MsalUiRequiredException || ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting driveItem", ex);
            }
        }

        public async Task<DriveItem> GetDriveItemIdsAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var driveItem = await graphServiceClient.Drives[driveId].Items[driveItemId].GetAsync(x =>
            {
                x.QueryParameters.Select = new[] { Constants.Selectors.SharePointIds };
            });
            if (driveItem == null) throw new CpsException($"Error while getting driveItem (driveId={driveId}, driveItemId={driveItemId})");
            return driveItem;
        }

        public async Task<DriveItem> CreateAsync(string driveId, string fileName, Stream fileStream, bool getAsUser = false)
        {
            if (fileStream.Length > 0)
            {
                var graphServiceClient = GetGraphServiceClient(getAsUser);
                var uploadSessionRequestBody = GetUploadSessionRequestBody("fail");
                var uploadSession = await graphServiceClient.Drives[driveId].Root.ItemWithPath(fileName).CreateUploadSession.PostAsync(uploadSessionRequestBody);
                if (uploadSession == null) throw new CpsException($"Error while creating file (driveId={driveId}, fileName={fileName})");
                return await UploadFileAsync(uploadSession, fileStream);
            }
            else
            {
                throw new CpsException("Cannot upload empty file stream.");
            }
        }

        public async Task<DriveItem?> UpdateContentAsync(string driveId, string driveItemId, Stream fileStream, bool getAsUser = false)
        {
            if (fileStream.Length > 0)
            {
                var graphServiceClient = GetGraphServiceClient(getAsUser);
                var uploadSessionRequestBody = GetUploadSessionRequestBody("replace");
                var uploadSession = await graphServiceClient.Drives[driveId].Items[driveItemId].CreateUploadSession.PostAsync(uploadSessionRequestBody);
                if (uploadSession == null) throw new CpsException($"Error while creating file (driveId={driveId}, driveItemId={driveItemId})");
                return await UploadFileAsync(uploadSession, fileStream);
            }
            else
            {
                throw new CpsException("Cannot upload empty file stream.");
            }
        }

        private static CreateUploadSessionPostRequestBody GetUploadSessionRequestBody(string conflictBehavior)
        {
            return new CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                        {
                            { "@microsoft.graph.conflictBehavior", conflictBehavior },
                    },
                },
            };
        }

        private static async Task<DriveItem> UploadFileAsync(UploadSession uploadSession, Stream fileStream)
        {
            // 10 MB; recommended fragment size is between 5-10 MiB
            var chunkSize = (320 * 1024) * 32;
            var fileUploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, fileStream, chunkSize);

            DriveItem? driveItem = null;
            try
            {
                // Upload the file
                var uploadResult = await fileUploadTask.UploadAsync();
                if (uploadResult.UploadSucceeded)
                    driveItem = uploadResult.ItemResponse;
            }
            catch (ODataError ex)
            {
                throw new CpsException("Failed to upload file.", ex);
            }

            if (driveItem == null)
            {
                throw new CpsException("Failed to upload file.");
            }

            return driveItem;
        }

        public async Task<DriveItem?> UpdateFileNameAsync(string driveId, string driveItemId, string fileName, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var driveItem = new DriveItem
            {
                Name = fileName
            };
            var updatedDriveItem = await graphServiceClient.Drives[driveId].Items[driveItemId].PatchAsync(driveItem);
            if (updatedDriveItem == null) throw new CpsException($"Error while updating filename (driveId={driveId}, driveItemId={driveItemId}, fileName={fileName})");
            return updatedDriveItem;
        }

        public async Task DeleteFileAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            await graphServiceClient.Drives[driveId].Items[driveItemId].DeleteAsync();
        }

        public async Task<Stream> GetStreamAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            Stream? memStream = await graphServiceClient.Drives[driveId].Items[driveItemId].Content.GetAsync();
            if (memStream == null) throw new CpsException($"Error while getting stream (driveId:{driveId}, driveItemId:{driveItemId})");
            var bufferedStream = new MemoryStream();
            await memStream.CopyToAsync(bufferedStream);//buffer the stream so that its seekable.
            return bufferedStream;
        }
    }
}