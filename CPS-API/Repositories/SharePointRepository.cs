using System.Reflection;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.SharePoint.Client;
using PnP.Core.Model.SharePoint;
using PnP.Framework;
using Constants = CPS_API.Models.Constants;
using CopyMigrationOptions = PnP.Core.Model.SharePoint.CopyMigrationOptions;
using GraphSite = Microsoft.Graph.Models.Site;

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
            if (!_memoryCache.TryGetValue(Constants.CacheKeyTermId + siteId + termsetName + label, out string? termId))
            {
                var groupId = await GetGroupId(siteId);
                if (string.IsNullOrEmpty(groupId)) throw new CpsException($"Error while getting group ID for site ({siteId})");

                var setId = await GetSetId(siteId, groupId, termsetName);
                if (string.IsNullOrEmpty(setId)) throw new CpsException($"Error while getting set ID for group (siteId = {siteId}, groupId = {groupId}, termsetName = {termsetName})");

                var termsResponse = await _graphAppServiceClient.Sites[siteId].TermStore.Groups[groupId].Sets[setId].Terms.GetAsync(x =>
                {
                    x.QueryParameters.Filter = $"labels/any(s:tolower(s/name) eq '{label}')";
                    x.QueryParameters.Select = ["labels", "id"];
                });
                if (termsResponse == null || termsResponse.Value == null || termsResponse.Value.Count != 1) throw new CpsException($"Error while getting terms (siteId={siteId}, setId={setId}, label={label})");
                termId = termsResponse.Value[0].Id;

                _memoryCache.Set(Constants.CacheKeyTermId + siteId + termsetName + label, termId);
            }
            return termId;
        }

        private async Task<string?> GetGroupId(string siteId)
        {
            if (!_memoryCache.TryGetValue(Constants.CacheKeyTermsGroupId + siteId, out string? groupId))
            {
                var groupsResponse = await _graphAppServiceClient.Sites[siteId].TermStore.Groups.GetAsync(x =>
                {
                    x.QueryParameters.Filter = $"DisplayName eq '{_globalSettings.TermStoreName}'";
                    x.QueryParameters.Select = ["displayName", "id"];
                });
                if (groupsResponse == null || groupsResponse.Value == null || groupsResponse.Value.Count != 1) throw new CpsException($"Error while getting group (siteId={siteId})");
                groupId = groupsResponse.Value[0].Id;

                _memoryCache.Set(Constants.CacheKeyTermsGroupId + siteId, groupId);
            }
            return groupId;
        }

        private async Task<string?> GetSetId(string siteId, string groupId, string termsetName)
        {
            if (!_memoryCache.TryGetValue(Constants.CacheKeyTermsSetId + siteId + termsetName, out string? setId))
            {
                var setsResponse = await _graphAppServiceClient.Sites[siteId].TermStore.Groups[groupId].Sets.GetAsync(x =>
                {
                    x.QueryParameters.Filter = $"localizedNames/any(s:s/name eq '{termsetName}')";
                    x.QueryParameters.Select = ["localizedNames", "id"];
                });
                if (setsResponse == null || setsResponse.Value == null || setsResponse.Value.Count != 1) throw new CpsException($"Error while getting set (siteId={siteId}, groupId={groupId}, termsetName={termsetName})");
                setId = setsResponse.Value[0].Id;

                _memoryCache.Set(Constants.CacheKeyTermsSetId + siteId + termsetName, setId);
            }
            return setId;
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