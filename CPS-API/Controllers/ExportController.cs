using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPS_API.Controllers
{
    [Authorize]
    [Route("[controller]")]
    [ApiController]
    public class ExportController : Controller
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly IExportRepository _exportRepository;

        private readonly TelemetryClient _telemetryClient;

        public ExportController(
            ISettingsRepository settingsRepository,
            TelemetryClient telemetryClient,
            IExportRepository exportRepository)
        {
            _settingsRepository = settingsRepository;
            _telemetryClient = telemetryClient;
            _exportRepository = exportRepository;
        }

        // GET
        [HttpGet]
        [Route("new")]
        public async Task<IActionResult> SynchroniseNewDocuments()
        {
            // Other synchronisation still running?
            try
            {
                bool? isSynchronisationRunning = await _settingsRepository.GetSetting<bool?>(Constants.SettingsIsNewSynchronisationRunningField);
                if (isSynchronisationRunning.HasValue && isSynchronisationRunning.Value) return Ok("Other new synchronisation job is running.");
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                return StatusCode(500, "Error while getting IsNewSynchronisationRunning");
            }

            // Set running synchronisation.
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsNewSynchronisationRunningField, true);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(Constants.SettingsLastTokenForNewField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsNewSynchronisationRunningField, false);
                return StatusCode(500, "Error while getting LastTokenForNew");
            }

            // Get last synchronisation date.
            DateTime? lastSynchronisation;
            try
            {
                lastSynchronisation = await _settingsRepository.GetSetting<DateTime?>(Constants.SettingsLastSynchronisationNewField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsNewSynchronisationRunningField, false);
                return StatusCode(500, ex.Message ?? "Error while getting LastSynchronisation");
            }
            if (lastSynchronisation == null) lastSynchronisation = DateTime.UtcNow.Date;

            ExportResponse result;
            try
            {
                result = await _exportRepository.SynchroniseNewDocumentsAsync(lastSynchronisation.Value, tokens);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsNewSynchronisationRunningField, false);
                return StatusCode(500, ex.Message ?? "Error while synchronising new documents");
            }

            // If all files are succesfully added then we update the last synchronisation date and token.
            try
            {
                var newSettings = new Dictionary<string, object?> {
                { Constants.SettingsLastSynchronisationNewField, DateTime.UtcNow },
                { Constants.SettingsLastTokenForNewField, result.NewNextTokens },
                { Constants.SettingsIsNewSynchronisationRunningField, false }
            };
                await _settingsRepository.SaveSettingsAsync(newSettings);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsNewSynchronisationRunningField, false);
                return StatusCode(500, ex.Message ?? "Error while synchronising new documents");
            }

            return Ok(GetNewResponse(result));
        }

        private static string GetNewResponse(ExportResponse result)
        {
            var failedItemsStr = result.FailedItems.Select(item => $"Error while adding file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) to FileStorage.\r\n").ToList();
            var message = String.Join(",", failedItemsStr.Select(x => x.ToString()).ToArray());
            return $"{result.NumberOfSucceededItems} items added" + (failedItemsStr.Count == 0 ? "" : "\r\n") + message;
        }

        [HttpGet]
        [Route("updated")]
        public async Task<IActionResult> SynchroniseUpdatedDocuments()
        {
            // Other synchronisation still running?
            try
            {
                bool? isSynchronisationRunning = await _settingsRepository.GetSetting<bool?>(Constants.SettingsIsChangedSynchronisationRunningField);
                if (isSynchronisationRunning.HasValue && isSynchronisationRunning.Value)
                {
                    return Ok("Other update synchronisation job is running.");
                }
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                return StatusCode(500, "Error while getting IsChangedSynchronisationRunning");
            }

            // Set running synchronisation.
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsChangedSynchronisationRunningField, true);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(Constants.SettingsLastTokenForChangedField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsChangedSynchronisationRunningField, false);
                return StatusCode(500, "Error while getting LastTokenForChanged");
            }

            // Get last synchronisation date.
            DateTime? lastSynchronisation;
            try
            {
                lastSynchronisation = await _settingsRepository.GetSetting<DateTime?>(Constants.SettingsLastSynchronisationChangedField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsChangedSynchronisationRunningField, false);
                return StatusCode(500, "Error while getting LastSynchronisation");
            }
            if (lastSynchronisation == null) lastSynchronisation = DateTime.UtcNow.Date;


            ExportResponse result;
            try
            {
                result = await _exportRepository.SynchroniseUpdatedDocumentsAsync(lastSynchronisation.Value, tokens);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsChangedSynchronisationRunningField, false);
                return StatusCode(500, ex.Message ?? "Error while synchronising updated documents");
            }

            // If all files are succesfully updated then we update the last synchronisation date and token. 
            var newSettings = new Dictionary<string, object?> {
                { Constants.SettingsLastSynchronisationChangedField, DateTime.UtcNow },
                { Constants.SettingsLastTokenForChangedField, result.NewNextTokens },
                { Constants.SettingsIsChangedSynchronisationRunningField, false }
            };
            await _settingsRepository.SaveSettingsAsync(newSettings);

            return Ok(GetUpdatedResponse(result));
        }

        private static string GetUpdatedResponse(ExportResponse result)
        {
            var failedItemsStr = result.FailedItems.Select(item => $"Error while updating file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) in FileStorage.\r\n").ToList();
            var message = String.Join(",", failedItemsStr.Select(x => x.ToString()).ToArray());
            return $"{result.NumberOfSucceededItems} items updated" + (failedItemsStr.Count == 0 ? "" : "\r\n") + message;
        }

        [HttpGet]
        [Route("deleted")]
        public async Task<IActionResult> SynchroniseDeletedDocuments()
        {
            // Other synchronisation still running?
            try
            {
                bool? isSynchronisationRunning = await _settingsRepository.GetSetting<bool?>(Constants.SettingsIsDeletedSynchronisationRunningField);
                if (isSynchronisationRunning.HasValue && isSynchronisationRunning.Value)
                {
                    return Ok("Other delete synchronisation job is running.");
                }
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                return StatusCode(500, "Error while getting IsDeleteddSynchronisationRunning");
            }

            // Set running synchronisation.
            await _settingsRepository.SaveSettingAsync(Constants.SettingsIsDeletedSynchronisationRunningField, true);

            // Get last synchronisation token.
            Dictionary<string, string> tokens;
            try
            {
                tokens = await _settingsRepository.GetLastTokensAsync(Constants.SettingsLastTokenForDeletedField);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsDeletedSynchronisationRunningField, false);
                return StatusCode(500, "Error while getting LastTokenForDeleted");
            }


            ExportResponse result;
            try
            {
                result = await _exportRepository.SynchroniseDeletedDocumentsAsync(tokens);
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                await _settingsRepository.SaveSettingAsync(Constants.SettingsIsDeletedSynchronisationRunningField, false);
                return StatusCode(500, ex.Message ?? "Error while synchronising deleted documents");
            }

            // If all files are succesfully deleted then we update the token.
            var newSettings = new Dictionary<string, object?> {
                { Constants.SettingsLastSynchronisationDeletedField, DateTime.UtcNow },
                { Constants.SettingsLastTokenForDeletedField, result.NewNextTokens },
                { Constants.SettingsIsDeletedSynchronisationRunningField, false }
            };
            await _settingsRepository.SaveSettingsAsync(newSettings);

            return Ok(GetDeletedResponse(result));
        }

        private static string GetDeletedResponse(ExportResponse result)
        {
            var failedItemsStr = result.FailedItems.Select(item => $"Error while deleting file (DriveId: {item.DriveId}, DriveItemId: {item.Id}) from FileStorage.\r\n").ToList();
            var message = String.Join(",", failedItemsStr.Select(x => x.ToString()).ToArray());
            return $"{result.NumberOfSucceededItems} items deleted" + (failedItemsStr.Count == 0 ? "" : "\r\n") + message;
        }

        // GET
        [HttpGet]
        [Route("publish")]
        public async Task<IActionResult> SynchroniseToBePublishedDocuments()
        {
            ToBePublishedExportResponse result;
            try
            {
                result = await _exportRepository.SynchroniseToBePublishedDocumentsAsync();
            }
            catch (Exception ex)
            {
                _telemetryClient.TrackException(ex);
                return StatusCode(500, ex.Message ?? "Error while synchronising new documents");
            }

            return Ok(GetPublicationResponse(result));
        }

        private static string GetPublicationResponse(ToBePublishedExportResponse result)
        {
            var failedItemsStr = result.FailedItems.Select(id => $"Error while adding file (ObjectId: {id}) to FileStorage.\r\n").ToList();
            var message = String.Join(",", failedItemsStr.Select(x => x.ToString()).ToArray());
            return $"{result.NumberOfSucceededItems} items added" + (failedItemsStr.Count == 0 ? "" : "\r\n") + message;
        }
    }
}