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

            // Done
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
                metadata = await _filesRepository.GetFileMetadataAsync(contentId);
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

            // Done
            return Ok(metadata);
        }

        // PUT
        [HttpPut]
        public async Task<IActionResult> CreateFile([FromBody] CpsFile file)
        {
            // Save content temporary in App Service memory.
            // Failed? Log error in App Insights

            // Add new file in SharePoint with Graph
            // Failed? Log error in App Insights
            DriveItem? driveItem = null;
            try
            {
                using (var ms = new MemoryStream())
                {
                    await Request.Body.CopyToAsync(ms);
                    ms.Position = 0;

                    //driveItem = await GraphHelper.PutFileAsync(file.Metadata.Ids.SiteId.ToString(), file.Metadata.Ids.DriveItemId.ToString(), ms);
                }
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedAccessException)
            {
                return StatusCode(401);
            }
            catch (Exception ex)
            {
                return StatusCode(500);
            }
            if (driveItem == null)
            {
                return StatusCode(500);
            }

            // Map metadata to Sharepoint columns
            // Failed?
            //  - log error in App Insights
            //  - remove file from Sharepoint

            // Generate ContentId
            // Failed?
            //  - log error in App Insights
            //  - remove file from Sharepoint
            string? contentId;
            try
            {
                contentId = await _contentIdRepository.GenerateContentIdAsync(file.Metadata.Ids);
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                //GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
                return StatusCode(500);
            }
            if (contentId.IsNullOrEmpty())
            {
                // Remove file from Sharepoint
                //GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
                return StatusCode(500);
            }

            // Update ContentId and metadata in Sharepoint with Graph
            // Failed?
            //  - log error in App Insights
            //  - remove file from Sharepoint
            //  - remove contendIds from Azure Storage Account
            //  - decrease sequence
            try
            {

            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                //GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
                return StatusCode(500);
            }

            // Done
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
            ContentIds newSharePointIds = await _filesRepository.CreateAsync(file);
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
            throw new NotImplementedException();
        }
    }
}
