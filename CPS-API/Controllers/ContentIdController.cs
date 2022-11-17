using CPS_API.Models;
using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Controllers
{
    [Route("/files/[controller]")]
    [ApiController]
    public class ContentIdController : Controller
    {
        [HttpPut]
        public async Task<IActionResult> CreateId([FromBody] ContentIds ids)
        {
            throw new NotImplementedException();
        }
    }
}
