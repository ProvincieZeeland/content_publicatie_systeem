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
                contentId = await _filesRepository.CreateFileAsync(Request, file);
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

        private static readonly FormOptions _defaultFormOptions = new FormOptions();

        [HttpPut]
        [DisableFormValueModelBinding]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = int.MaxValue)]
        [Route("new/{classification}")]
        public async Task<IActionResult> CreateLargeFile(Classification classification)
        {
            if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                ModelState.AddModelError("File", $"The request couldn't be processed (ContentType is not multipart).");
                return BadRequest(ModelState);
            }

            // Find wanted storage location depending on classification
            // todo: get driveid matching classification
            string driveId = "";


            // ToDo: move to service/helper
            var formAccumulator = new KeyValueAccumulator();
            var untrustedFileNameForStorage = string.Empty;
            var streamedFileContent = Array.Empty<byte>();

            var boundary = MultipartRequestHelper.GetBoundary(MediaTypeHeaderValue.Parse(Request.ContentType),
                                                              _defaultFormOptions.MultipartBoundaryLengthLimit);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync();
            while (section != null)
            {
                var hasContentDispositionHeader = ContentDispositionHeaderValue.TryParse(section.ContentDisposition,
                                                                                         out var contentDisposition);

                if (hasContentDispositionHeader)
                {
                    if (MultipartRequestHelper.HasFileContentDisposition(contentDisposition))
                    {
                        untrustedFileNameForStorage = contentDisposition.FileName.Value;
                        streamedFileContent = await FileHelper.ProcessStreamedFile(section,
                                                                                   contentDisposition,
                                                                                   ModelState);

                        if (!ModelState.IsValid)
                        {
                            // todo: Log error to app insights separately
                            return BadRequest(ModelState);
                        }
                    }
                    else if (MultipartRequestHelper.HasFormDataContentDisposition(contentDisposition))
                    {
                        // Don't limit the key name length because the 
                        // multipart headers length limit is already in effect.
                        var key = HeaderUtilities.RemoveQuotes(contentDisposition.Name).Value;
                        var encoding = FileHelper.GetEncoding(section);

                        if (encoding == null)
                        {
                            ModelState.AddModelError("File", $"The request couldn't be processed, encoding not found.");
                            // todo: Log error to app insights separately

                            return BadRequest(ModelState);
                        }

                        using (var streamReader = new StreamReader(
                            section.Body,
                            encoding,
                            detectEncodingFromByteOrderMarks: true,
                            bufferSize: 1024,
                            leaveOpen: true))
                        {
                            // The value length limit is enforced by MultipartBodyLengthLimit
                            var value = await streamReader.ReadToEndAsync();
                            if (string.Equals(value, "undefined",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                value = string.Empty;
                            }

                            formAccumulator.Append(key, value);
                        }
                    }
                }

                // Drain any remaining section body that hasn't been consumed and
                // read the headers for the next section.
                section = await reader.ReadNextSectionAsync();
            }

            var file = new CpsFile()
            {
                Content = streamedFileContent,
                Metadata = new FileInformation
                {
                    Ids = new ContentIds { DriveId = driveId },
                    FileName = untrustedFileNameForStorage,
                    AdditionalMetadata = new FileMetadata
                    {
                        Classification = classification
                    }
                }
            };

            // save to repo
            ContentIds newSharePointIds = await _filesRepository.CreateFileAsync(file);
            string contentId = await _contentIdRepository.GenerateContentIdAsync(newSharePointIds);

            return Ok(contentId);
        }

        // POST
        [HttpPut]
        [Route("content/{contentId}")]
        //[Route("{contentId}/content")]
        public async Task<IActionResult> UpdateFileContent(string contentId, [FromBody] byte[] content)
        {
            throw new NotImplementedException();
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
