using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Graph;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using CPS_API.Repositories;
using CPS_API.Models;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme)]
    public class HomeController : Controller
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly IFilesRepository _filesRepository;
        private readonly IObjectIdRepository _objectIdRepository;

        public HomeController(GraphServiceClient graphServiceClient,
                              IFilesRepository filesRepository,
                              IObjectIdRepository objectIdRepository)
        {
            _graphServiceClient = graphServiceClient;
            _filesRepository = filesRepository;
            _objectIdRepository = objectIdRepository;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _graphServiceClient.Sites.Root.Request().GetAsync();
            ViewData["ApiResult"] = user.DisplayName;

            return View();
        }


        // GET
        [HttpGet]
        [Route("content/{objectId}")]
        //[Route("{objectId}/content")]
        public async Task<IActionResult> GetFileURL(string objectId)
        {
            string? fileUrl;
            try
            {
                fileUrl = await _filesRepository.GetUrlAsync(objectId);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message ?? "Url not found!");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while getting url");
            }
            if (string.IsNullOrEmpty(fileUrl)) return NotFound("Url not found");

            return Ok(fileUrl);
        }

        [HttpGet]
        [Route("metadata/{objectId}")]
        //[Route("{objectId}/metadata")]
        public async Task<IActionResult> GetFileMetadata(string objectId)
        {
            FileInformation metadata;
            try
            {
                metadata = await _filesRepository.GetMetadataAsync(objectId);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message ?? "Url not found!");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while getting metadata");
            }
            if (metadata == null) return NotFound("Metadata not found");

            return Ok(metadata);
        }


        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }

    public class ErrorViewModel
    {
        public string RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
