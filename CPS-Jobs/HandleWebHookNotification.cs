using System.IO;
using System.Threading.Tasks;
using CPS_Jobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace CPS_Jobs
{
    public class HandleWebHookNotification
    {
        private readonly IConfiguration _configuration;

        public HandleWebHookNotification(IConfiguration config)
        {
            _configuration = config;
        }

        [FunctionName("HandleWebHookNotification")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation($"Webhook endpoint triggered!");

            // Grab the validationToken URL parameter
            string validationToken = req.Query["validationtoken"];

            // If a validation token is present, we need to respond within 5 seconds by  
            // returning the given validation token. This only happens when a new 
            // web hook is being added
            if (validationToken != null)
            {
                log.LogInformation($"Validation token {validationToken} received");
                return new OkObjectResult(validationToken);
            }

            log.LogInformation($"SharePoint triggered our webhook");
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            log.LogInformation($"Received following payload: {requestBody}");

            var notifications = JsonConvert.DeserializeObject<ResponseModel<NotificationModel>>(requestBody).Value;
            log.LogInformation($"Found {notifications.Count} notifications");

            if (notifications.Count > 0)
            {
                log.LogInformation($"Processing notifications...");
                foreach (var notification in notifications)
                {
                    await AddNotificationToQueueAsync(log, notification);
                }
            }

            // if we get here we assume the request was well received
            return new OkResult();
        }

        private async Task AddNotificationToQueueAsync(ILogger log, NotificationModel notification)
        {
            var queue = await GetNotificationsQueue();
            var message = JsonConvert.SerializeObject(notification);
            log.LogInformation($"Before adding a message to the queue. Message content: {message}");
            await queue.AddMessageAsync(new CloudQueueMessage(message));
            log.LogInformation($"Message added");
        }

        private async Task<CloudQueue> GetNotificationsQueue()
        {
            var storageTableConnectionstring = _configuration.GetValue<string>("Settings:StorageTableConnectionstring");
            var storageAccount = CloudStorageAccount.Parse(storageTableConnectionstring);
            var queueClient = storageAccount.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference("sharepointlistwebhooknotifications");
            await queue.CreateIfNotExistsAsync();
            return queue;
        }
    }
}
