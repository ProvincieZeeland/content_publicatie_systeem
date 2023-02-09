using System.Net;
using CPS_API.Models;
using CPS_API.Repositories;
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

        public ObjectIdController(IObjectIdRepository objectIdRepository)
        {
            _objectIdRepository = objectIdRepository;
        }

        [HttpPut]
        public async Task<IActionResult> CreateId([FromBody] ObjectIdentifiers ids)
        {
            try
            {
                string objectId = await _objectIdRepository.GenerateObjectIdAsync(ids);
                return Ok(objectId);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message ?? $"File not found by objectIdentifiers");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while creating objectId");
            }
        }
    }
}
