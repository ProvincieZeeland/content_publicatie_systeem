using CPS_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Controllers
{
    [Route("/files/[controller]")]
    [ApiController]
    public class ContentIdController : Controller
    {
        [HttpPut]
        public async Task<IActionResult> CreateId([FromBody] ContentIds ids)
        {
            // Configuratie bepalen.
            var builder = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json");
            var configuration = builder.Build();

            // Storageaccount definiëren.
            var connectionString = configuration.GetConnectionString("CloudStorageAccount");
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            var tableClient = storageAccount.CreateCloudTableClient();

            // Huidig volgnummer ophalen uit settings
            var settingsTable = tableClient.GetTableReference("settings");
            var retrieveOperation = TableOperation.Retrieve<SettingsEntity>("0", "0");
            var result = await settingsTable.ExecuteAsync(retrieveOperation);
            var currentSetting = result.Result as SettingsEntity;
            if (currentSetting == null || currentSetting.Sequence < 0)
            {
                return StatusCode(500);
            }

            // Nieuwe volgnummer opslaan in settings
            var sequence = currentSetting.Sequence + 1;
            var newSetting = new SettingsEntity(sequence);
            var insertop = TableOperation.InsertOrReplace(newSetting);
            await settingsTable.ExecuteAsync(insertop);

            // Id's opslaan in documents
            var contentId = $"ZLD{DateTime.Now.Year}-{sequence}";
            var document = new DocumentsEntity(contentId, ids);
            insertop = TableOperation.InsertOrReplace(document);
            var documentsTable = tableClient.GetTableReference("documents");
            await documentsTable.ExecuteAsync(insertop);

            // Klaar
            return Ok(contentId);
        }
    }

    public class SettingsEntity : TableEntity
    {
        public long Sequence { get; set; }

        public SettingsEntity()
        {

        }

        public SettingsEntity(long Sequence)
        {
            this.PartitionKey = "0";
            this.RowKey = "0";
            this.Sequence = Sequence;
        }
    }

    public class DocumentsEntity : TableEntity
    {
        public Guid SiteId { get; set; }

        public Guid WebId { get; set; }

        public Guid ListId { get; set; }

        public int ListItemId { get; set; }

        public Guid DriveId { get; set; }

        public Guid DriveItemId { get; set; }

        public DocumentsEntity()
        {

        }

        public DocumentsEntity(string contentId, ContentIds ids)
        {
            this.PartitionKey = contentId;
            this.RowKey = contentId;
            this.SiteId = ids.SiteId;
            this.WebId = ids.WebId;
            this.ListId = ids.ListId;
            this.ListItemId = ids.ListItemId;
            this.DriveId = ids.DriveId;
            this.DriveItemId = ids.DriveItemId;
        }
    }
}
