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

            string? fileUrl;
            try
            {
                fileUrl = await _filesRepository.GetUrlAsync(objectId);
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, "Access denied", properties);
            }
            catch (FileNotFoundException ex)
            {
                return this.LogAndThrowNotFound(_logger, ex, $"File not found by objectId ({objectId})", properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, args: properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while getting url", properties);
            }
            if (string.IsNullOrEmpty(fileUrl))
            {
                return this.LogAndThrowInternalServerError(_logger, errorMessage: "Error while getting url", args: properties);
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
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, "Access denied", properties);
            }
            catch (FileNotFoundException ex)
            {
                return this.LogAndThrowNotFound(_logger, ex, $"File not found by objectId ({objectId})", properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, args: properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while getting metadata", properties);
            }
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

            string? objectId;
            try
            {
                var spoIds = await _filesRepository.CreateFileByBytesAsync(file.Metadata, file.Content);
                objectId = spoIds.ObjectId;
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, args: properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, args: properties);
            }
            catch (Exception ex) when (ex is NameAlreadyExistsException)
            {
                return this.LogAndThrowConflict(_logger, ex, "Filename already exists", properties);
            }
            catch (ObjectIdAlreadyExistsException ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "ObjectId already exists", properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while creating file", properties);
            }
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
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, args: properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, args: properties);
            }
            catch (Exception ex) when (ex is NameAlreadyExistsException)
            {
                return this.LogAndThrowConflict(_logger, ex, "Filename already exists", properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while creating large file", properties);
            }
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

            try
            {
                await _filesRepository.UpdateContentAsync(objectId, content);
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, args: properties);
            }
            catch (FileNotFoundException ex)
            {
                return this.LogAndThrowNotFound(_logger, ex, $"File not found by objectId ({objectId})", properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, args: properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while updating content", properties);
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
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, args: properties);
            }
            catch (FileNotFoundException ex)
            {
                return this.LogAndThrowNotFound(_logger, ex, $"File not found by objectId ({objectId})", properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, args: properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while updating content", properties);
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
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, args: properties);
            }
            catch (FileNotFoundException ex)
            {
                return this.LogAndThrowNotFound(_logger, ex, $"File not found by objectId ({objectId})", properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, args: properties);
            }
            catch (ObjectIdAlreadyExistsException ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "ObjectId already exists", properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while updating metadata", properties);
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
            catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                return this.LogAndThrowForbidden(_logger, ex, args: properties);
            }
            catch (FileNotFoundException ex)
            {
                return this.LogAndThrowNotFound(_logger, ex, $"File not found by objectId ({objectId})", properties);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return this.LogAndThrowUnauthorized(_logger, ex, args: properties);
            }
            catch (Exception ex)
            {
                return this.LogAndThrowInternalServerError(_logger, ex, "Error while updating fileName", properties);
            }

            return Ok();
        }
    }
}