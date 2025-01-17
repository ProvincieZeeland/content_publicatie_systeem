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
using Drive = Microsoft.Graph.Models.Drive;
using GraphDeltaResponse = Microsoft.Graph.Drives.Item.Items.Item.Delta.DeltaGetResponse;

namespace CPS_API.Repositories
{
    public interface IDriveRepository
    {
        Task<Drive> GetDriveAsync(string siteId, string listId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemIdsAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<DriveItem> CreateAsync(string driveId, string fileName, Stream fileStream, bool getAsUser = false);

        Task<DriveItem?> UpdateContentAsync(string driveId, string driveItemId, Stream fileStream, bool getAsUser = false);

        Task<DriveItem?> UpdateFileNameAsync(string driveId, string driveItemId, string fileName, bool getAsUser = false);

        Task DeleteFileAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<DeltaResponse> GetNewItems(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens, bool getAsUser = false);

        Task<DeltaResponse> GetUpdatedItems(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens, bool getAsUser = false);

        Task<DeltaResponse> GetDeletedItems(Dictionary<string, string> tokens, bool getAsUser = false);

        Task<Stream> GetStreamAsync(string driveId, string driveItemId, bool getAsUser = false);
    }

    public class DriveRepository : IDriveRepository
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly GraphServiceClient _graphAppServiceClient;
        private readonly GlobalSettings _globalSettings;

        public DriveRepository(
            GraphServiceClient graphServiceClient,
            IOptions<GlobalSettings> settings,
            ITokenAcquisition tokenAcquisition)
        {
            _graphServiceClient = graphServiceClient;
            _graphAppServiceClient = new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(new AppOnlyAuthenticationProvider(tokenAcquisition, settings)));
            _globalSettings = settings.Value;
        }

        private GraphServiceClient GetGraphServiceClient(bool getAsUser)
        {
            if (getAsUser) return _graphServiceClient;
            return _graphAppServiceClient;
        }

