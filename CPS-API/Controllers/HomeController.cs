﻿using System.Diagnostics;
using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Repositories;
using Microsoft.ApplicationInsights;
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
        private readonly IMetadataRepository _metadataRepository;
        private readonly TelemetryClient _telemetryClient;

        public HomeController(GraphServiceClient graphServiceClient,
                              IFilesRepository filesRepository,
                              TelemetryClient telemetryClient,
                              IMetadataRepository metadataRepository)
        {
            _graphServiceClient = graphServiceClient;
            _filesRepository = filesRepository;
            _telemetryClient = telemetryClient;
            _metadataRepository = metadataRepository;
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
        public async Task<IActionResult> GetFileURL(string objectId)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            string? fileUrl;
            try
            {
                fileUrl = await _filesRepository.GetUrlAsync(objectId, true);
            }
            catch (Exception ex) when (ex is MsalUiRequiredException || ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                HttpContext.Response.Cookies.Delete($"{CookieAuthenticationDefaults.CookiePrefix}{CookieAuthenticationDefaults.AuthenticationScheme}");
                return Redirect(HttpContext.Request.GetEncodedPathAndQuery());
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Forbidden"
                };
                return View("Error", viewmodel);
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? $"File not found by objectId ({objectId})"
                };
                return View("Error", viewmodel);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Unauthorized"
                };
                return View("Error", viewmodel);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Error while getting url"
                };
                return View("Error", viewmodel);
            }
            if (fileUrl.IsNullOrEmpty())
            {
                _telemetryClient.TrackException(new CpsException("File URL is null"), properties);
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
        public async Task<IActionResult> GetFileMetadata(string objectId)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            FileInformation metadata;
            try
            {
                metadata = await _metadataRepository.GetMetadataAsync(objectId, true);
            }
            catch (Exception ex) when (ex is MsalUiRequiredException || ex.InnerException is MsalUiRequiredException || ex.InnerException?.InnerException is MsalUiRequiredException)
            {
                HttpContext.Response.Cookies.Delete($"{CookieAuthenticationDefaults.CookiePrefix}{CookieAuthenticationDefaults.AuthenticationScheme}");
                return Redirect(HttpContext.Request.GetEncodedPathAndQuery());
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Forbidden"
                };
                return View("Error", viewmodel);
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? $"File not found by objectId ({objectId})"
                };
                return View("Error", viewmodel);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Unauthorized"
                };
                return View("Error", viewmodel);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                var viewmodel = new ErrorViewModel
                {
                    RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                    ErrorMessage = ex.Message ?? "Error while getting metadata"
                };
                return View("Error", viewmodel);
            }
            if (metadata == null)
            {
                _telemetryClient.TrackException(new CpsException("Metadata is null"), properties);
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