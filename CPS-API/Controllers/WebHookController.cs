using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("[controller]")]
    [ApiController]
    public class WebHookController : Controller
    {
        private readonly IDriveRepository _driveRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly IWebHookRepository _webHookRepository;

        public WebHookController(
            IDriveRepository driveRepository,
            IOptions<GlobalSettings> settings,
            IWebHookRepository webHookRepository)
        {
            _driveRepository = driveRepository;
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

            Microsoft.Graph.Site site;
            try
            {
                site = await _driveRepository.GetSiteAsync(data.SiteId);
            }
            catch (Exception ex)
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

        [HttpPut]
        [Route("HandleDropOffNotification")]
        public async Task<IActionResult> HandleDropOffNotification(WebHookNotification notification)
        {
            string message;
            try
            {
                message = await _webHookRepository.HandleNotificationAsync(notification);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while handling notification");
            }
            return Ok(message);
        }
    }
}