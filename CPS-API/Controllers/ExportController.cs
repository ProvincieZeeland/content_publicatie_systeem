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
            // Get all new files from known locations
            // For each file:
            // generate xml from metadata
            // upload file to storage container
            // upload xml to storage container
            throw new NotImplementedException();
        }

        [HttpGet]
        [Route("updated")]
        public async Task<IActionResult> SynchroniseUpdatedDocuments()
        {
            // Get all updated files from known locations
            // For each file:
            // generate xml from metadata
            // upload file to storage container
            // upload xml to storage container
            throw new NotImplementedException();
        }

        [HttpGet]
        [Route("deleted")]
        public async Task<IActionResult> SynchroniseDeletedDocuments()
        {
            // Get all deleted files from known locations
            // For each file:
            // delete file from storage container
            // delete xml from storage container
            throw new NotImplementedException();
        }
    }
}
