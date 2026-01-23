using System.Diagnostics;
using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Newtonsoft.Json;
using Constants = CPS_API.Models.Constants;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = OpenIdConnectDefaults.AuthenticationScheme)]
    public class HomeController : Controller
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly IFilesRepository _filesRepository;
        private readonly IMetadataRepository _metadataRepository;
        private readonly ILogger _logger;

        public HomeController(
            GraphServiceClient graphServiceClient,
            IFilesRepository filesRepository,
            ILogger<HomeController> logger,
            IMetadataRepository metadataRepository)
        {
            _graphServiceClient = graphServiceClient;
            _filesRepository = filesRepository;
            _logger = logger;
            _metadataRepository = metadataRepository;
        }

        public async Task<IActionResult> Index()
        {
            var site = await _graphServiceClient.Sites["root"].GetAsync();
            if (site == null) throw new CpsException("Error while getting site");
            ViewData["ApiResult"] = site.DisplayName;
            return View();
        }

        // GET
        [HttpGet]
        [Route("content/{objectId}")]
        [AuthorizeForScopes(Scopes = new[] { "Sites.Read.All", "Files.Read.All" })]
        public async Task<IActionResult> GetFileURL(string objectId)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            string? fileUrl = null;
            IActionResult? errorResult = await HandleWithErrorViewAsync(
                async () => fileUrl = await _filesRepository.GetUrlAsync(objectId, true),
                properties,
                "Error while getting url"
            );
            if (errorResult != null) return errorResult;

            if (string.IsNullOrEmpty(fileUrl))
            {
                _logger.LogError("File URL is null | {Properties}", properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = "Error while getting url"
                };
                return View(Constants.ErrorView, viewmodel);
            }

            return Redirect(fileUrl);
        }

        [HttpGet]
        [Route("metadata/{objectId}")]
        [AuthorizeForScopes(Scopes = new[] { "Sites.Read.All", "Files.Read.All" })]
        public async Task<IActionResult> GetFileMetadata(string objectId)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            FileInformation? metadata = null;
            IActionResult? errorResult = await HandleWithErrorViewAsync(
                async () => metadata = await _metadataRepository.GetMetadataAsync(objectId, true),
                properties,
                "Error while getting metadata"
            );
            if (errorResult != null)
                return errorResult;

            if (metadata == null)
            {
                _logger.LogError("Metadata is null | {Properties}", properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = "Error while getting metadata"
                };
                return View(Constants.ErrorView, viewmodel);
            }

            var settings = new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-ddTHH:mm:sszzz",
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };
            var json = JsonConvert.SerializeObject(metadata, settings);
            return Ok(json);
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        /// <summary>
        /// Handles exceptions and returns an error view if needed, otherwise null.
        /// </summary>
        private async Task<IActionResult?> HandleWithErrorViewAsync(
            Func<Task> action,
            Dictionary<string, string> properties,
            string defaultErrorMessage)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception ex) when (ex is MsalUiRequiredException || ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                HttpContext.Response.Cookies.Delete($"{CookieAuthenticationDefaults.CookiePrefix}{CookieAuthenticationDefaults.AuthenticationScheme}");
                return Redirect(HttpContext.Request.GetEncodedPathAndQuery());
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                var errorMessage = "Forbidden";
                _logger.LogError(ex, Constants.ErrorMessagePropertiesFormatString, errorMessage, properties);
                return View(Constants.ErrorView, new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? errorMessage
                });
            }
            catch (FileNotFoundException ex)
            {
                var errorMessage = $"File not found by objectId ({properties.GetValueOrDefault("ObjectId")})";
                _logger.LogError(ex, Constants.ErrorMessagePropertiesFormatString, errorMessage, properties);
                return View(Constants.ErrorView, new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? errorMessage
                });
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                var errorMessage = "Unauthorized";
                _logger.LogError(ex, Constants.ErrorMessagePropertiesFormatString, errorMessage, properties);
                return View(Constants.ErrorView, new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? errorMessage
                });
            }
            catch (Exception ex)
            {
                var errorMessage = defaultErrorMessage;
                _logger.LogError(ex, Constants.ErrorMessagePropertiesFormatString, errorMessage, properties);
                return View(Constants.ErrorView, new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? errorMessage
                });
            }
        }
    }

    public class ErrorViewModel
    {
        public string RequestId { get; set; } = string.Empty;

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public string ErrorMessage { get; set; } = string.Empty;

        public bool ShowErrorMessage
        {
            get
            {
#if DEBUG
                return !string.IsNullOrEmpty(ErrorMessage);
#else
                return false;
#endif
            }
        }
    }
}