using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace CPS_Jobs
{
    public class HandleWebHookQueue
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IConfiguration _configuration;

        public HandleWebHookQueue(ITokenAcquisition tokenAcquisition,
                                       IConfiguration config)
        {
            _tokenAcquisition = tokenAcquisition;
            _configuration = config;
        }

        [Function("HandleWebHookQueue")]
        public async Task Run(
            [QueueTrigger("sharepointlistwebhooknotifications")] string myQueueItem,
            ILogger log)
        {
            log.LogInformation($"Queue trigger function triggered. Message content: {myQueueItem}");

            var scope = _configuration.GetValue<string>("Settings:Scope");
            var baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            if (string.IsNullOrEmpty(scope)) throw new Exception("Scope cannot be empty");
            if (string.IsNullOrEmpty(baseUrl)) throw new Exception("BaseUrl cannot be empty");

            var response = await callService(baseUrl, scope, "/WebHook/HandleDropOffNotification", log, myQueueItem);
            if (response.IsSuccessStatusCode)
            {
                log.LogInformation($"Queue message processed");
            }
            else
            {
                string responseContent = "";
                if (response.Content != null)
                {
                    try
                    {
                        responseContent = await response.Content.ReadAsStringAsync();
                    }
                    catch { }
                }
                log.LogError($"Queue message not processed. Content: {responseContent}");
            }
        }

        private async Task<HttpResponseMessage> callService(string baseUrl, string scope, string url, ILogger log, string body)
        {
            try
            {
                string token = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);
                using (var client = new HttpClient())
                {
                    var method = HttpMethod.Put;
                    var request = new HttpRequestMessage(method, baseUrl + url);
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    return await client.SendAsync(request);
                }
            }
            catch
            {
                log.LogError("Could not start sync for url " + url);
                throw;
            }
        }
    }
}
