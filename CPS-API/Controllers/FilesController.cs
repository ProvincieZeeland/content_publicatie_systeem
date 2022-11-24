using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.WindowsAzure.Storage.Table;
using System.Text.Json;

namespace CPS_API.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        // GET
        [HttpGet]
        [Route("content/{contentId}")]
        //[Route("{contentId}/content")]
        public async Task<IActionResult> GetFileURL(string contentId)
        {
            // Storageaccount definiëren.
            var tableClient = ApiHelper.getCloudTableClientFromStorageAccount();
            if (tableClient == null)
            {
                return StatusCode(500);
            }

            // Sharepoint id's bepalen.
            var documentsEntity = await this.getDocumentsEntity(tableClient, contentId);
            if (documentsEntity == null)
            {
                return NotFound();
            }

            // Bestand ophalen.
            var fileUrl = await GraphHelper.GetFileUrlAsync(documentsEntity.SiteId.ToString(), documentsEntity.DriveItemId);
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
            // Storageaccount definiëren.
            var tableClient = ApiHelper.getCloudTableClientFromStorageAccount();
            if (tableClient == null)
            {
                return StatusCode(500);
            }

            // Sharepoint id's bepalen.
            var documentsEntity = await this.getDocumentsEntity(tableClient, contentId);
            if (documentsEntity == null)
            {
                return NotFound();
            }

            // Listitem ophalen.
            var listItem = await GraphHelper.GetLisItemAsync(documentsEntity.SiteId.ToString(), documentsEntity.ListId.ToString(), documentsEntity.ListItemId.ToString());
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

        private async Task<DocumentsEntity?> getDocumentsEntity(CloudTableClient? tableClient, string contentId)
        {
            var documentsTable = tableClient.GetTableReference("documents");
            var retrieveOperation = TableOperation.Retrieve<DocumentsEntity>(contentId, contentId);
            var result = await documentsTable.ExecuteAsync(retrieveOperation);
            var documentsEntity = result.Result as DocumentsEntity;
            return documentsEntity;
        }

        // PUT
        [HttpPut]
        public async Task<IActionResult> CreateFile([FromBody] CpsFile file)
        {
            throw new NotImplementedException();
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
