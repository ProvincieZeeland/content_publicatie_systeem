using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Controllers
{
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
        public async Task<IActionResult> CreateId([FromBody] ObjectIds ids)
        {
            try
            {
                string objectId = await _objectIdRepository.GenerateObjectIdAsync(ids);
                return Ok(objectId);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(401);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
