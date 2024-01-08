using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.Taxonomy;
using PnP.Framework;
using ChangeType = Microsoft.SharePoint.Client.ChangeType;
using CPSConstants = CPS_API.Models.Constants;
using FieldTaxonomyValue = Microsoft.SharePoint.Client.Taxonomy.TaxonomyFieldValue;

namespace CPS_API.Services
{
    public interface ISharePointRepository
    {
        Task UpdateTermsForMetadataAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task UpdateTermsForExternalReferencesAsync(FileInformation metadata, List<string> listItemIds, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false);

        Task<SharePointListItemsDelta> GetListAndFilteredChangesAsync(string siteUrl, string listId, string changeToken);
    }

    public class SharePointRepository : ISharePointRepository
    {
        private readonly GlobalSettings _globalSettings;
        private readonly IListRepository _listRepository;
        private readonly IMemoryCache _memoryCache;

        public SharePointRepository(
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            IListRepository listRepository,
            IMemoryCache memoryCache)
        {
            _globalSettings = settings.Value;
            _listRepository = listRepository;
            _memoryCache = memoryCache;
        }

        #region Terms

        public async Task UpdateTermsForMetadataAsync(FileInformation metadata, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            var site = await _listRepository.GetSiteAsync(metadata.Ids.SiteId, getAsUser);

            // Graph does not support full Term management yet, using PnP for SPO API instead
            using var authenticationManager = new AuthenticationManager(_globalSettings.ClientId, StoreName.My, StoreLocation.CurrentUser, _globalSettings.CertificateThumbprint, _globalSettings.TenantId);
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

                var propertyInfo = MetadataHelper.GetMetadataPropertyInfo(metadata, fieldMapping);
                var value = MetadataHelper.GetMetadataValue(metadata, fieldMapping);

                var newValue = GetNewTermValue(propertyInfo, value, fieldMapping, isForNewFile, ignoreRequiredFields, termStore);
                if (newValue != null)
                {
                    newValues.Add(newValue.Value.Key, newValue.Value.Value);
                }
            }
            return newValues;
        }

        public async Task UpdateTermsForExternalReferencesAsync(FileInformation metadata, List<string> listItemIds, bool isForNewFile = false, bool ignoreRequiredFields = false, bool getAsUser = false)
        {
            if (metadata.ExternalReferences == null) throw new ArgumentNullException("metadata.ExternalReferences");

            var site = await _listRepository.GetSiteAsync(metadata.Ids.SiteId, getAsUser);

            // Graph does not support full Term management yet, using PnP for SPO API instead
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, StoreName.My, StoreLocation.CurrentUser, _globalSettings.CertificateThumbprint, _globalSettings.TenantId);
            using ClientContext context = await authenticationManager.GetContextAsync(site.WebUrl);

            var termStore = GetAllTerms(context, metadata.Ids.SiteId);
            if (termStore == null) throw new CpsException("Term store not found!");

