using System.Net;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;

namespace CPS_API.Controllers
{
    [Authorize]
    [Route("/files/[controller]")]
    [ApiController]
    public class ObjectIdController : ControllerBase
    {
        private readonly IObjectIdRepository _objectIdRepository;
        private readonly ILogger _logger;

        public ObjectIdController(
            IObjectIdRepository objectIdRepository,
            ILogger<ObjectIdController> logger)
        {
            _objectIdRepository = objectIdRepository;
            _logger = logger;
        }

        [HttpPut]
        public async Task<IActionResult> CreateId([FromBody] ObjectIdentifiers ids)
        {
            if (ids == null) return StatusCode(400, "ObjectIdentifiers are required");

            var properties = new Dictionary<string, string>
            {
                ["SiteId"] = ids.SiteId ?? string.Empty,
                ["ListItemId"] = ids.ListItemId ?? string.Empty,
                ["ListId"] = ids.ListId ?? string.Empty,
                ["DriveId"] = ids.DriveId ?? string.Empty,
                ["DriveItemId"] = ids.DriveItemId ?? string.Empty,
                ["AdditionalObjectId"] = ids.AdditionalObjectId ?? string.Empty
            };

            try
            {
                string objectId = await _objectIdRepository.GenerateObjectIdAsync(ids);
                return Ok(objectId);
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, properties: properties);
            }
            catch (FileNotFoundException ex)
            {
                return this.LogAndThrowNotFound(_logger, ex, "File not found with objectIdentifiers", properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, properties: properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while creating objectId", properties);
            }
        }
    }
}