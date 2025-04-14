using System.Reflection;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Graph;
using Microsoft.Graph.Models.TermStore;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.SharePoint.Client;
using PnP.Core.Model.SharePoint;
using PnP.Framework;
using Constants = CPS_API.Models.Constants;
using CopyMigrationOptions = PnP.Core.Model.SharePoint.CopyMigrationOptions;
using GraphSite = Microsoft.Graph.Models.Site;
using Term = CPS_API.Models.Term;

namespace CPS_API.Services
{
    public interface ISharePointRepository
    {
        Task<string?> GetSiteWebUrlAsync(string siteId, bool getAsUser = false);

        Task<GraphSite> GetSiteAsync(string siteId, bool getAsUser = false);

        Task<string> MoveFileAsync(string siteId, string listId, string listItemId, string destinationSiteId, string destinationListId);

        Task<string?> GetNewTermValue(string siteId, PropertyInfo propertyInfo, object? value, FieldMapping fieldMapping, bool isForNewFile, bool ignoreRequiredFields);
    }

    public class SharePointRepository : ISharePointRepository
    {
        private readonly GlobalSettings _globalSettings;
        private readonly IMemoryCache _memoryCache;
        private readonly GraphServiceClient _graphServiceClient;
        private readonly GraphServiceClient _graphAppServiceClient;
        private readonly CertificateService _certificateService;

        public SharePointRepository(
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IMemoryCache memoryCache,
            GraphServiceClient graphServiceClient,
            CertificateService certificateService,
            ITokenAcquisition tokenAcquisition)
        {
            _globalSettings = settings.Value;
            _memoryCache = memoryCache;
            _graphServiceClient = graphServiceClient;
            _graphAppServiceClient = new GraphServiceClient(new BaseBearerTokenAuthenticationProvider(new AppOnlyAuthenticationProvider(tokenAcquisition, settings)));
            _certificateService = certificateService;
        }

        private GraphServiceClient GetGraphServiceClient(bool getAsUser)
        {
            if (getAsUser) return _graphServiceClient;
            return _graphAppServiceClient;
        }

        #region Site 

        public async Task<string?> GetSiteWebUrlAsync(string siteId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var site = await graphServiceClient.Sites[siteId].GetAsync(x =>
            {
                x.QueryParameters.Select = [Constants.SelectWebUrl];
            });
            if (site == null) throw new CpsException($"Error while getting site (siteId:{siteId})");
            return site.WebUrl;
        }

        public async Task<GraphSite> GetSiteAsync(string siteId, bool getAsUser = false)
        {
            var graphServiceClient = GetGraphServiceClient(getAsUser);
            var site = await graphServiceClient.Sites[siteId].GetAsync();
            if (site == null) throw new CpsException($"Error while getting site (siteId:{siteId})");
            return site;
        }

        #endregion

        #region Terms

        public async Task<string?> GetNewTermValue(string siteId, PropertyInfo propertyInfo, object? value, FieldMapping fieldMapping, bool isForNewFile, bool ignoreRequiredFields)
        {
            try
            {
                if (MetadataHelper.KeepExistingValue(value, propertyInfo, isForNewFile, ignoreRequiredFields))
                {
                    return null;
                }

                var defaultValue = MetadataHelper.GetMetadataDefaultValue(value, propertyInfo, fieldMapping, isForNewFile, ignoreRequiredFields);
                if (defaultValue != null)
                {
                    value = defaultValue;
                }
                if (MetadataHelper.IsMetadataFieldEmpty(value, propertyInfo))
                {
                    return null;
                }

                return await GetTermGuidAsync(siteId, fieldMapping.TermsetName, value as string ?? string.Empty);
            }
            catch (FieldRequiredException)
            {
                throw;
            }
            catch (CpsException)
            {
                throw;
            }
            catch
            {
                throw new ArgumentException("Cannot parse received input to valid Sharepoint field data", fieldMapping.FieldName);
            }
        }

        private async Task<string?> GetTermGuidAsync(string siteId, string termsetName, string label)
        {
            // Get term ID
            label = label.ToLower();
            if (!_memoryCache.TryGetValue(Constants.CacheKeyTermsId + termsetName, out List<Term>? cachedTerms))
            {
                var groupId = await GetGroupIdAsync(siteId);
                if (string.IsNullOrEmpty(groupId)) throw new CpsException($"Error while getting group ID for site ({siteId})");

                var sets = await GetSetsAsync(siteId, groupId);
                if (sets.Count == 0) throw new CpsException($"Error while getting sets for group (siteId = {siteId}, groupId = {groupId}, termsetName = {termsetName})");

                foreach (var set in sets)
                {
                    (var setName, var terms) = await GetTermsAsync(siteId, groupId, set);
                    if (setName.ToLower().Equals(termsetName.ToLower())) cachedTerms = terms;
                }
            }
            var term = cachedTerms?.Find(item => string.Equals(item.Label, label.ToLower()));
            return term?.Id;
        }

