using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using static CPS_API.Helpers.GraphHelper;

namespace CPS_API.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly StorageTableService _storageTableService;
        private readonly IFilesRepository _filesRepository;
        private readonly IContentIdRepository _contentIdRepository;

        public FilesController(StorageTableService storageTableService,
                               IFilesRepository filesRepository,
                               IContentIdRepository contentIdRepository)
        {
            this._storageTableService = storageTableService;
            _filesRepository = filesRepository;
            _contentIdRepository = contentIdRepository;
        }

        // GET
        [HttpGet]
        [Route("content/{contentId}")]
        //[Route("{contentId}/content")]
        public async Task<IActionResult> GetFileURL(string contentId)
        {
            // Sharepoint id's bepalen.
            var documentsEntity = await this._storageTableService.GetContentIdsAsync(contentId);
            if (documentsEntity == null)
            {
                return NotFound();
            }

            // Bestand ophalen.
            string fileUrl;
            try
            {
                fileUrl = await GraphHelper.GetFileUrlAsync(documentsEntity.SiteId.ToString(), documentsEntity.DriveItemId);
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedException)
            {
                return StatusCode(401);
            }
            catch (Exception ex)
            {
                return StatusCode(500);
            }
            if (fileUrl.IsNullOrEmpty())
            {
                return NotFound();
            }

            // Klaar
            return Ok(fileUrl);
        }

        [HttpGet]
        [Route("metadata/{contentId}")]
        //[Route("{contentId}/metadata")]
        public async Task<IActionResult> GetFileMetadata(string contentId)
        {
            // Sharepoint id's bepalen.
            var documentsEntity = await this._storageTableService.GetContentIdsAsync(contentId);
            if (documentsEntity == null)
            {
                return NotFound();
            }

            // Listitem ophalen.
            ListItem listItem;
            try
            {
                listItem = await GraphHelper.GetLisItemAsync(documentsEntity.SiteId.ToString(), documentsEntity.ListId.ToString(), documentsEntity.ListItemId.ToString());
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedException)
            {
                return StatusCode(401);
            }
            catch (Exception ex)
            {
                return StatusCode(500);
            }
            if (listItem == null)
            {
                return NotFound();
            }

            // Metadata mappen.
            var metadata = listItem.Fields as MetadataFieldValueSet;
            if (metadata == null)
            {
                return NotFound();
            }
            var json = JsonSerializer.Serialize<MetadataFieldValueSet>(metadata);

            // Klaar
            return Ok(json);
        }

        // PUT
        [HttpPut]
        public async Task<IActionResult> CreateFile([FromBody] CpsFile file)
        {
            // Content tijdelijk opslaan in geheugen van App Service; let op, check bestandsformaat voor opslaan > te groot? foutmelding
            // Opslaan mislukt? Dan loggen in App Insights

            // Nieuw bestand aanmaken in Sharepoint m.b.v. Graph
            // Aanmaak mislukt? Dan loggen in App Insights
            DriveItem? driveItem;
            try
            {
                using (var ms = new MemoryStream())
                {
                    await Request.Body.CopyToAsync(ms);
                    ms.Position = 0;

                    driveItem = await GraphHelper.PutFileAsync(file.Metadata.Ids.SiteId.ToString(), file.Metadata.Ids.DriveItemId.ToString(), ms);
                }
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedException)
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

            // Metadata mappen naar Sharepoint kolommen
            // Mapping mislukt? 
            //  - loggen in App Insights
            //  - bestand verwijderen uit Sharepoint

            // ContentId genereren
            // Genereren mislukt?
            //  - loggen in App Insights
            //  - bestand verwijderen uit Sharepoint
            string? contentId;
            try
            {
                contentId = this.createId(file.Metadata.Ids);
            }
            catch (Exception ex)
            {
                // Bestand verwijderen uit Sharepoint
                GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
                return StatusCode(500);
            }
            if (contentId.IsNullOrEmpty())
            {
                // Bestand verwijderen uit Sharepoint
                GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
                return StatusCode(500);
            }

            // ContentId en metadata bijwerken in Sharepoint m.b.v. Graph
            // Bijwerken mislukt? 
            //  - loggen in App Insights
            //  - bestand verwijderen uit Sharepoint
            //  - contendIds verwijderen uit Azure Storage Account
            //  - volgnummer verlagen
            try
            {

            }
            catch (Exception ex)
            {
                // Bestand verwijderen uit Sharepoint
                GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
                return StatusCode(500);
            }

            // Klaar
            return Ok(contentId);
        }

        private string? createId(ContentIds ids)
        {
            var baseUrl = "https://localhost:7159/";
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(baseUrl + "files/contentid/");
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Method = "PUT";

            using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
            {
                var json = JsonSerializer.Serialize<ContentIds>(ids);
                streamWriter.Write(json);
            }

            var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
            using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
            {
                return streamReader.ReadToEnd();
            }
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
