using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace CPS_API.Controllers
{
    [Authorize]
    [Route("[controller]")]
    [ApiController]
    public class WebHookController : Controller
    {
        private readonly GlobalSettings _globalSettings;
        private readonly IWebHookRepository _webHookRepository;

        public WebHookController(
            IOptions<GlobalSettings> settings,
            IWebHookRepository webHookRepository)
        {
            _globalSettings = settings.Value;
            _webHookRepository = webHookRepository;
        }

        [HttpPut]
        [Route("Create")]
        public async Task<ActionResult> Create(DropOffType dropOffType)
        {
            if (!_globalSettings.WebHookSettings.CreateEnabled)
            {
                return StatusCode(404);
            }

            SubscriptionModel subscription;
            try
            {
                subscription = await _webHookRepository.CreateWebHookForDropOffAsync(dropOffType);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while creating webhook");
            }
            return Ok(subscription);
        }

        [HttpPost]
        [AllowAnonymous]
        [Route("HandleSharePointNotification")]
        public async Task<IActionResult> HandleSharePointNotification([FromQuery] string? validationToken)
        {
            if (HttpContext.Request.Headers.TryGetValue("ClientState", out var clientStateHeader))
            {
                var clientStateHeaderValue = clientStateHeader.FirstOrDefault() ?? string.Empty;
                if (string.IsNullOrEmpty(clientStateHeaderValue) || !clientStateHeaderValue.Equals(_globalSettings.WebHookSettings.ClientState))
                {
                    return StatusCode(403);
                }
            }
            else
            {
                return StatusCode(403);
            }

            string? message;
            try
            {
                var requestBody = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
                var notificationsResponse = JsonConvert.DeserializeObject<ResponseModel<WebHookNotification>>(requestBody);
                message = await _webHookRepository.HandleSharePointNotificationAsync(validationToken, notificationsResponse);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while handling notification");
            }
            if (message == null)
            {
                return Ok();
            }
            return Ok(message);
        }

        [HttpPut]
        [Route("HandleDropOffNotification")]
        public async Task<IActionResult> HandleDropOffNotification(WebHookNotification notification)
        {
            ListItemsProcessModel processedItems;
            try
            {
                processedItems = await _webHookRepository.HandleDropOffNotificationAsync(notification);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while handling notification");
            }

            var message = "Notification proccesed.";
            message += $" Found {processedItems.processedItemIds.Count + processedItems.notProcessedItemIds.Count} items, ";
            message += $" {processedItems.processedItemIds.Count} items successfully processed, ";
            message += $" {processedItems.notProcessedItemIds.Count} items not processed ({String.Join(", ", processedItems.notProcessedItemIds)})";
            return Ok(message);
        }
    }
}