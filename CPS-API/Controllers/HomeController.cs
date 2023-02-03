using System.Diagnostics;
using System.Net;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Identity.Client;
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
            catch (MsalUiRequiredException ex)
            {
                HttpContext.Response.Cookies.Delete($"{CookieAuthenticationDefaults.CookiePrefix}{CookieAuthenticationDefaults.AuthenticationScheme}");
                return Redirect(HttpContext.Request.GetEncodedPathAndQuery());
            }
            catch (Exception ex) when (ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                HttpContext.Response.Cookies.Delete($"{CookieAuthenticationDefaults.CookiePrefix}{CookieAuthenticationDefaults.AuthenticationScheme}");
                return Redirect(HttpContext.Request.GetEncodedPathAndQuery());
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Forbidden"
                };
                return View("Error", viewmodel);
            }
            catch (FileNotFoundException ex)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? $"File not found by objectId ({objectId})"
                };
                return View("Error", viewmodel);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Unauthorized"
                };
                return View("Error", viewmodel);
            }
            catch (Exception ex)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Error while getting url"
                };
                return View("Error", viewmodel);
            }
            if (fileUrl.IsNullOrEmpty())
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = "Error while getting url"
                };
                return View("Error", viewmodel);
            }

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
            catch (MsalUiRequiredException ex)
            {
                HttpContext.Response.Cookies.Delete($"{CookieAuthenticationDefaults.CookiePrefix}{CookieAuthenticationDefaults.AuthenticationScheme}");
                return Redirect(HttpContext.Request.GetEncodedPathAndQuery());
            }
            catch (Exception ex) when (ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                HttpContext.Response.Cookies.Delete($"{CookieAuthenticationDefaults.CookiePrefix}{CookieAuthenticationDefaults.AuthenticationScheme}");
                return Redirect(HttpContext.Request.GetEncodedPathAndQuery());
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Forbidden"
                };
                return View("Error", viewmodel);
            }
            catch (FileNotFoundException ex)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? $"File not found by objectId ({objectId})"
                };
                return View("Error", viewmodel);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Unauthorized"
                };
                return View("Error", viewmodel);
            }
            catch (Exception ex)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Error while getting metadata"
                };
                return View("Error", viewmodel);
            }
            if (metadata == null)
            {
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = "Error while getting metadata"
                };
                return View("Error", viewmodel);
            }

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

        public string ErrorMessage { get; set; }

        public bool ShowErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    }
}