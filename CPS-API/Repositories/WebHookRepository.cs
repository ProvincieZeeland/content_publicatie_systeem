using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CPS_API.Database;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using GraphSite = Microsoft.Graph.Models.Site;
using PnPWebhookSubscription = PnP.Framework.Entities.WebhookSubscription;
using WebHookType = CPS_API.Models.WebhookType;

namespace CPS_API.Repositories
{
    public interface IWebHookRepository
    {
        Task<PnPWebhookSubscription> CreateWebHookAsync(string siteId, string listId, WebHookType webHookType);

        Task<bool> DeleteWebHookAsync(GraphSite site, string listId, string subscriptionId);

        Task<DateTime> ExtendWebHookAsync(string webUrl, string listId, string subscriptionId);
    }

    public class WebHookRepository : IWebHookRepository
    {
        private readonly ISharePointRepository _sharePointRepository;
        private readonly CertificateService _certificateService;
        private readonly GlobalSettings _globalSettings;
        private readonly CpsDbContext _dbContext;
        private readonly IDatabaseHealthService _databaseHealthService;

        public WebHookRepository(ISharePointRepository sharePointRepository, CertificateService certificateService, IOptions<GlobalSettings> settings, CpsDbContext dbContext, IDatabaseHealthService databaseHealthService)//NOSONAR
        {
            _sharePointRepository = sharePointRepository;
            _certificateService = certificateService;
            _globalSettings = settings.Value;
            _dbContext = dbContext;
            _databaseHealthService = databaseHealthService;
        }

        public async Task<PnPWebhookSubscription> CreateWebHookAsync(string siteId, string listId, WebHookType webHookType)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(siteId);
            ArgumentNullException.ThrowIfNullOrWhiteSpace(listId);

            var webUrl = await _sharePointRepository.GetSiteWebUrlAsync(siteId);
            if (string.IsNullOrWhiteSpace(webUrl)) throw new CpsException("Error while getting webhook site");

            string accessToken = await GetAccessTokenAsync(webUrl);
            var subscription = await AddListWebHookAsync(webUrl, listId, _globalSettings.WebHookSettings.EndPoint, accessToken, _globalSettings.WebHookSettings.ClientState);
            if (subscription == null) throw new CpsException("Error while adding webhook");

            // Save expiration date and subscriptionId for extending webhook.
            await UpsertWebhookSubscriptionAsync(subscription.Id, subscription.ExpirationDateTime, webHookType);

            return subscription;
        }

