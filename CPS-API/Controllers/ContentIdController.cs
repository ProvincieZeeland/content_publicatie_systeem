using CPS_API.Helpers;
using CPS_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using static CPS_API.Helpers.GraphHelper;

namespace CPS_API.Controllers
{
    [Route("/files/[controller]")]
    [ApiController]
    public class ContentIdController : Controller
    {
        private readonly StorageTableService _storageTableService;

        public ContentIdController(StorageTableService storageTableService)
        {
            this._storageTableService = storageTableService;
        }

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

            // Huidig volgnummer ophalen uit settings
            var currentSetting = await this._storageTableService.GetCurrentSettingAsync();
            if (currentSetting == null || currentSetting.Sequence < 0)
            {
                return StatusCode(500);
            }

            // Nieuwe volgnummer opslaan in settings
            var sequence = currentSetting.Sequence + 1;
            var newSetting = new SettingsEntity(sequence);
            var succeeded = await this._storageTableService.SaveSettingAsync(newSetting);
            if (!succeeded)
            {
                return StatusCode(500);
            }

            // Id's opslaan in documents
            string? contentId;
            try
            {
                contentId = $"ZLD{DateTime.Now.Year}-{sequence}";
                succeeded = await this._storageTableService.SaveContentIdsAsync(contentId, drive, driveItem, ids);
                if (!succeeded)
                {
                    // Volgnummer terugdraaien.
                    await this._storageTableService.SaveSettingAsync(currentSetting);

                    return StatusCode(500);
                }
            }
            catch (Exception)
            {
                // Volgnummer terugdraaien.
                await this._storageTableService.SaveSettingAsync(currentSetting);

                return StatusCode(500);
            }

            // Klaar
            return Ok(contentId);
        }
    }
}