            UpdateTermsForExternalReference(context, termStore, metadata, listItemIds, isForNewFile, ignoreRequiredFields);
        }

        private void UpdateTermsForExternalReference(ClientContext context, TermGroup termStore, FileInformation metadata, List<string> listItemIds, bool isForNewFile = false, bool ignoreRequiredFields = false)
        {
            var i = 0;
            var listId = metadata.Ids.ExternalReferenceListId;
            foreach (var externalReference in metadata.ExternalReferences)
            {
                var newValues = GetNewExternalReferencesTermValues(termStore, externalReference, isForNewFile, ignoreRequiredFields);

                // actually update fields
                UpdateTermFields(listId, listItemIds[i], context, newValues);
                i++;
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
            var list = GetList(context, listId);
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

            var mappedTermSet = termStore.TermSets.Where(s => s.Name == fieldMapping.TermsetName).FirstOrDefault();
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
            if (!_memoryCache.TryGetValue(CPSConstants.CacheKeyTermGroup + siteId, out TermGroup cacheValue))
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

                _memoryCache.Set(CPSConstants.CacheKeyTermGroup + siteId, cacheValue, cacheEntryOptions);
            }
            return cacheValue;
        }

        #endregion Terms

        #region List

        private static async Task<List> GetAndLoadListAsync(ClientContext context, string listId)
        {
            var list = GetList(context, listId);
            context.Load(list);
            await context.ExecuteQueryRetryAsync(1);
            return list;
        }

        private static List GetList(ClientContext context, string listId)
        {
            return context.Web.Lists.GetById(new Guid(listId));
        }

        #endregion List

        #region List changes

        public async Task<SharePointListItemsDelta> GetListAndFilteredChangesAsync(string siteUrl, string listId, string changeToken)
        {
            using var authenticationManager = new AuthenticationManager(_globalSettings.ClientId, StoreName.My, StoreLocation.CurrentUser, _globalSettings.CertificateThumbprint, _globalSettings.TenantId);
            using ClientContext context = await authenticationManager.GetContextAsync(siteUrl);

            // Get list
            var list = await GetAndLoadListAsync(context, listId);

            // Get changes on list
            var changes = await GetDeltaListItemsAndLastChangeToken(context, list, changeToken);

            // Get correct and unique changes
            return FilterChangesOnDeletedAndUnique(changes);
        }

        private async Task<SharePointListItemsDelta> GetDeltaListItemsAndLastChangeToken(ClientContext context, List list, string changeToken)
        {
            ChangeCollection changeCollection;
            var changes = new SharePointListItemsDelta
            {
                Items = new List<SharePointListItemDelta>(),
                NewChangeToken = changeToken
            };
            do
            {
                changeCollection = await GetListChangesAsync(context, list, changes.NewChangeToken);
                changes.Items.AddRange(getDeltaListItems(changeCollection));
                changes.NewChangeToken = changeCollection.LastChangeToken.StringValue;
            }
            while (changeCollection.HasMoreChanges);
            return changes;
        }

        private async Task<ChangeCollection> GetListChangesAsync(ClientContext context, List list, string lastChangeToken)
        {
            ChangeQuery query = new ChangeQuery(false, false)
            {
                Add = true,
                Item = true,
                Update = true,
                Move = true,
                DeleteObject = true,
                SystemUpdate = true
            };
            if (!string.IsNullOrEmpty(lastChangeToken))
            {
                query.ChangeTokenStart = new ChangeToken
                {
                    StringValue = lastChangeToken
                };
            }

            var changeCollection = list.GetChanges(query);
            context.Load(
                changeCollection,
                cc => cc.LastChangeToken,
                cc => cc.HasMoreChanges,
                cc => cc.Include(
                    c => ((ChangeItem)c).ItemId,
                    c => ((ChangeItem)c).UniqueId,
                    c => ((ChangeItem)c).ContentTypeId,
                    c => ((ChangeItem)c).ChangeType,
                    c => ((ChangeItem)c).ActivityType,
                    c => ((ChangeItem)c).Editor,
                    c => ((ChangeItem)c).EditorLoginName,
                    c => ((ChangeItem)c).IsRecycleBinOperation
                    ));

            await context.ExecuteQueryRetryAsync(1);
            return changeCollection;
        }

        private List<SharePointListItemDelta> getDeltaListItems(ChangeCollection changes)
        {
            var deltaListItems = new List<SharePointListItemDelta>();
            foreach (var change in changes)
            {
                if (change is ChangeItem changeItem)
                {
                    var deltaListItem = new SharePointListItemDelta(changeItem.ItemId, changeItem.ChangeType);
                    deltaListItems.Add(deltaListItem);
                }
            }
            return deltaListItems;
        }

        private static SharePointListItemsDelta FilterChangesOnDeletedAndUnique(SharePointListItemsDelta changes)
        {
            var deletedItemIds = changes.Items.Where(d => d.ChangeType == ChangeType.DeleteObject).DistinctBy(d => d.ListItemId).Select(d => d.ListItemId).ToList();
            var uniqueItems = changes.Items.DistinctBy(d => d.ListItemId).ToList();
            changes.Items = uniqueItems.Where(d => !deletedItemIds.Contains(d.ListItemId)).ToList();
            return changes;
        }

        #endregion List changes
    }
}