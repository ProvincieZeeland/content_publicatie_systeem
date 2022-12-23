using CPS_API.Models;
using CPS_API.Repositories;
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
        private readonly IObjectIdRepository _objectIdRepository;

        public FilesController(IFilesRepository filesRepository,
                               IObjectIdRepository objectIdRepository)
        {
            _filesRepository = filesRepository;
            _objectIdRepository = objectIdRepository;
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
            if (fileUrl.IsNullOrEmpty()) return NotFound("Url not found");

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
            if (objectId.IsNullOrEmpty()) return NotFound("ObjectId not found");

            return Ok(objectId);
        }

        [HttpPut]
        [RequestSizeLimit(5368709120)] // 5 GB
        [Route("new/{source}/{classification}")]
        public async Task<IActionResult> CreateLargeFile(string source, Classification classification)
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
                        AdditionalMetadata = new FileMetadata
                        {
                            Source = source,
                            Classification = classification
                        }
                    }
                };

                var spoIds = await _filesRepository.CreateFileAsync(file, formFile);
                objectId = spoIds.ObjectId;
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
            if (objectId.IsNullOrEmpty()) return NotFound("ObjectId not found");

            return Ok(objectId);
        }

        // POST
        [HttpPut]
        [Route("content/{objectId}")]
        //[Route("{objectId}/content")]
        public async Task<IActionResult> UpdateFileContent(string objectId, [FromBody] byte[] content)
        {
            bool succeeded;
            try
            {
                succeeded = await _filesRepository.UpdateContentAsync(Request, objectId, content);
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
                return StatusCode(500, ex.Message ?? "Error while updating content");
            }
            if (!succeeded) return NotFound("Error while updating content");

            return Ok();
        }

        [HttpPut]
        [Route("metadata/{objectId}")]
        //[Route("{objectId}/metadata")]
        public async Task<IActionResult> UpdateFileMetadata(string objectId, [FromBody] FileInformation fileInfo)
        {
            fileInfo.Ids.ObjectId = objectId;

            FieldValueSet? fields;
            try
            {
                fields = await _filesRepository.UpdateMetadataAsync(fileInfo);
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
            if (fields == null) return NotFound("Error while updating metadata");

            return Ok();
        }
    }
}
