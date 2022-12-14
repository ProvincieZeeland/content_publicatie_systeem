using CPS_API.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class ExportController : Controller
    {
        private readonly IDriveRepository _driveRepository;

        private readonly ISettingsRepository _settingsRepository;

        public ExportController(IDriveRepository driveRepository,
                                ISettingsRepository settingsRepository)
        {
            _driveRepository = driveRepository;
            _settingsRepository = settingsRepository;
        }

        // GET
        [HttpGet]
        [Route("new")]
        public async Task<IActionResult> SynchroniseNewDocuments()
        {
            // Get all new files from known locations
            var startDate = await _settingsRepository.GetLastSynchronisationAsync();
            if (startDate == null) return StatusCode(500);

            var deletedItems = await _driveRepository.GetNewItems(startDate.Value);
            if (deletedItems == null) return StatusCode(500);

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
            var startDate = await _settingsRepository.GetLastSynchronisationAsync();
            if (startDate == null) return StatusCode(500);

            var deletedItems = await _driveRepository.GetUpdatedItems(startDate.Value);
            if (deletedItems == null) return StatusCode(500);

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
            var startDate = await _settingsRepository.GetLastSynchronisationAsync();
            if (startDate == null) return StatusCode(500);

            var deletedItems = await _driveRepository.GetDeletedItems(startDate.Value);
            if (deletedItems == null) return StatusCode(500);

            // For each file:
            // delete file from storage container
            // delete xml from storage container

            throw new NotImplementedException();
        }
    }
}