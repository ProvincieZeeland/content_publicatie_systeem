using System.Net;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("/files/[controller]")]
    [ApiController]
    public class ObjectIdController : Controller
    {
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly TelemetryClient _telemetryClient;

        public ObjectIdController(IObjectIdRepository objectIdRepository, TelemetryClient telemetryClient)
        {
            _objectIdRepository = objectIdRepository;
            _telemetryClient = telemetryClient;
        }

        [HttpPut]
        public async Task<IActionResult> CreateId([FromBody] ObjectIdentifiers ids)
        {
            if (ids == null) return StatusCode(400, "ObjectIdentifiers are required");

            var properties = new Dictionary<string, string?>
            {
                ["SiteId"] = ids.SiteId,
                ["ListItemId"] = ids.ListItemId,
                ["ListId"] = ids.ListId,
                ["DriveId"] = ids.DriveId,
                ["DriveItemId"] = ids.DriveItemId,
                ["AdditionalObjectId"] = ids.AdditionalObjectId
            };

            try
            {
                string objectId = await _objectIdRepository.GenerateObjectIdAsync(ids);
                return Ok(objectId);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return NotFound(ex.Message ?? $"File not found with objectIdentifiers");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while creating objectId");
            }
        }
    }
}
