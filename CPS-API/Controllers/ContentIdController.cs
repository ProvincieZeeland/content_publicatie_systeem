using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using static CPS_API.Helpers.GraphHelper;

namespace CPS_API.Controllers
{
    [Route("/files/[controller]")]
    [ApiController]
    public class ContentIdController : Controller
    {
        private readonly ISettingsRepository _settingsRepository;

        private readonly IDocumentsRepository _documentsRepository;

        public ContentIdController(ISettingsRepository settingsRepository, IDocumentsRepository documentsRepository)
        {
            this._settingsRepository = settingsRepository;
            this._documentsRepository = documentsRepository;
        }

        [HttpPut]
        public async Task<IActionResult> CreateId([FromBody] ContentIds ids)
        {
            // Get drive ids.
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

            // Get current sequence from settings
            var currentSetting = await this._settingsRepository.GetCurrentSettingAsync();
            if (currentSetting == null || currentSetting.Sequence < 0)
            {
                return StatusCode(500);
            }

            // Save new sequence in settings
            var sequence = currentSetting.Sequence + 1;
            var newSetting = new SettingsEntity(sequence);
            var succeeded = await this._settingsRepository.SaveSettingAsync(newSetting);
            if (!succeeded)
            {
                return StatusCode(500);
            }

            // Save Ids in documents
            string? contentId;
            try
            {
                contentId = $"ZLD{DateTime.Now.Year}-{sequence}";
                succeeded = await this._documentsRepository.SaveContentIdsAsync(contentId, drive, driveItem, ids);
                if (!succeeded)
                {
                    // Undo sequence change
                    await this._settingsRepository.SaveSettingAsync(currentSetting);

                    return StatusCode(500);
                }
            }
            catch (Exception)
            {
                // Undo sequence change
                await this._settingsRepository.SaveSettingAsync(currentSetting);

                return StatusCode(500);
            }

            // Done
            return Ok(contentId);
        }
    }
}
