using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Newtonsoft.Json;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("[controller]")]
    [ApiController]
    public class WebHookController : Controller
    {
        private readonly IListRepository _metadataRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IWebHookRepository _webHookRepository;

        public WebHookController(
            IListRepository metadataRepository,
            IOptions<GlobalSettings> settings,
            IWebHookRepository webHookRepository)
        {
            _metadataRepository = metadataRepository;
            _globalSettings = settings.Value;
            _webHookRepository = webHookRepository;
        }

        [HttpPost]
        [Route("Create")]
        public async Task<ActionResult> Create([FromBody] WebHookData data)
        {
            if (!_globalSettings.CreateWebHookEnabled)
            {
                return StatusCode(404);
            }

            Site site;
            try
            {
                site = await _metadataRepository.GetSiteAsync(data.SiteId);
            }
            catch (Exception)
            {
                return StatusCode(500, "Error while getting site");
            }

            SubscriptionModel subscription;
            try
            {
                subscription = await _webHookRepository.CreateWebHookAsync(site, data.ListId);
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
                if (string.IsNullOrEmpty(clientStateHeaderValue) || !clientStateHeaderValue.Equals(_globalSettings.WebHookClientState))
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
            string message;
            try
            {
                message = await _webHookRepository.HandleDropOffNotificationAsync(notification);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while handling notification");
            }
            return Ok(message);
        }
    }
}