        public async Task<Drive> GetDriveAsync(string siteId, string listId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var drive = await graphServiceClient.Sites[siteId].Lists[listId].Drive.GetAsync();
            if (drive == null) throw new CpsException($"Error while getting drive (siteId={siteId}, listId={listId})");
            return drive;
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
                x.QueryParameters.Select = new[] { Constants.SelectSharePointIds };
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

        public async Task<DeltaResponse> GetNewItems(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens, bool getAsUser = false)
        {
            var response = await GetDeltaForPublicDrivesAsync(tokens, getAsUser);
            response.Items = response.Items.Where(item => item.Deleted == null && item.CreatedDateTime >= lastSynchronisation).ToList();
            response.Items = response.Items.Where(item => item.Folder == null).ToList();
            response.Items = response.Items.OrderBy(item => item.CreatedDateTime).ToList();
            return response;
        }

        public async Task<DeltaResponse> GetUpdatedItems(DateTimeOffset lastSynchronisation, Dictionary<string, string> tokens, bool getAsUser = false)
        {
            var response = await GetDeltaForPublicDrivesAsync(tokens, getAsUser);
            response.Items = response.Items.Where(item => item.Deleted == null && item.CreatedDateTime < lastSynchronisation && item.LastModifiedDateTime >= lastSynchronisation).ToList();
            response.Items = response.Items.Where(item => item.Folder == null).ToList();
            response.Items = response.Items.OrderBy(item => item.LastModifiedDateTime).ToList();
            return response;
        }

        public async Task<DeltaResponse> GetDeletedItems(Dictionary<string, string> tokens, bool getAsUser = false)
        {
            var response = await GetDeltaForPublicDrivesAsync(tokens, getAsUser);
            response.Items = response.Items.Where(item => item.Deleted != null).ToList();
            response.Items = response.Items.Where(item => item.Folder == null).ToList();
            response.Items = response.Items.OrderBy(item => item.LastModifiedDateTime).ToList();
            return response;
        }

        private async Task<DeltaResponse> GetDeltaForPublicDrivesAsync(Dictionary<string, string> tokens, bool getAsUser = false)
        {
            // Get all public drives
            var driveIds = _globalSettings.PublicDriveIds;
            if (driveIds == null || driveIds.Count == 0) throw new CpsException("Drives not found");

            // For each drive:
            // Call graph delta and get changed items since time
            var driveItems = new List<DeltaDriveItem>();
            var deltaLinks = new Dictionary<string, string>();
            foreach (var driveId in driveIds)
            {
                var response = await GetDeltaResponseAsync(driveId, tokens, getAsUser);
                driveItems.AddRange(response.driveItems);
                if (response.deltaLink != null) deltaLinks.Add(driveId, response.deltaLink!);
            }

            // Delta can contain doubles
            driveItems = driveItems.DistinctBy(item => item.DriveId + item.Id).ToList();

            return new DeltaResponse(
                items: driveItems,
                deltaLinks: deltaLinks
            );
        }

        private async Task<(List<DeltaDriveItem> driveItems, string? deltaLink)> GetDeltaResponseAsync(string driveId, Dictionary<string, string> tokens, bool getAsUser = false)
        {
            var driveItems = new List<DeltaDriveItem>();
            GraphDeltaResponse delta;
            try
            {
                var deltaLink = await GetDeltaLinkForDelta(tokens, driveId, getAsUser);
                if (deltaLink == null) throw new CpsException("Error while getting query token");
                delta = await GetDeltaAsync(driveId, deltaLink, getAsUser);
                if (delta.Value == null) throw new CpsException($"Error while getting delta (driveId:{driveId})");
                driveItems.AddRange(delta.Value.Select(i => MapDriveItemToDeltaItem(driveId, i)));

                // Fetch additional pages for delta; we get max of 500 per request by default
                while (delta.OdataNextLink != null)
                {
                    delta = await GetNextPageForDeltaAsync(delta, driveId, getAsUser);
                    if (delta.Value == null) throw new CpsException($"Error while getting next delta (driveId:{driveId},OdataNextLink:{delta.OdataNextLink})");
                    driveItems.AddRange(delta.Value.Select(i => MapDriveItemToDeltaItem(driveId, i)));
                }
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting changed driveItems with delta", ex);
            }

            if (delta.OdataDeltaLink != null) return (driveItems, delta.OdataDeltaLink);
            return (driveItems, null);
        }

        private async Task<string?> GetDeltaLinkForDelta(Dictionary<string, string> tokens, string driveId, bool getAsUser = false)
        {
            var gettingDeltaLinkSucceeded = tokens.TryGetValue(driveId, out var deltaLink);
            if (!gettingDeltaLinkSucceeded)
            {
                return null;
            }

            // Transition to Graph SDK 5 requires an other format for deltaLink.
            // TODO: This validation is only required for the first time using the new graph sdk, after this the code can be deleted.
            if (Uri.TryCreate(deltaLink, UriKind.Absolute, out var outUri)
               && (outUri.Scheme == Uri.UriSchemeHttp || outUri.Scheme == Uri.UriSchemeHttps))
            {
                return deltaLink;
            }

            string rootDriveItemId = await GetRootDriveItemIdAsync(driveId, getAsUser);
            return $"https://graph.microsoft.com/v1.0/drives/{driveId}/items/{rootDriveItemId}/delta()?token={deltaLink}";
        }

        private async Task<GraphDeltaResponse> GetDeltaAsync(string driveId, string? deltaLink = null, bool getAsUser = false)
        {
            string rootDriveItemId = await GetRootDriveItemIdAsync(driveId, getAsUser);

            GraphDeltaResponse? deltaResponse;
            if (string.IsNullOrWhiteSpace(deltaLink))
            {
                // Initial call does not contain a nextLink.
                var graphServiceClient = GetGraphServiceClient(getAsUser);
                deltaResponse = await graphServiceClient.Drives[driveId].Items[rootDriveItemId].Delta.GetAsDeltaGetResponseAsync();
            }
            else
            {
                return await GetDeltaByLinkAsync(driveId, rootDriveItemId, deltaLink, getAsUser);
            }
            if (deltaResponse == null) throw new CpsException($"Error while getting delta (driveId:{driveId},rootDriveItemId:{rootDriveItemId})");
            return deltaResponse;
        }

        private async Task<GraphDeltaResponse> GetNextPageForDeltaAsync(GraphDeltaResponse delta, string driveId, bool getAsUser = false)
        {
            if (string.IsNullOrWhiteSpace(delta.OdataNextLink)) throw new CpsException($"Error while getting next page for delta (driveId:{driveId})");

            string rootDriveItemId = await GetRootDriveItemIdAsync(driveId, getAsUser);

            return await GetDeltaByLinkAsync(driveId, rootDriveItemId, delta.OdataNextLink, getAsUser);
        }

        private async Task<string> GetRootDriveItemIdAsync(string driveId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);

            var rootDriveItem = await graphServiceClient.Drives[driveId].Root.GetAsync();
            if (rootDriveItem == null || string.IsNullOrWhiteSpace(rootDriveItem.Id)) throw new CpsException($"Error while getting root for delta (driveId:{driveId})");
            return rootDriveItem.Id;
        }

        private async Task<GraphDeltaResponse> GetDeltaByLinkAsync(string driveId, string rootDriveItemId, string link, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var deltaResponse = await graphServiceClient.Drives[driveId].Items[rootDriveItemId].Delta.WithUrl(link).GetAsDeltaGetResponseAsync();
            if (deltaResponse == null) throw new CpsException($"Error while getting delta (driveId:{driveId},rootDriveId:{rootDriveItemId},link:{link})");
            return deltaResponse;
        }

        private static DeltaDriveItem MapDriveItemToDeltaItem(string driveId, DriveItem item)
        {
            return new DeltaDriveItem
            {
                Id = item.Id,
                DriveId = driveId,
                Name = item.Name,
                Folder = item.Folder,
                CreatedDateTime = item.CreatedDateTime,
                LastModifiedDateTime = item.LastModifiedDateTime,
                Deleted = item.Deleted
            };
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