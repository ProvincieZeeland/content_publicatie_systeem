using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.WindowsAzure.Storage.Table;
using static CPS_API.Helpers.GraphHelper;

namespace CPS_API.Controllers
{
    [Route("/files/[controller]")]
    [ApiController]
    public class ContentIdController : Controller
    {

        [HttpPut]
        public async Task<IActionResult> CreateId([FromBody] ContentIds ids)
        {
            // Drive bepalen.
            Drive? drive;
            DriveItem? driveItem;
            try
            {
                drive = await GraphHelper.GetDriveAsync(ids.SiteId.ToString());
                driveItem = await GraphHelper.GetDriveItemAsync(ids.SiteId.ToString(), ids.ListId.ToString(), ids.ListItemId.ToString());
            }
            catch (Exception ex) when (ex.InnerException is UnauthorizedException)
            {
                return StatusCode(401);
            }
            catch (Exception ex)
            {
                return StatusCode(500);
            }

            // Storageaccount definiëren.
            var tableClient = ApiHelper.getCloudTableClientFromStorageAccount();
            if (tableClient == null)
            {
                return StatusCode(500);
            }

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
            string? contentId;
            try
            {
                contentId = $"ZLD{DateTime.Now.Year}-{sequence}";
                var document = new DocumentsEntity(contentId, drive, driveItem, ids);
                insertop = TableOperation.InsertOrReplace(document);
                var documentsTable = tableClient.GetTableReference("documents");
                await documentsTable.ExecuteAsync(insertop);
            }
            catch (Exception)
            {
                // Volgnummer terugdraaien.
                insertop = TableOperation.InsertOrReplace(currentSetting);
                await settingsTable.ExecuteAsync(insertop);

                return StatusCode(500);
            }

            // Klaar
            return Ok(contentId);
        }
    }
}
