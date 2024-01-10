using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace CPS_Jobs
{
    public class PublicationFunction
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IConfiguration _configuration;

        public PublicationFunction(ITokenAcquisition tokenAcquisition,
                                   IConfiguration config)
        {
            _tokenAcquisition = tokenAcquisition;
            _configuration = config;
        }

        [FunctionName("PublicationFunction")]
        public async Task Run([TimerTrigger("0 0 0 * * *")] TimerInfo timer, ILogger log)
        {
            log.LogInformation($"CPS Publication Timer trigger function started at: {DateTime.Now}");

            string scope = _configuration.GetValue<string>("Settings:Scope");
            string baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            if (string.IsNullOrEmpty(scope)) throw new Exception("Scope cannot be empty");
            if (string.IsNullOrEmpty(baseUrl)) throw new Exception("BaseUrl cannot be empty");

            await callService(baseUrl, scope, "/Export/new", log);
        }

        private async Task callService(string baseUrl, string scope, string url, ILogger log)
        {
            try
            {
                HttpResponseMessage response;
                string token = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);
                using (var client = new HttpClient())
                {
                    var method = HttpMethod.Get;
                    var request = new HttpRequestMessage(method, baseUrl + url);
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    response = await client.SendAsync(request);
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