using CPS_API.Helpers;
using CPS_API.Models;
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
        private readonly StorageTableService _storageTableService;

        public FilesController(StorageTableService storageTableService)
        {
            this._storageTableService = storageTableService;
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
            // Content tijdelijk opslaan in geheugen van App Service
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
