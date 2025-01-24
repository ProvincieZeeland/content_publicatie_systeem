using System.Threading.Tasks;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CPS_Jobs
{
    public class HandleWebHookQueue
    {
        private readonly ILogger<HandleWebHookQueue> _logger;
        private readonly IConfiguration _configuration;
        private readonly AppService _appService;

        public HandleWebHookQueue(ILogger<HandleWebHookQueue> logger, IConfiguration config, AppService appService)
        {
            _logger = logger;
            _configuration = config;
            _appService = appService;
        }

        [Function("HandleWebHookQueue")]
        public async Task Run(
            [QueueTrigger("sharepointlistwebhooknotifications")] string myQueueItem)
        {
            _logger.LogInformation("Queue trigger function triggered. Message content: {MyQueueItem}", myQueueItem);

            var scope = _configuration.GetValue<string>("Settings:Scope");
            var baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            if (string.IsNullOrEmpty(scope)) throw new CpsException("Scope cannot be empty");
            if (string.IsNullOrEmpty(baseUrl)) throw new CpsException("BaseUrl cannot be empty");

            var response = await _appService.PutAsync(baseUrl, scope, "/WebHook/HandleDropOffNotification", myQueueItem);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Queue message processed");
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
                    catch
                    {
                        _logger.LogError("Queue message not processed.");
                    }
                }
                _logger.LogError("Queue message not processed. Content: {responseContent}", responseContent);
            }
        }
    }
}
