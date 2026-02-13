using System.Net;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models.ODataErrors;
using Newtonsoft.Json;

namespace CPS_API.Controllers
{
    [Authorize]
    [Route("[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly IFilesRepository _filesRepository;
        private readonly IMetadataRepository _metadataRepository;
        private readonly ILogger _logger;

        public FilesController(
            IFilesRepository filesRepository,
            ILogger<FilesController> logger,
            IMetadataRepository metadataRepository)
        {
            _filesRepository = filesRepository;
            _logger = logger;
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

            string? fileUrl = null;
            IActionResult? errorResult = await HandleApiExceptionsAsync(
                async () => fileUrl = await _filesRepository.GetUrlAsync(objectId),
                properties,
                notFoundMessage: $"File not found by objectId ({objectId})",
                internalErrorMessage: "Error while getting url"
            );
            if (errorResult != null) return errorResult;

            if (string.IsNullOrEmpty(fileUrl))
            {
                return this.LogAndThrowInternalServerError(_logger, errorMessage: "Error while getting url", properties: properties);
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

            FileInformation? metadata = null;
            IActionResult? errorResult = await HandleApiExceptionsAsync(
                async () => metadata = await _metadataRepository.GetMetadataAsync(objectId),
                properties,
                notFoundMessage: $"File not found by objectId ({objectId})",
                internalErrorMessage: "Error while getting metadata"
            );
            if (errorResult != null) return errorResult;

            if (metadata == null) return StatusCode(500, "Error while getting metadata");

            var settings = new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-ddTHH:mm:sszzz",
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };
            var json = JsonConvert.SerializeObject(metadata, settings);
            return Ok(json);
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

            string? objectId = null;
            IActionResult? errorResult = await HandleApiExceptionsAsync(
                async () =>
                {
                    var spoIds = await _filesRepository.CreateFileByBytesAsync(file.Metadata, file.Content);
                    objectId = spoIds.ObjectId;
                },
                properties,
                conflictMessage: "Filename already exists",
                internalErrorMessage: "Error while creating file",
                objectIdExistsMessage: "ObjectId already exists"
            );
            if (errorResult != null) return errorResult;

            if (string.IsNullOrEmpty(objectId)) return StatusCode(500, "Error while creating file");

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

            string? objectId = null;
            var formFile = Request.Form.Files[0];

            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = "",
                ["FileName"] = formFile.FileName,
                ["Source"] = source,
                ["Classification"] = classification
            };

            IActionResult? errorResult = await HandleApiExceptionsAsync(
                async () =>
                {
                    var spoIds = await _filesRepository.CreateLargeFileAsync(source, classification, formFile);
                    objectId = spoIds.ObjectId;
                },
                properties,
                conflictMessage: "Filename already exists",
                internalErrorMessage: "Error while creating large file"
            );
            if (errorResult != null) return errorResult;

            if (string.IsNullOrEmpty(objectId)) return StatusCode(500, "Error while creating large file");

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

            IActionResult? errorResult = await HandleApiExceptionsAsync(
                async () => await _filesRepository.UpdateContentAsync(objectId, content),
                properties,
                notFoundMessage: $"File not found by objectId ({objectId})",
                internalErrorMessage: "Error while updating content"
            );
            if (errorResult != null) return errorResult;

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

            IActionResult? errorResult = await HandleApiExceptionsAsync(
                async () =>
                {
                    var formFile = Request.Form.Files[0];
                    await _filesRepository.UpdateContentAsync(objectId, formFile);
                },
                properties,
                notFoundMessage: $"File not found by objectId ({objectId})",
                internalErrorMessage: "Error while updating content"
            );
            if (errorResult != null) return errorResult;

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

            IActionResult? errorResult = await HandleApiExceptionsAsync(
                async () => await _metadataRepository.UpdateAllMetadataAsync(fileInfo, ignoreRequiredFields: true),
                properties,
                notFoundMessage: $"File not found by objectId ({objectId})",
                internalErrorMessage: "Error while updating metadata",
                objectIdExistsMessage: "ObjectId already exists"
            );
            if (errorResult != null) return errorResult;

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

            IActionResult? errorResult = await HandleApiExceptionsAsync(
                async () => await _metadataRepository.UpdateFileName(objectId, data.FileName),
                properties,
                notFoundMessage: $"File not found by objectId ({objectId})",
                internalErrorMessage: "Error while updating fileName"
            );
            if (errorResult != null) return errorResult;

            return Ok();
        }

        /// <summary>
        /// Handles common API exception logic and returns an appropriate IActionResult if an error occurs, otherwise null.
        /// </summary>
        private async Task<IActionResult?> HandleApiExceptionsAsync(
            Func<Task> action,
            Dictionary<string, string> properties,
            string? notFoundMessage = null,
            string? conflictMessage = null,
            string? internalErrorMessage = null,
            string? objectIdExistsMessage = null)
        {
            try
            {
                await action();
                return null;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, "Access denied", properties);
            }
            catch (FileNotFoundException ex)
            {
                return this.LogAndThrowNotFound(_logger, ex, notFoundMessage ?? "File not found", properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, properties: properties);
            }
            catch (Exception ex) when (ex is NameAlreadyExistsException)
            {
                return this.LogAndThrowConflict(_logger, ex, conflictMessage ?? "Conflict", properties);
            }
            catch (ObjectIdAlreadyExistsException ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, objectIdExistsMessage ?? "ObjectId already exists", properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, internalErrorMessage ?? "Internal Server Error", properties);
            }
        }
    }
}