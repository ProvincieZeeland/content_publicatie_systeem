using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Repositories;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly IFilesRepository _filesRepository;
        private readonly IMetadataRepository _metadataRepository;
        private readonly TelemetryClient _telemetryClient;

        public FilesController(IFilesRepository filesRepository,
                               TelemetryClient telemetry,
                               IMetadataRepository metadataRepository)
        {
            _filesRepository = filesRepository;
            _telemetryClient = telemetry;
            _metadataRepository = metadataRepository;
        }

        // GET
        [HttpGet]
        [Route("content/{objectId}")]
        public async Task<IActionResult> GetFileURL(string objectId)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            string? fileUrl;
            try
            {
                fileUrl = await _filesRepository.GetUrlAsync(objectId);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Access denied");
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return NotFound(ex.Message ?? $"File not found by objectId ({objectId})");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while getting url");
            }
            if (fileUrl.IsNullOrEmpty())
            {
                _telemetryClient.TrackEvent("Error while getting url", properties);
                return StatusCode(500, "Error while getting url");
            }

            return Ok(fileUrl);
        }

        [HttpGet]
        [Route("metadata/{objectId}")]
        public async Task<IActionResult> GetFileMetadata(string objectId)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            FileInformation metadata;
            try
            {
                metadata = await _metadataRepository.GetMetadataAsync(objectId);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Access denied");
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return NotFound(ex.Message ?? $"File not found by objectId ({objectId})");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while getting metadata");
            }
            if (metadata == null) return StatusCode(500, "Error while getting metadata");

            return Ok(metadata);
        }

        // PUT
        [HttpPut]
        [RequestSizeLimit(419430400)] // 400 MB
        public async Task<IActionResult> CreateFile([FromBody] CpsFile file)
        {
            var fileIsValid = ValidateRequiredPropertiesForFile(file, out var errorMessage);
            if (!fileIsValid)
            {
                return StatusCode(400, errorMessage);
            }

            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = "",
                ["FileName"] = file.Metadata.FileName!,
                ["Source"] = file.Metadata.AdditionalMetadata!.Source,
                ["Classification"] = file.Metadata.AdditionalMetadata!.Classification!
            };

            string? objectId;
            try
            {
                var spoIds = await _filesRepository.CreateFileByBytesAsync(file.Metadata, file.Content);
                objectId = spoIds.ObjectId;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex) when (ex is NameAlreadyExistsException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(409, "Filename already exists");
            }
            catch (ObjectIdAlreadyExistsException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while creating file");
            }
            if (objectId.IsNullOrEmpty()) return StatusCode(500, "Error while creating file");

            return Ok(objectId);
        }

        private static bool ValidateRequiredPropertiesForFile(CpsFile file, out string errorMessage)
        {
            errorMessage = "";
            if (file.Content == null || file.Content.Length == 0)
            {
                errorMessage = "File is required";
                return false;
            }
            else if (file.Metadata == null)
            {
                errorMessage = "File metadata is required";
                return false;
            }
            else if (string.IsNullOrEmpty(file.Metadata.FileName))
            {
                errorMessage = "Filename is required";
                return false;
            }
            else if (file.Metadata.AdditionalMetadata == null)
            {
                errorMessage = "File AdditionalMetadata is required";
                return false;
            }
            else if (string.IsNullOrEmpty(file.Metadata.AdditionalMetadata.Source))
            {
                errorMessage = "Source is required";
                return false;
            }
            else if (string.IsNullOrEmpty(file.Metadata.AdditionalMetadata.Classification))
            {
                errorMessage = "Classification is required";
                return false;
            }
            return true;
        }

        [HttpPut]
        [RequestSizeLimit(5368709120)] // 5 GB
        [Route("new/{source}/{classification}")]
        public async Task<IActionResult> CreateLargeFile(string source, string classification)
        {
            if (Request.Form.Files.Count != 1) return StatusCode(400, "File is required");
            if (string.IsNullOrEmpty(source)) return StatusCode(400, "Source is required");
            if (string.IsNullOrEmpty(classification)) return StatusCode(400, "Classification is required");

            string? objectId;
            var formFile = Request.Form.Files[0];

            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = "",
                ["FileName"] = formFile.FileName,
                ["Source"] = source,
                ["Classification"] = classification
            };

            try
            {
                var spoIds = await _filesRepository.CreateLargeFileAsync(source, classification, formFile);
                objectId = spoIds.ObjectId;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex) when (ex is NameAlreadyExistsException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(409, "Filename already exists");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while creating large file");
            }
            if (objectId.IsNullOrEmpty()) return StatusCode(500, "Error while creating large file");

            return Ok(objectId);
        }

        // POST
        [HttpPut]
        [RequestSizeLimit(419430400)] // 400 MB
        [Route("content/{objectId}")]
        public async Task<IActionResult> UpdateFileContent(string objectId, [FromBody] byte[] content)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            try
            {
                await _filesRepository.UpdateContentAsync(objectId, content);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return NotFound(ex.Message ?? $"File not found by objectId ({objectId})");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while updating content");
            }

            return Ok(objectId);
        }

        [HttpPut]
        [RequestSizeLimit(5368709120)] // 5 GB
        [Route("largeContent/{objectId}")]
        public async Task<IActionResult> UpdateFileContent(string objectId)
        {
            if (Request.Form.Files.Count != 1) return StatusCode(400, "File is required");
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            try
            {
                var formFile = Request.Form.Files[0];
                await _filesRepository.UpdateContentAsync(objectId, formFile);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return NotFound(ex.Message ?? $"File not found by objectId ({objectId})");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while updating content");
            }

            return Ok(objectId);
        }

        [HttpPut]
        [Route("metadata/{objectId}")]
        public async Task<IActionResult> UpdateFileMetadata(string objectId, [FromBody] FileInformation fileInfo)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            if (fileInfo.Ids == null)
            {
                fileInfo.Ids = new ObjectIdentifiers();
            }
            fileInfo.Ids.ObjectId = objectId;

            try
            {
                await _metadataRepository.UpdateAllMetadataAsync(fileInfo, ignoreRequiredFields: true);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return NotFound(ex.Message ?? $"File not found by objectId ({objectId})");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (ObjectIdAlreadyExistsException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while updating metadata");
            }

            return Ok(objectId);
        }

        [HttpPut]
        [Route("filename/{objectId}")]
        public async Task<IActionResult> UpdateFileName(string objectId, [FromBody] FileNameData data)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            try
            {
                await _metadataRepository.UpdateFileName(objectId, data.FileName);
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (FileNotFoundException ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return NotFound(ex.Message ?? $"File not found by objectId ({objectId})");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex, properties);
                return StatusCode(500, ex.Message ?? "Error while updating fileName");
            }

            return Ok();
        }
    }
}