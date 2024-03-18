using System.Reflection;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.Taxonomy;
using PnP.Framework;
using Constants = CPS_API.Models.Constants;
using FieldTaxonomyValue = Microsoft.SharePoint.Client.Taxonomy.TaxonomyFieldValue;
using GraphSite = Microsoft.Graph.Site;

namespace CPS_API.Services
{
    public interface ISharePointRepository
    {
        Task<GraphSite> GetSiteAsync(string siteId, bool getAsUser = false);

        Task UpdateTermsForMetadataAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateTermsForExternalReferencesAsync(string siteId, string listId, List<ExternalReferenceItem> externalReferenceItems, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);
    }

    public class SharePointRepository : ISharePointRepository
    {
        private readonly GlobalSettings _globalSettings;
        private readonly IMemoryCache _memoryCache;
        private readonly GraphServiceClient _graphClient;
        private readonly CertificateService _certificateService;

        public SharePointRepository(
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IMemoryCache memoryCache,
            GraphServiceClient graphClient,
            CertificateService certificateService)
        {
            _globalSettings = settings.Value;
            _memoryCache = memoryCache;
            _graphClient = graphClient;
            _certificateService = certificateService;
        }

        #region Site 

        public async Task<GraphSite> GetSiteAsync(string siteId, bool getAsUser = false)
        {
            var request = _graphClient.Sites[siteId].Request();
            if (!getAsUser)
            {
                request = request.WithAppOnly();
            }
            return await request.GetAsync();
        }

        #endregion

        #region Terms

        public async Task UpdateTermsForMetadataAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            var site = await GetSiteAsync(metadata.Ids.SiteId, getAsUser);

            // Graph does not support full Term management yet, using PnP for SPO API instead
            var certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);
            using ClientContext context = await authenticationManager.GetContextAsync(site.WebUrl);

            var termStore = GetAllTerms(context, metadata.Ids.SiteId);
            if (termStore == null) throw new CpsException("Term store not found!");

            var newValues = GetNewMetadataTermValues(termStore, metadata, isForNewFile, ignoreRequiredFields);

