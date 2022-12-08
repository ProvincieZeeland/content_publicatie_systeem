using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace CPS_API.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly IFilesRepository _filesRepository;
        private readonly IContentIdRepository _contentIdRepository;

        public FilesController(IFilesRepository filesRepository,
                               IContentIdRepository contentIdRepository)
        {
            _filesRepository = filesRepository;
            _contentIdRepository = contentIdRepository;
        }

        // GET
        [HttpGet]
        [Route("content/{contentId}")]
        //[Route("{contentId}/content")]
        public async Task<IActionResult> GetFileURL(string contentId)
        {
            string? fileUrl;
            try
            {
                fileUrl = await _filesRepository.GetUrlAsync(contentId);
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
        [Route("metadata/{contentId}")]
        //[Route("{contentId}/metadata")]
        public async Task<IActionResult> GetFileMetadata(string contentId)
        {
            FileInformation metadata;
            try
            {
                metadata = await _filesRepository.GetMetadataAsync(contentId);
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
            string? contentId;
            try
            {
                var spoIds = await _filesRepository.CreateFileAsync(file);
                contentId = spoIds.ContentId;
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
            if (contentId.IsNullOrEmpty()) return NotFound("ContentId not found");

            return Ok(contentId);
        }


        [HttpPut]
        [RequestSizeLimit(5368709120)] // 5 GB
        [Route("new/{source}/{classification}")]
        public async Task<IActionResult> CreateLargeFile(string source, Classification classification)
        {
            if (Request.Form.Files.Count != 1) throw new ArgumentException("You must add one file for upload in form data");

            string? contentId;
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
                contentId = spoIds.ContentId;
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
            if (contentId.IsNullOrEmpty()) return NotFound("ContentId not found");

            return Ok(contentId);
        }

        // POST
        [HttpPut]
        [Route("content/{contentId}")]
        //[Route("{contentId}/content")]
        public async Task<IActionResult> UpdateFileContent(string contentId, [FromBody] byte[] content)
        {
            bool succeeded;
            try
            {
                succeeded = await _filesRepository.UpdateContentAsync(Request, contentId, content);
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
        [Route("metadata/{contentId}")]
        //[Route("{contentId}/metadata")]
        public async Task<IActionResult> UpdateFileMetadata(string contentId, [FromBody] FileInformation fileInfo)
        {
            fileInfo.Ids.ContentId = contentId;

            ListItem? listItem;
            try
            {
                listItem = await _filesRepository.UpdateMetadataAsync(fileInfo);
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
            if (listItem == null) return NotFound("ListItem not found");

            return Ok();
        }
    }
}
