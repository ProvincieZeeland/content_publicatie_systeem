using System;
using System.Net;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using CPS_API.Repositories;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.ExternalConnectors;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace CPS_API.Controllers
{
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly IFilesRepository _filesRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly TelemetryClient _telemetryClient;

        public FilesController(IFilesRepository filesRepository,
                               IOptions<GlobalSettings> settings,
                               TelemetryClient telemetry)
        {
            _filesRepository = filesRepository;
            _globalSettings = settings.Value;
            _telemetryClient = telemetry;
        }

        // GET
        [HttpGet]
        [Route("content/{objectId}")]
        //[Route("{objectId}/content")]
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
        //[Route("{objectId}/metadata")]
        public async Task<IActionResult> GetFileMetadata(string objectId)
        {
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            FileInformation metadata;
            try
            {
                metadata = await _filesRepository.GetMetadataAsync(objectId);
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
        [RequestSizeLimit(4194304)] // 4 MB
        public async Task<IActionResult> CreateFile([FromBody] CpsFile file)
        {
            if (file.Content == null || file.Content.Length == 0) return StatusCode(400, "File is required");
            if (string.IsNullOrEmpty(file.Metadata?.FileName)) return StatusCode(400, "Filename is required");
            if (string.IsNullOrEmpty(file.Metadata?.AdditionalMetadata?.Source)) return StatusCode(400, "Source is required");
            if (string.IsNullOrEmpty(file.Metadata?.AdditionalMetadata?.Classification)) return StatusCode(400, "Classification is required");


            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = "",
                ["FileName"] = file.Metadata?.FileName,
                ["Source"] = file.Metadata?.AdditionalMetadata?.Source,
                ["Classification"] = file.Metadata?.AdditionalMetadata?.Classification
            };

            string? objectId;
            try
            {
                var spoIds = await _filesRepository.CreateFileAsync(file);
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
                return StatusCode(500, ex.Message ?? "Error while creating file");
            }
            if (objectId.IsNullOrEmpty()) return StatusCode(500, "Error while creating file");

            return Ok(objectId);
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
            var formFile = Request.Form.Files.First();

            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = "",
                ["FileName"] = formFile.FileName,
                ["Source"] = source,
                ["Classification"] = classification
            };

            try
            {
                CpsFile file = new CpsFile
                {
                    Metadata = new FileInformation
                    {
                        FileName = formFile.FileName,
                        AdditionalMetadata = new FileMetadata()
                    }
                };
                foreach (var fieldMapping in _globalSettings.MetadataMapping)
                {
                    if (fieldMapping.DefaultValue != null)
                    {
                        var defaultAsStr = fieldMapping.DefaultValue?.ToString();
                        if (!defaultAsStr.IsNullOrEmpty())
                        {
                            if (fieldMapping.FieldName == nameof(file.Metadata.SourceCreatedOn) || fieldMapping.FieldName == nameof(file.Metadata.SourceCreatedBy) || fieldMapping.FieldName == nameof(file.Metadata.SourceModifiedOn) || fieldMapping.FieldName == nameof(file.Metadata.SourceModifiedBy) || fieldMapping.FieldName == nameof(file.Metadata.MimeType) || fieldMapping.FieldName == nameof(file.Metadata.FileExtension))
                            {
                                file.Metadata[fieldMapping.FieldName] = fieldMapping.DefaultValue;
                            }
                            else
                            {
                                file.Metadata.AdditionalMetadata[fieldMapping.FieldName] = fieldMapping.DefaultValue;
                            }
                        }
                    }
                }
                file.Metadata.AdditionalMetadata.Source = source;
                file.Metadata.AdditionalMetadata.Classification = classification;

                var spoIds = await _filesRepository.CreateFileAsync(file, formFile);
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
        [RequestSizeLimit(4194304)] // 4 MB
        [Route("content/{objectId}")]
        //[Route("{objectId}/content")]
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

            return Ok();
        }


        [HttpPut]
        [RequestSizeLimit(5368709120)] // 5 GB
        [Route("largeContent/{objectId}")]
        //[Route("{objectId}/content")]
        public async Task<IActionResult> UpdateFileContent(string objectId)
        {
            if (Request.Form.Files.Count != 1) return StatusCode(400, "File is required");
            var properties = new Dictionary<string, string>
            {
                ["ObjectId"] = objectId
            };

            try
            {
                var formFile = Request.Form.Files.First();
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

            return Ok();
        }

        [HttpPut]
        [Route("metadata/{objectId}")]
        //[Route("{objectId}/metadata")]
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
                await _filesRepository.UpdateMetadataAsync(fileInfo);
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
                return StatusCode(500, ex.Message ?? "Error while updating metadata");
            }

            return Ok();
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
                await _filesRepository.UpdateFileName(objectId, data.FileName);
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
