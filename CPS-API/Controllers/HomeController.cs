using System.Diagnostics;
using System.Net;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme)]
    public class HomeController : Controller
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly IFilesRepository _filesRepository;

        public HomeController(GraphServiceClient graphServiceClient,
                              IFilesRepository filesRepository)
        {
            _graphServiceClient = graphServiceClient;
            _filesRepository = filesRepository;
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
        [AuthorizeForScopes(Scopes = new[] { "Sites.Read.All", "Files.Read.All" })]
        //[Route("{objectId}/content")]
        public async Task<IActionResult> GetFileURL(string objectId)
        {
            string? fileUrl;
            try
            {
                fileUrl = await _filesRepository.GetUrlAsync(objectId, true);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message ?? $"File not found by objectId ({objectId})");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while getting url");
            }
            if (fileUrl.IsNullOrEmpty()) return StatusCode(500, "Error while getting url");

            return Redirect(fileUrl);
        }

        [HttpGet]
        [Route("metadata/{objectId}")]
        [AuthorizeForScopes(Scopes = new[] { "Sites.Read.All", "Files.Read.All" })]
        //[Route("{objectId}/metadata")]
        public async Task<IActionResult> GetFileMetadata(string objectId)
        {
            FileInformation metadata;
            try
            {
                metadata = await _filesRepository.GetMetadataAsync(objectId, true);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (FileNotFoundException ex)
            {
                return NotFound(ex.Message ?? $"File not found by objectId ({objectId})");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while getting metadata");
            }
            if (metadata == null) return StatusCode(500, "Error while getting metadata");

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