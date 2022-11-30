using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text.Json;
using static CPS_API.Helpers.GraphHelper;

namespace CPS_API.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly IDocumentsRepository _documentsRepository;

        public FilesController(IDocumentsRepository documentsRepository)
        {
            this._documentsRepository = documentsRepository;
        }

        // GET
        [HttpGet]
        [Route("content/{contentId}")]
        //[Route("{contentId}/content")]
        public async Task<IActionResult> GetFileURL(string contentId)
        {
            // Get SharepPoint ids.
            var documentsEntity = await this._documentsRepository.GetContentIdsAsync(contentId);
            if (documentsEntity == null)
            {
                return NotFound();
            }

            // Get File
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

            // Done
            return Ok(fileUrl);
        }

        [HttpGet]
        [Route("metadata/{contentId}")]
        //[Route("{contentId}/metadata")]
        public async Task<IActionResult> GetFileMetadata(string contentId)
        {
            // Get SharePoint ids
            var documentsEntity = await this._documentsRepository.GetContentIdsAsync(contentId);
            if (documentsEntity == null)
            {
                return NotFound();
            }

            // Get Listitem
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

            // Map metadata
            var metadata = listItem.Fields as MetadataFieldValueSet;
            if (metadata == null)
            {
                return NotFound();
            }
            var json = JsonSerializer.Serialize<MetadataFieldValueSet>(metadata);

            // Done
            return Ok(json);
        }

        // PUT
        [HttpPut]
        public async Task<IActionResult> CreateFile([FromBody] CpsFile file)
        {
            // Save content temporary in App Service memory.
            // Failed? Log error in App Insights

            // Add new file in SharePoint with Graph
            // Failed? Log error in App Insights
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
                contentId = this.createId(file.Metadata.Ids);
            }
            catch (Exception ex)
            {
                // Remove file from Sharepoint
                GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
                return StatusCode(500);
            }
            if (contentId.IsNullOrEmpty())
            {
                // Remove file from Sharepoint
                GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
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
                GraphHelper.DeleteFileAsync(file.Metadata.Ids.SiteId.ToString(), driveItem.Id);
                return StatusCode(500);
            }

            // Done
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

        [HttpPut]
        [DisableFormValueModelBinding]
        [DisableRequestSizeLimit]
        [RequestFormLimits(MultipartBodyLengthLimit = int.MaxValue)]
        [Route("new/{classification}")]
        public async Task<IActionResult> CreateLargeFile(Classification classification)
        {
            throw new NotImplementedException();
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
