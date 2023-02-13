using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace CPS_Jobs
{
    public class SynchronisationFunction
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IConfiguration _configuration;

        public SynchronisationFunction(ITokenAcquisition tokenAcquisition, IConfiguration config)
        {
            _tokenAcquisition = tokenAcquisition;
            _configuration = config;
        }


        [FunctionName("SynchronisationFunction")]
        public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, ILogger log)
        {
            log.LogInformation($"CPS Timer trigger function started at: {DateTime.Now}");

            List<Task> tasks = new List<Task>();

            // Start New sync            
            tasks.Add(callService("/Export/new"));

            // Start Update sync  
            tasks.Add(callService("/Export/updated"));

            // Start Delete sync  
            tasks.Add(callService("/Export/deleted"));

            // Wait for all to finish
            await Task.WhenAll(tasks);
        }

        private async Task callService(string url)
        {
            string token = await _tokenAcquisition.GetAccessTokenForAppAsync(_configuration.GetValue<string>("Settings:Scope"));
            string baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            using (var client = new HttpClient())
            {
                var method = HttpMethod.Post;
                var request = new HttpRequestMessage(method, baseUrl + url);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                await client.SendAsync(request);
            }
        }
    }
}
