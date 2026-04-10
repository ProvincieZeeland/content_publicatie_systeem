using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_Jobs.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GlobalSettings = CPS_Jobs.Models.GlobalSettings;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace CPS_Jobs
{
    public class WebhooksFunction
    {
        private readonly ILogger<WebhooksFunction> _logger;
        private readonly GlobalSettings _globalSettings;
        private readonly AppService _appService;

        public WebhooksFunction(ILogger<WebhooksFunction> logger, IOptions<GlobalSettings> config, AppService appService)
        {
            _logger = logger;
            _globalSettings = config.Value;
            _appService = appService;
        }

        [Function("receive")]
        public async Task<IActionResult> ReceiveNotification([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req, [FromQuery] string validationToken)
        {
            _logger.LogInformation("SPO webhook called endpoint");
            if (req.Headers.TryGetValue("ClientState", out var clientStateHeader))
            {
                var clientStateHeaderValue = clientStateHeader.FirstOrDefault() ?? string.Empty;
                if (string.IsNullOrEmpty(clientStateHeaderValue) || !clientStateHeaderValue.Equals(_globalSettings.ClientState))
                {
                    return new UnauthorizedObjectResult(403);
                }
            }
            else
            {
                return new UnauthorizedObjectResult(403);
            }

            try
            {
                // If a validation token is present, we need to respond within 5 seconds by returning the given validation token.
                // This only happens when a new webhook is being added
                if (!string.IsNullOrEmpty(validationToken))
                {
                    _logger.LogInformation("Validation token {ValidationToken} received", validationToken);
                    return new OkObjectResult(validationToken);
                }
                else
                {
                    string requestBody;
                    try
                    {
                        requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to read request body");
                        throw new InvalidOperationException("Error occurred while reading the webhook request body.", ex);
                    }

                    if (!string.IsNullOrWhiteSpace(requestBody))
                    {
                        ResponseModel<WebHookNotification>? notificationsResponse = JsonSerializer.Deserialize<ResponseModel<WebHookNotification>>(requestBody);
                        await HandleSharePointNotificationAsync(notificationsResponse);
                        return new OkResult();
                    }
                }
                return new BadRequestObjectResult("Failed to parse body");
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex.Message ?? "Error while handling notification");
            }
        }

        /// <summary>
        /// When adding a webhook respond with given validation token.
        /// 
        /// Webhook sends notification when something changes in the list.
        /// Put this notification on a queue, so response time remains within 5 seconds.
        /// </summary>
        public async Task HandleSharePointNotificationAsync(ResponseModel<WebHookNotification>? notificationsResponse)
        {
            if (notificationsResponse == null)
            {
                _logger.LogInformation("NotificationsResponse from webhook is null");
                return;
            }

            List<WebHookNotification> notifications = notificationsResponse.Value;
            _logger.LogInformation("Webhook: Found {Notifications} notifications", notifications.Count);
            if (notifications.Count > 0)
            {
                foreach (WebHookNotification notification in notifications)
                {
                    //Add every call into the same queue for now; azure function will pick it up and handle accordingly
                    _logger.LogInformation("Adding notification to queue");
                    await AddNotificationToQueueAsync(notification, Constants.Webhook.Queue);
                }
            }
        }

        private async Task AddNotificationToQueueAsync(WebHookNotification notification, string queueName)
        {
            var queue = new QueueClient(Environment.GetEnvironmentVariable("AzureWebJobsStorage"), queueName);
            await queue.CreateIfNotExistsAsync();

            // bug in SDK; https://github.com/Azure/azure-sdk-for-net/issues/10242
            string plainText = JsonSerializer.Serialize(notification);
            byte[] plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);

            _logger.LogInformation("Before adding a message to the queue. Message content: {MessageContent}", plainText);
            await queue.SendMessageAsync(Convert.ToBase64String(plainTextBytes));
            _logger.LogInformation("Message added");
        }

        [Function("handleQueue")]
        public async Task Run(
        [QueueTrigger("sharepointlistwebhooknotifications")] QueueMessage message)
        {
            _logger.LogInformation("Queue trigger function triggered. Message content: {MyQueueItem}", message.MessageText);

            WebHookNotification? notification = JsonSerializer.Deserialize<WebHookNotification>(message.MessageText);
            if (notification == null) throw new CpsException("Message received is not correct object");

            if (string.IsNullOrEmpty(_globalSettings.Scope)) throw new CpsException("Scope cannot be empty");
            if (string.IsNullOrEmpty(_globalSettings.BaseUrl)) throw new CpsException("BaseUrl cannot be empty");

            string body = JsonSerializer.Serialize(notification);
            var response = await _appService.PutAsync(_globalSettings.BaseUrl, _globalSettings.Scope, "/WebHook/HandleWebhookNotificationFromQueue", body);
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
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Queue message not processed");
                    }
                }
                _logger.LogError("Queue message not processed. Content: {ResponseContent}", responseContent);
            }
        }
    }
}
