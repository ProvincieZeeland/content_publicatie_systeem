﻿using System.Net;
using System.Text.RegularExpressions;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace CPS_API.Repositories
{
    public interface IDriveRepository
    {
        Task<Drive> GetDriveAsync(string driveId, bool getAsUser = false);

        Task<Drive> GetDriveAsync(string siteId, string listId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false);

        Task<DriveItem> GetDriveItemIdsAsync(string driveId, string driveItemId, bool getAsUser = false);

        Task<DriveItem?> CreateAsync(string driveId, string fileName, Stream fileStream, bool getAsUser = false);

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
        private readonly GraphServiceClient _graphClient;
        private readonly GlobalSettings _globalSettings;

        public DriveRepository(GraphServiceClient graphClient, IOptions<GlobalSettings> settings)
        {
            _graphClient = graphClient;
            _globalSettings = settings.Value;
        }

        public async Task<Drive> GetDriveAsync(string siteId, string listId, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Lists[listId].Drive.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<Drive> GetDriveAsync(string driveId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId, bool getAsUser = false)
        {
            if (siteId.IsNullOrEmpty())
            {
                throw new CpsException("Error while getting driveItem, unkown SiteId");
            }
            else if (listId.IsNullOrEmpty())
            {
                throw new CpsException("Error while getting driveItem, unkown ListId");
            }
            else if (listItemId.IsNullOrEmpty())
            {
                throw new CpsException("Error while getting driveItem, unkown ListItemId");
            }

            var request = _graphClient.Sites[siteId].Lists[listId].Items[listItemId].DriveItem.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }

            try
            {
                var driveItem = await request.GetAsync();
                if (driveItem == null) throw new FileNotFoundException($"DriveItem (SiteId = {siteId}, ListId = {listId}, ListItemId = {listItemId}) does not exist!");
                return driveItem;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                throw;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"DriveItem (SiteId = {siteId}, ListId = {listId}, ListItemId = {listItemId}) does not exist!");
            }
            catch (Exception ex)
            {
                throw new CpsException("Error while getting driveItem", ex);
            }
        }

        public async Task<DriveItem> GetDriveItemAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Items[driveItemId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        public async Task<DriveItem> GetDriveItemIdsAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Items[driveItemId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.Select("sharepointids").GetAsync();
        }

        public async Task<DriveItem?> CreateAsync(string driveId, string fileName, Stream fileStream, bool getAsUser = false)
        {
            if (fileStream.Length > 0)
            {
                var properties = new DriveItemUploadableProperties() { ODataType = null, AdditionalData = new Dictionary<string, object>() };
                properties.AdditionalData.Add("@microsoft.graph.conflictBehavior", "fail");

                var request = _graphClient.Drives[driveId].Root
                    .ItemWithPath(fileName).CreateUploadSession(properties)
                    .Request();
                if (!getAsUser)
                {
                    request = request.WithAppOnly();
                }
                var uploadSession = await request.PostAsync();

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
                catch (ServiceException ex)
                {
                    throw new CpsException("Failed to upload file.", ex);
                }

                if (driveItem == null)
                {
                    throw new CpsException("Failed to upload file.");
                }

                return driveItem;
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
                var properties = new DriveItemUploadableProperties() { ODataType = null, AdditionalData = new Dictionary<string, object>() };
                properties.AdditionalData.Add("@microsoft.graph.conflictBehavior", "replace");

                var request = _graphClient.Drives[driveId].Items[driveItemId].CreateUploadSession(properties).Request();
                if (!getAsUser)
                {
                    request = request.WithAppOnly();
                }
                var uploadSession = await request.PostAsync();

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
                catch (ServiceException ex)
                {
                    throw new CpsException("Failed to upload file.", ex);
                }

                if (driveItem == null)
                {
                    throw new CpsException("Failed to upload file.");
                }

                return driveItem;
            }
            else
            {
                throw new CpsException("Cannot upload empty file stream.");
            }
        }

        public async Task<DriveItem?> UpdateFileNameAsync(string driveId, string driveItemId, string fileName, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Items[driveItemId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            var driveItem = new DriveItem
            {
                Name = fileName
            };
            return await request.UpdateAsync(driveItem);
        }

        public async Task DeleteFileAsync(string driveId, string driveItemId, bool getAsUser = false)
        {
            var request = _graphClient.Drives[driveId].Items[driveItemId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            await request.DeleteAsync();
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
            response.Items = response.Items.Where(item => item.Deleted == null && item.CreatedDateTime < lastSynchronisation).ToList();
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
            if (driveIds.IsNullOrEmpty()) throw new CpsException("Drives not found");

            // For each drive:
            // Call graph delta and get changed items since time
            var driveItems = new List<DeltaDriveItem>();
            var nextTokens = new Dictionary<string, string>();
            foreach (var driveId in driveIds)
            {
                IDriveItemDeltaCollectionPage delta;
                try
                {
                    var queryOptions = GetDeltaQueryOptions(tokens, driveId);
                    delta = await GetDeltaAsync(driveId, queryOptions, getAsUser);
                    driveItems.AddRange(delta.CurrentPage.Select(i => MapDriveItemToDeltaItem(driveId, i)));

                    // Fetch additional pages for delta; we get max of 500 per request by default
                    while (delta.NextPageRequest != null)
                    {
                        delta = await GetNextPageForDeltaAsync(delta, getAsUser);
                        driveItems.AddRange(delta.CurrentPage.Select(i => MapDriveItemToDeltaItem(driveId, i)));
                    }
                }
                catch (Exception ex)
                {
                    throw new CpsException("Error while getting changed driveItems with delta", ex);
                }

                var nextToken = GetDeltaNextToken(delta);
                if (nextToken != null)
                {
                    nextTokens.Add(driveId, nextToken);
                }
            }

            // Delta can contain doubles
            driveItems = driveItems.DistinctBy(item => item.DriveId + item.Id).ToList();

            return new DeltaResponse(
                items: driveItems,
                nextTokens: nextTokens
            );
        }

        private List<QueryOption>? GetDeltaQueryOptions(Dictionary<string, string> tokens, string driveId)
        {
            var gettingTokenSucceeded = tokens.TryGetValue(driveId, out var token);
            if (!gettingTokenSucceeded)
            {
                return null;
            }
            return new List<QueryOption>()
            {
                new QueryOption("token", token)
            };
        }

        private async Task<IDriveItemDeltaCollectionPage> GetDeltaAsync(string driveId, List<QueryOption> queryOptions, bool getAsUser)
        {
            var request = _graphClient.Drives[driveId].Root.Delta().Request(queryOptions);
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        private static async Task<IDriveItemDeltaCollectionPage> GetNextPageForDeltaAsync(IDriveItemDeltaCollectionPage delta, bool getAsUser)
        {
            var newPageRequest = delta.NextPageRequest;
            if (!getAsUser)
            {
                newPageRequest = delta.NextPageRequest.WithAppOnly();
            }
            return await newPageRequest.GetAsync();
        }

        private static string? GetDeltaNextToken(IDriveItemDeltaCollectionPage delta)
        {
            var nextTokenExists = delta.AdditionalData.TryGetValue("@odata.deltaLink", out var nextTokenCall);
            if (!nextTokenExists)
            {
                return null;
            }
            var pattern = @".*\?token=(.*)";
            var match = Regex.Match(nextTokenCall.ToString(), pattern);
            if (!match.Success || match.Groups.Count < 2)
            {
                return null;
            }
            return match.Groups[1]?.Value;
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
            var request = _graphClient.Drives[driveId].Items[driveItemId].Content.Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }
    }
}