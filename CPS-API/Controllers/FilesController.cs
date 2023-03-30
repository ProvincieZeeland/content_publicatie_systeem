using System.Net;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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
        private readonly GlobalSettings _globalSettings;

        public FilesController(IFilesRepository filesRepository,
                               IOptions<GlobalSettings> settings)
        {
            _filesRepository = filesRepository;
            _globalSettings = settings.Value;
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
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(403, ex.Message ?? "Access denied");
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
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(403, ex.Message ?? "Access denied");
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

        // PUT
        [HttpPut]
        public async Task<IActionResult> CreateFile([FromBody] CpsFile file)
        {
            string? objectId;
            try
            {
                var spoIds = await _filesRepository.CreateFileAsync(file);
                objectId = spoIds.ObjectId;
            }
            catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
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
            if (Request.Form.Files.Count != 1) throw new ArgumentException("You must add one file for upload in form data");

            string? objectId;
            var formFile = Request.Form.Files.First();
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
                return StatusCode(403, ex.Message ?? "Forbidden");
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return StatusCode(401, ex.Message ?? "Unauthorized");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message ?? "Error while creating large file");
            }
            if (objectId.IsNullOrEmpty()) return StatusCode(500, "Error while creating large file");

            return Ok(objectId);
        }

        // POST
        [HttpPut]
        [Route("content/{objectId}")]
        //[Route("{objectId}/content")]
        public async Task<IActionResult> UpdateFileContent(string objectId, [FromBody] byte[] content)
        {
            try
            {
                await _filesRepository.UpdateContentAsync(objectId, content);
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
                return StatusCode(500, ex.Message ?? "Error while updating content");
            }

            return Ok();
        }

        [HttpPut]
        [Route("metadata/{objectId}")]
        //[Route("{objectId}/metadata")]
        public async Task<IActionResult> UpdateFileMetadata(string objectId, [FromBody] FileInformation fileInfo)
        {
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
                return StatusCode(500, ex.Message ?? "Error while updating metadata");
            }

            return Ok();
        }

        [HttpPut]
        [Route("filename/{objectId}")]
        public async Task<IActionResult> UpdateFileName(string objectId, [FromBody] FileNameData data)
        {
            try
            {
                await _filesRepository.UpdateFileName(objectId, data.FileName);
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
                return StatusCode(500, ex.Message ?? "Error while updating fileName");
            }

            return Ok();
        }
    }
}
