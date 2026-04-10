using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Controllers
{
    [Authorize]
    [Route("[controller]")]
    [ApiController]
    public class ExportController : ControllerBase
    {
        private readonly IExportRepository _exportRepository;
        private readonly ILogger _logger;

        public ExportController(
            IExportRepository exportRepository,
            ILogger<ExportController> logger)
        {
            _exportRepository = exportRepository;
            _logger = logger;
        }

        // GET
        [HttpGet]
        [Route("publish")]
        public async Task<IActionResult> SynchroniseToBePublishedDocuments()
        {
            ToBePublishedExportResponse result;
            try
            {
                result = await _exportRepository.SynchroniseToBePublishedDocumentsAsync();
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, Constants.NewDocumentsSynchronisationError);
            }

            return Ok(GetPublicationResponse(result));
        }

        private static string GetPublicationResponse(ToBePublishedExportResponse result)
        {
            var failedItemsStr = result.FailedItems.Select(id => $"Error while adding file (ObjectId: {id}) to FileStorage.\r\n").ToList();
            var message = String.Join(",", failedItemsStr.Select(x => x.ToString()).ToArray());
            return $"{result.NumberOfSucceededItems} items added" + (failedItemsStr.Count == 0 ? "" : "\r\n") + message;
        }
    }
}