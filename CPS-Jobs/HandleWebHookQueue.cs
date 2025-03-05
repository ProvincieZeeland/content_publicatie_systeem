using System.Threading.Tasks;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CPS_Jobs
{
    public class HandleWebHookQueue
    {
        private readonly ILogger<HandleWebHookQueue> _logger;
        private readonly GlobalSettings _globalSettings;
        private readonly AppService _appService;

        public HandleWebHookQueue(ILogger<HandleWebHookQueue> logger, IOptions<GlobalSettings> config, AppService appService)
        {
            _logger = logger;
            _globalSettings = config.Value;
            _appService = appService;
        }

        [Function("HandleWebHookQueue")]
        public async Task Run(
            [QueueTrigger("sharepointlistwebhooknotifications")] string myQueueItem)
        {
            _logger.LogInformation("Queue trigger function triggered. Message content: {MyQueueItem}", myQueueItem);

            if (string.IsNullOrEmpty(_globalSettings.Scope)) throw new CpsException("Scope cannot be empty");
            if (string.IsNullOrEmpty(_globalSettings.BaseUrl)) throw new CpsException("BaseUrl cannot be empty");

            var response = await _appService.PutAsync(_globalSettings.BaseUrl, _globalSettings.Scope, "/WebHook/HandleDropOffNotification", myQueueItem);
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
