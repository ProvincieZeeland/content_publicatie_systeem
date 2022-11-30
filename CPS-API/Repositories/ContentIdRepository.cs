using CPS_API.Helpers;
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
        private readonly StorageTableService _storageTableService;
        //private readonly GraphService _graphService;

        public ContentIdRepository(StorageTableService storageTableService)
        {
            _storageTableService = storageTableService;
        }

        public Task<string> GenerateContentIdAsync(ContentIds sharePointIds)
        {
            // Check if sharepointIds already in table, if so; return contentId.

            // Get sequencenr for contentid from table
            // Increase sequencenr and store in table
            // Create new contentId
            // find driveID + driveItemID for object
            // Store contentId + backend ids in table


            // Drive bepalen.
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

            //// Huidig volgnummer ophalen uit settings
            //var currentSetting = await this._storageTableService.GetCurrentSettingAsync();
            //if (currentSetting == null || currentSetting.Sequence < 0)
            //{
            //    return StatusCode(500);
            //}

            //// Nieuwe volgnummer opslaan in settings
            //var sequence = currentSetting.Sequence + 1;
            //var newSetting = new SettingsEntity(sequence);
            //var succeeded = await this._storageTableService.SaveSettingAsync(newSetting);
            //if (!succeeded)
            //{
            //    return StatusCode(500);
            //}

            //// Id's opslaan in documents
            //string? contentId;
            //try
            //{
            //    contentId = $"ZLD{DateTime.Now.Year}-{sequence}";
            //    succeeded = await this._storageTableService.SaveContentIdsAsync(contentId, drive, driveItem, ids);
            //    if (!succeeded)
            //    {
            //        // Volgnummer terugdraaien.
            //        await this._storageTableService.SaveSettingAsync(currentSetting);

            //        return StatusCode(500);
            //    }
            //}
            //catch (Exception)
            //{
            //    // Volgnummer terugdraaien.
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
