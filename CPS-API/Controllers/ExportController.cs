using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class ExportController : Controller
    {
        // GET
        [HttpGet]
        [Route("new")]
        public async Task<IActionResult> SynchroniseNewDocuments()
        {
            throw new NotImplementedException();
        }

        [HttpGet]
        [Route("updated")]
        public async Task<IActionResult> SynchroniseUpdatedDocuments()
        {
            throw new NotImplementedException();
        }

        [HttpGet]
        [Route("deleted")]
        public async Task<IActionResult> SynchroniseDeletedDocuments()
        {
            throw new NotImplementedException();
        }
    }
}
