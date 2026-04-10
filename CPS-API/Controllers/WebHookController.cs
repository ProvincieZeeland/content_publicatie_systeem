using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using CPS_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WebhookSubscription = PnP.Framework.Entities.WebhookSubscription;

namespace CPS_API.Controllers
{
    [Authorize]
    [Route("[controller]")]
    [ApiController]
    public class WebHookController : ControllerBase
    {
        private readonly GlobalSettings _globalSettings;
        private readonly IWebHookRepository _webHookRepository;
        private readonly WebhookNotificationService _webhookNotificationService;
        private readonly ILogger _logger;

        public WebHookController(
            IOptions<GlobalSettings> settings,
            IWebHookRepository webHookRepository,
            WebhookNotificationService webhookNotificationService,
            ILogger<WebHookController> logger)
        {
            _globalSettings = settings.Value;
            _webHookRepository = webHookRepository;
            _webhookNotificationService = webhookNotificationService;
            _logger = logger;
        }

        [HttpPut]
        [Route("Create")]
        public async Task<IActionResult> Create(string siteId, string listId, WebhookType webHookType)
        {
            if (!_globalSettings.WebHookSettings.CreateEnabled)
            {
                return StatusCode(404);
            }

            WebhookSubscription subscription;
            try
            {
                subscription = await _webHookRepository.CreateWebHookAsync(siteId, listId, webHookType);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while creating webhook");
            }
            return Ok(subscription);
        }

        [HttpPut]
        [Route("Extend")]
        public async Task<IActionResult> Extend(string webUrl, string listId, string subscriptionId, string endpoint)
        {
            if (!_globalSettings.WebHookSettings.CreateEnabled)
            {
                return StatusCode(404);
            }

            DateTime expirationDateTime;
            try
            {
                expirationDateTime = await _webHookRepository.ExtendWebHookAsync(webUrl, listId, subscriptionId);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while updating webhook");
            }
            return Ok(expirationDateTime);
        }

        [HttpPut]
        [Route("HandleWebhookNotificationFromQueue")]
        public async Task<IActionResult> HandleWebhookNotificationFromQueue(WebHookNotification notification)
        {
            string message;
            try
            {
                message = await _webhookNotificationService.HandleWebhookNotificationFromQueueAsync(notification);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while handling notification");
            }

            return Ok(message);
        }
    }
}