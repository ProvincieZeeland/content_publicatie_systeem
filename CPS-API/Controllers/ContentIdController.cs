using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Controllers
{
    [Authorize]
    [Route("/files/[controller]")]
    [ApiController]
    public class ContentIdController : Controller
    {
        private readonly IContentIdRepository _contentIdRepository;

        public ContentIdController(IContentIdRepository contentIdRepository)
        {
            _contentIdRepository = contentIdRepository;
        }

        [HttpPut]
        public async Task<IActionResult> CreateId([FromBody] ContentIds ids)
        {
            try
            {
                string contentId = await _contentIdRepository.GenerateContentIdAsync(ids);
                return Ok(contentId);
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
