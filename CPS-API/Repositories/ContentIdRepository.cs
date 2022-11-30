using CPS_API.Models;

namespace CPS_API.Repositories
{
    public interface IContentIdRepository
    {
        Task<string> GetContentIdAsync(ContentIds sharePointIds);

        Task<string> GenerateContentIdAsync(ContentIds sharePointIds);
    }

    public class ContentIdRepository : IContentIdRepository
    {
        private readonly ISettingsRepository _settingsRepository;

        private readonly IDocumentsRepository _documentsRepository;

        //private readonly GraphService _graphService;

        public ContentIdRepository(ISettingsRepository settingsRepository, IDocumentsRepository documentsRepository)
        {
            this._settingsRepository = settingsRepository;
            this._documentsRepository = documentsRepository;
        }

        public Task<string> GenerateContentIdAsync(ContentIds sharePointIds)
        {
            // Check if sharepointIds already in table, if so; return contentId.

            // Get sequencenr for contentid from table
            // Increase sequencenr and store in table
            // Create new contentId
            // find driveID + driveItemID for object
            // Store contentId + backend ids in table


            //// Get drive ids.
            //Drive? drive;
            //DriveItem? driveItem;
            //try
            //{
            //    drive = await GraphHelper.GetDriveAsync(ids.SiteId.ToString());
            //    driveItem = await GraphHelper.GetDriveItemAsync(ids.SiteId.ToString(), ids.ListId.ToString(), ids.ListItemId.ToString());
            //}
            //catch (Exception ex) when (ex.InnerException is UnauthorizedException)
            //{
            //    return StatusCode(401);
            //}
            //catch (Exception ex)
            //{
            //    return StatusCode(500);
            //}

            //// Get current sequence from settings
            //var currentSetting = await this._storageTableService.GetCurrentSettingAsync();
            //if (currentSetting == null || currentSetting.Sequence < 0)
            //{
            //    return StatusCode(500);
            //}

            //// Save new sequence in settings
            //var sequence = currentSetting.Sequence + 1;
            //var newSetting = new SettingsEntity(sequence);
            //var succeeded = await this._storageTableService.SaveSettingAsync(newSetting);
            //if (!succeeded)
            //{
            //    return StatusCode(500);
            //}

            //// Save Ids in documents
            //string? contentId;
            //try
            //{
            //    contentId = $"ZLD{DateTime.Now.Year}-{sequence}";
            //    succeeded = await this._storageTableService.SaveContentIdsAsync(contentId, drive, driveItem, ids);
            //    if (!succeeded)
            //    {
            //        // Undo sequence change
            //        await this._storageTableService.SaveSettingAsync(currentSetting);

            //        return StatusCode(500);
            //    }
            //}
            //catch (Exception)
            //{
            //    // Undo sequence change
            //    await this._storageTableService.SaveSettingAsync(currentSetting);

            //    return StatusCode(500);
            //}

            throw new NotImplementedException();
        }

        public Task<string> GetContentIdAsync(ContentIds sharePointIds)
        {
            // find contentId in table by sharepoint siteId + webId + listId + itemId

            throw new NotImplementedException();
        }
    }
}