        private async Task<string?> GetGroupIdAsync(string siteId)
        {
            var groupsResponse = await _graphAppServiceClient.Sites[siteId].TermStore.Groups.GetAsync(x =>
            {
                x.QueryParameters.Filter = $"DisplayName eq '{_globalSettings.TermStoreName}'";
                x.QueryParameters.Select = ["displayName", "id"];
            });
            if (groupsResponse == null || groupsResponse.Value == null || groupsResponse.Value.Count != 1) throw new CpsException($"Error while getting group (siteId={siteId})");
            return groupsResponse.Value[0].Id;
        }

        private async Task<List<Set>> GetSetsAsync(string siteId, string groupId)
        {
            var setsResponse = await _graphAppServiceClient.Sites[siteId].TermStore.Groups[groupId].Sets.GetAsync(x =>
            {
                x.QueryParameters.Select = ["localizedNames", "id"];
            });
            if (setsResponse == null || setsResponse.Value == null) throw new CpsException($"Error while getting set (siteId={siteId}, groupId={groupId})");
            return setsResponse.Value;
        }

        private async Task<(string setName, List<Term> terms)> GetTermsAsync(string siteId, string groupId, Set set)
        {
            if (string.IsNullOrWhiteSpace(set.Id)) throw new CpsException($"Error while getting set ID for group (siteId = {siteId}, groupId = {groupId}");

            var setName = set.LocalizedNames?.FirstOrDefault()?.Name;
            if (string.IsNullOrWhiteSpace(setName)) throw new CpsException($"Error while getting set name for group (siteId = {siteId}, groupId = {groupId}");

            var termsResponse = await _graphAppServiceClient.Sites[siteId].TermStore.Groups[groupId].Sets[set.Id].Terms.GetAsync(x =>
            {
                x.QueryParameters.Select = ["labels", "id"];
            });
            if (termsResponse == null || termsResponse.Value == null) throw new CpsException($"Error while getting terms (siteId={siteId}, setId={set.Id})");
            var terms = termsResponse.Value.Select(item => new Term { Id = item.Id, Label = item.Labels?.Select(l => l.Name).FirstOrDefault()?.ToLower() }).ToList();

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                                    .SetSlidingExpiration(TimeSpan.FromHours(8))
                                    .SetAbsoluteExpiration(TimeSpan.FromHours(8));
            _memoryCache.Set(Constants.CacheKeyTermsId + setName, terms, cacheEntryOptions);
            return (setName, terms);
        }

        #endregion Terms

        public async Task<string> MoveFileAsync(string siteId, string listId, string listItemId, string destinationSiteId, string destinationListId)
        {
            var webUrl = await GetSiteWebUrlAsync(siteId);
            var destinationWebUrl = await GetSiteWebUrlAsync(destinationSiteId);
            if (string.IsNullOrWhiteSpace(destinationWebUrl)) throw new CpsException("Error while getting destination site web url");

            var certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);

            using ClientContext context = await authenticationManager.GetContextAsync(webUrl);
            using ClientContext destinationContext = await authenticationManager.GetContextAsync(destinationWebUrl);

            using var pnpCoreContext = PnPCoreSdk.Instance.GetPnPContext(context);
            using var destinationPnpCoreContext = PnPCoreSdk.Instance.GetPnPContext(destinationContext);

            var rootSiteHostName = new Uri(destinationWebUrl).GetLeftPart(UriPartial.Authority);

            var destinationList = await destinationPnpCoreContext.Web.Lists.GetByIdAsync(new Guid(destinationListId));
            await destinationList.RootFolder.LoadAsync();
            var destinationAbsoluteUrl = $"{rootSiteHostName}{destinationList.RootFolder.ServerRelativeUrl}";

            var list = await pnpCoreContext.Web.Lists.GetByIdAsync(new Guid(listId));
            var listItemParsed = int.TryParse(listItemId, out var listItemIdAsInt);
            if (!listItemParsed) throw new CpsException("Error while moving file");

            var listItem = list.Items.FirstOrDefault(p => p.Id == listItemIdAsInt);
            if (listItem == null) throw new CpsException(nameof(listItem) + " not found");
            await listItem.File.LoadAsync();

            var jobUris = new List<string> { rootSiteHostName + listItem.File.ServerRelativeUrl };
            var copyJobs = await pnpCoreContext.Site.CreateCopyJobsAsync(jobUris.ToArray(), destinationAbsoluteUrl, new CopyMigrationOptions
            {
                IsMoveMode = true,
                BypassSharedLock = true,
                ExcludeChildren = true,
                IgnoreVersionHistory = false,
                NameConflictBehavior = SPMigrationNameConflictBehavior.Fail
            });

            await pnpCoreContext.Site.EnsureCopyJobHasFinishedAsync(copyJobs);

            var file = await destinationPnpCoreContext.Web.GetFileByLinkAsync(destinationAbsoluteUrl + "/" + listItem.File.Name, p => p.VroomItemID);
            return file.VroomItemID;
        }
    }
}