        /// <summary>
        /// This method adds a web hook to a SharePoint list.
        /// Note that you need your webhook endpoint being passed into this method to be up and running and reachable from the internet
        /// </summary>
        /// <param name="siteUrl">Url of the site holding the list</param>
        /// <param name="listId">Id of the list</param>
        /// <param name="webHookEndPoint">Url of the web hook service endpoint (the one that will be called during an event)</param>
        /// <param name="accessToken">Access token to authenticate against SharePoint</param>
        /// <param name="validityInMonths">Optional web hook validity in months, defaults to 3 months, max is 6 months</param>
        /// <returns>subscription ID of the new web hook</returns>
        private static async Task<PnPWebhookSubscription?> AddListWebHookAsync(string siteUrl, string listId, string webHookEndPoint, string accessToken, string webHookClientState, int validityInMonths = 3)
        {
            string? responseString = null;
            using (var httpClient = new HttpClient())
            {
                string baseUrl = siteUrl.TrimEnd('/');
                string safeListId = Uri.EscapeDataString(listId);
                string requestUrl = $"{baseUrl}/_api/web/lists('{safeListId}')/subscriptions";
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                request.Content = new StringContent(JsonConvert.SerializeObject(
                    new PnPWebhookSubscription()
                    {
                        Resource = String.Format("{0}/_api/web/lists('{1}')", siteUrl, listId.ToString()),
                        NotificationUrl = webHookEndPoint,
                        ExpirationDateTime = DateTime.Now.AddMonths(validityInMonths).ToUniversalTime(),
                        ClientState = webHookClientState
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    responseString = await response.Content.ReadAsStringAsync();
                }
                else
                {
                    // Something went wrong...
                    throw new CpsException(await response.Content.ReadAsStringAsync());
                }
            }

            return await Task.Run(() => JsonConvert.DeserializeObject<PnPWebhookSubscription>(responseString));
        }

        public async Task<DateTime> ExtendWebHookAsync(string webUrl, string listId, string subscriptionId)
        {
            if (string.IsNullOrWhiteSpace(webUrl)) throw new CpsException("Error while getting webhook site");
            if (string.IsNullOrWhiteSpace(listId)) throw new CpsException("Error while getting webhook list");
            if (string.IsNullOrWhiteSpace(subscriptionId)) throw new CpsException("Error while getting subscriptionId for webhook");

            string accessToken = await GetAccessTokenAsync(webUrl);
            DateTime expirationDateTime = await UpdateListWebHookAsync(webUrl, listId, subscriptionId, _globalSettings.WebHookSettings.EndPoint, accessToken, _globalSettings.WebHookSettings.ClientState);

            // Save expiration date for renewing webhook.
            await UpsertWebhookSubscriptionAsync(subscriptionId, expirationDateTime);

            return expirationDateTime;
        }

        private async Task<string> GetAccessTokenAsync(string webUrl)
        {
            X509Certificate2? certificate = await _certificateService.GetCertificateAsync();
            using var authenticationManager = new PnP.Framework.AuthenticationManager(_globalSettings.ClientId, certificate, _globalSettings.TenantId);
            string accessToken = await authenticationManager.GetAccessTokenAsync(webUrl);
            if (accessToken == null) throw new CpsException("Error while getting accessToken");
            return accessToken;
        }

        /// <summary>
        /// Updates the expiration datetime (and notification URL) of an existing SharePoint list web hook
        /// </summary>
        /// <param name="siteUrl">Url of the site holding the list</param>
        /// <param name="listId">Id of the list</param>
        /// <param name="subscriptionId">Id of the web hook subscription that we need to update</param>
        /// <param name="webHookEndPoint">Url of the web hook service endpoint (the one that will be called during an event)</param>
        /// <param name="accessToken">Access token to authenticate against SharePoint</param>
        /// <returns>true if succesful, exception in case something went wrong</returns>
        /// <param name="validityInMonths">Optional web hook validity in months, defaults to 3 months, max is 6 months</param>
        private static async Task<DateTime> UpdateListWebHookAsync(string siteUrl, string listId, string subscriptionId, string webHookEndPoint, string accessToken, string webHookClientState, int validityInMonths = 3)
        {
            using (var httpClient = new HttpClient())
            {
                string baseUrl = siteUrl.TrimEnd('/');
                string safeListId = Uri.EscapeDataString(listId);
                string safeSubscriptionId = Uri.EscapeDataString(subscriptionId);
                var uriBuilder = new UriBuilder(baseUrl);
                uriBuilder.Path = $"/_api/web/lists('{safeListId}')/subscriptions('{safeSubscriptionId}')";
                string requestUrl = uriBuilder.ToString();
                HttpRequestMessage request = new HttpRequestMessage(new HttpMethod("PATCH"), requestUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var expirationDateTime = DateTime.Now.AddMonths(validityInMonths).ToUniversalTime();
                request.Content = new StringContent(JsonConvert.SerializeObject(
                    new PnPWebhookSubscription()
                    {
                        ExpirationDateTime = expirationDateTime,
                        NotificationUrl = webHookEndPoint,
                        ClientState = webHookClientState
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await httpClient.SendAsync(request);

                if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
                {
                    // oops...something went wrong, maybe the web hook does not exist?
                    throw new CpsException(await response.Content.ReadAsStringAsync());
                }
                else
                {
                    return await Task.Run(() => expirationDateTime);
                }
            }
        }

        public async Task<bool> DeleteWebHookAsync(GraphSite site, string listId, string subscriptionId)
        {
            if (string.IsNullOrWhiteSpace(site.WebUrl)) throw new CpsException($"Error while deleting webhook: site webUrl unknown");

            string accessToken = await GetAccessTokenAsync(site.WebUrl);
            return await DeleteListWebHookAsync(site.WebUrl, listId, subscriptionId, accessToken);
        }

        /// <summary>
        /// Deletes an existing SharePoint list web hook
        /// </summary>
        /// <param name="siteUrl">Url of the site holding the list</param>
        /// <param name="listId">Id of the list</param>
        /// <param name="subscriptionId">Id of the web hook subscription that we need to delete</param>
        /// <param name="accessToken">Access token to authenticate against SharePoint</param>
        /// <returns>true if succesful, exception in case something went wrong</returns>
        public async Task<bool> DeleteListWebHookAsync(string siteUrl, string listId, string subscriptionId, string accessToken)
        {
            using (var httpClient = new HttpClient())
            {
                string requestUrl = String.Format("{0}/_api/web/lists('{1}')/subscriptions('{2}')", siteUrl, listId, subscriptionId);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Delete, requestUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                HttpResponseMessage response = await httpClient.SendAsync(request);

                if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
                {
                    // oops...something went wrong, maybe the web hook does not exist?
                    throw new CpsException(await response.Content.ReadAsStringAsync());
                }
                else
                {
                    return await Task.Run(() => true);
                }
            }
        }

        public async Task UpsertWebhookSubscriptionAsync(string subscriptionId, DateTime subscriptionExpirationDate, WebhookType? webhookType = null)
        {
            var webhookSubscription = await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.WebhookSubscription.FirstOrDefaultAsync(ws => ws.SubscriptionId.Equals(subscriptionId)),
                nameof(UpsertWebhookSubscriptionAsync)
            );
            if (webhookSubscription == null)
            {
                if (webhookType == null) throw new CpsException("Error while upserting webhook subscription: webhookType is required for new subscription");
                await _dbContext.WebhookSubscription.AddAsync(new WebhookSubscription
                {
                    SubscriptionId = subscriptionId,
                    WebhookType = webhookType.Value,
                    SubscriptionExpirationDateTime = subscriptionExpirationDate,
                });
            }
            else
            {
                webhookSubscription.SubscriptionExpirationDateTime = subscriptionExpirationDate;
            }
            await _databaseHealthService.ExecuteWithWarmupAsync(
                async () => await _dbContext.SaveChangesAsync(),
                nameof(UpsertWebhookSubscriptionAsync)
            );
        }
    }
}