            // actually update fields
            UpdateTermFields(metadata.Ids.ListId, metadata.Ids.ListItemId, context, newValues);
        }

        private Dictionary<string, FieldTaxonomyValue> GetNewMetadataTermValues(TermGroup termStore, FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            Dictionary<string, FieldTaxonomyValue> newValues = new Dictionary<string, FieldTaxonomyValue>();
            foreach (var fieldMapping in _globalSettings.MetadataMapping)
            {
                if (!MetadataHelper.IsEditFieldAllowed(fieldMapping, isForNewFile, isForTermEdit: true))
                {
                    continue;
                }

                var propertyInfo = MetadataHelper.GetMetadataPropertyInfo(fieldMapping, metadata);
                var value = MetadataHelper.GetMetadataValue(metadata, fieldMapping);

                var newValue = GetNewTermValue(propertyInfo, value, fieldMapping, isForNewFile, ignoreRequiredFields, termStore);
                if (newValue != null)
                {
                    newValues.Add(newValue.Value.Key, newValue.Value.Value);
                }
            }
            return newValues;
        }

        public async Task UpdateTermsForExternalReferencesAsync(string siteId, string listId, List<ExternalReferenceItem> externalReferenceItems, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (listId == null) throw new ArgumentNullException("metadata.ExternalReferences");

            var site = await GetSiteAsync(siteId, getAsUser);

            // Graph does not support full Term management yet, using PnP for SPO API instead
            var certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);
            using ClientContext context = await authenticationManager.GetContextAsync(site.WebUrl);

            var termStore = GetAllTerms(context, siteId);
            if (termStore == null) throw new CpsException("Term store not found!");

            UpdateTermsForExternalReference(context, termStore, listId, externalReferenceItems, isForNewFile, ignoreRequiredFields);
        }

        private void UpdateTermsForExternalReference(ClientContext context, TermGroup termStore, string listId, List<ExternalReferenceItem> externalReferenceItems, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            foreach (var externalReferenceItem in externalReferenceItems)
            {
                var newValues = GetNewExternalReferencesTermValues(termStore, externalReferenceItem.ExternalReference, isForNewFile, ignoreRequiredFields);

                // actually update fields
                UpdateTermFields(listId, externalReferenceItem.ListItem.Id, context, newValues);
            }
        }

        private Dictionary<string, FieldTaxonomyValue> GetNewExternalReferencesTermValues(TermGroup termStore, ExternalReferences externalReference, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            Dictionary<string, FieldTaxonomyValue> newValues = new Dictionary<string, FieldTaxonomyValue>();
            foreach (var fieldMapping in _globalSettings.ExternalReferencesMapping)
            {
                if (!MetadataHelper.IsEditFieldAllowed(fieldMapping, isForNewFile, isForTermEdit: true))
                {
                    continue;
                }

                var propertyInfo = externalReference.GetType().GetProperty(fieldMapping.FieldName);
                if (propertyInfo == null) throw new CpsException($"FieldMapping {fieldMapping.FieldName} not found!");
                var value = externalReference[fieldMapping.FieldName];

                var newValue = GetNewTermValue(propertyInfo, value, fieldMapping, isForNewFile, ignoreRequiredFields, termStore);
                if (newValue != null)
                {
                    newValues.Add(newValue.Value.Key, newValue.Value.Value);
                }
            }
            return newValues;
        }

        private static KeyValuePair<string, FieldTaxonomyValue>? GetNewTermValue(PropertyInfo propertyInfo, object? value, FieldMapping fieldMapping, bool isForNewFile, bool ignoreRequiredFields, TermGroup termStore)
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

                return GetTerm(termStore, fieldMapping, value);
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

        private static void UpdateTermFields(string listId, string listItemId, ClientContext context, Dictionary<string, FieldTaxonomyValue> newValues)
        {
            var list = context.Web.Lists.GetById(new Guid(listId));
            var listItem = list.GetItemById(listItemId);

            var fields = listItem.ParentList.Fields;
            context.Load(fields);

            foreach (var newTerm in newValues)
            {
                var field = context.CastTo<TaxonomyField>(fields.GetByInternalNameOrTitle(newTerm.Key));
                context.Load(field);
                field.SetFieldValueByValue(listItem, newTerm.Value);
            }
            listItem.Update();
            context.ExecuteQuery();
        }

        private static KeyValuePair<string, FieldTaxonomyValue> GetTerm(TermGroup termStore, FieldMapping fieldMapping, object? value)
        {
            if (value == null)
            {
                throw new CpsException("Term not found for empty value, fieldName = " + fieldMapping.FieldName);
            }

            var mappedTermSet = termStore.TermSets.Where(s => s.Name.Equals(fieldMapping.TermsetName, StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
            if (mappedTermSet == null)
            {
                throw new CpsException("Termset not found by name " + fieldMapping.TermsetName);
            }

            var mappedTerm = mappedTermSet.Terms.Where(t => t.Name.Equals(value.ToString(), StringComparison.InvariantCultureIgnoreCase)).FirstOrDefault();
            if (mappedTerm == null)
            {
                throw new CpsException("Term not found by value " + value.ToString());
            }

            var termValue = new FieldTaxonomyValue
            {
                TermGuid = mappedTerm.Id.ToString(),
                Label = mappedTerm.Name,
                WssId = -1
            };
            return new KeyValuePair<string, FieldTaxonomyValue>(fieldMapping.SpoColumnName, termValue);
        }

        private TermGroup? GetAllTerms(ClientContext context, string siteId)
        {
            if (!_memoryCache.TryGetValue(Constants.CacheKeyTermGroup + siteId, out TermGroup cacheValue))
            {
                var taxonomySession = TaxonomySession.GetTaxonomySession(context);
                var termStore = taxonomySession.GetDefaultSiteCollectionTermStore();
                if (termStore == null) return null;

                var name = _globalSettings.TermStoreName;
                context.Load(termStore,
                                store => store.Name,
                                store => store.Groups.Where(g => g.Name == name && !g.IsSystemGroup && !g.IsSiteCollectionGroup)
                                    .Include(
                                    group => group.Id,
                                    group => group.Name,
                                    group => group.TermSets.Include(
                                        termSet => termSet.Id,
                                        termSet => termSet.Name,
                                        termSet => termSet.Terms.Include(
                                            t => t.Id,
                                            t => t.Name,
                                            t => t.IsDeprecated,
                                            t => t.Labels))));
                context.ExecuteQuery();
                cacheValue = termStore.Groups.FirstOrDefault();

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                                        .SetSlidingExpiration(TimeSpan.FromSeconds(5))
                                        .SetAbsoluteExpiration(TimeSpan.FromHours(1));

                _memoryCache.Set(Constants.CacheKeyTermGroup + siteId, cacheValue, cacheEntryOptions);
            }
            return cacheValue;
        }

        #endregion Terms
    }
}