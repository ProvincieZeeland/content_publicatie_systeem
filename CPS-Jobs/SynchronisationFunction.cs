using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CPS_Jobs.Models;
using CPS_Jobs.Repositories;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace CPS_Jobs
{
    public class SynchronisationFunction
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IConfiguration _configuration;
        private readonly ISettingsRepository _settingsRepository;

        public SynchronisationFunction(ITokenAcquisition tokenAcquisition,
                                       IConfiguration config,
                                       ISettingsRepository settingsRepository)
        {
            _tokenAcquisition = tokenAcquisition;
            _configuration = config;
            _settingsRepository = settingsRepository;
        }

        [FunctionName("SynchronisationFunction")]
        public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, ILogger log)
        {
            log.LogInformation($"CPS Timer trigger function started at: {DateTime.Now}");

            string scope = _configuration.GetValue<string>("Settings:Scope");
            string baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            if (string.IsNullOrEmpty(scope)) throw new Exception("Scope cannot be empty");
            if (string.IsNullOrEmpty(baseUrl)) throw new Exception("BaseUrl cannot be empty");

            string settingsPartitionKey = _configuration.GetValue<string>("Settings:SettingsPartitionKey");
            var settingsLastSynchronisationNewRowKey = _configuration.GetValue<string>("Settings:SettingsLastSynchronisationNewRowKey");
            var settingsLastSynchronisationChangedRowKey = _configuration.GetValue<string>("Settings:SettingsLastSynchronisationChangedRowKey");
            var settingsLastSynchronisationDeletedRowKey = _configuration.GetValue<string>("Settings:SettingsLastSynchronisationDeletedRowKey");
            if (string.IsNullOrEmpty(settingsPartitionKey)) throw new Exception("SettingsPartitionKey cannot be empty");
            if (string.IsNullOrEmpty(settingsLastSynchronisationNewRowKey)) throw new Exception("SettingsLastSynchronisationNewRowKey cannot be empty");
            if (string.IsNullOrEmpty(settingsLastSynchronisationChangedRowKey)) throw new Exception("SettingsLastSynchronisationChangedRowKey cannot be empty");
            if (string.IsNullOrEmpty(settingsLastSynchronisationDeletedRowKey)) throw new Exception("SettingsLastSynchronisationDeletedRowKey cannot be empty");

            List<Task> tasks = new List<Task>();
            // Start New sync     
            var newSetting = new SettingsEntity(settingsPartitionKey, settingsLastSynchronisationNewRowKey);
            newSetting.LastSynchronisationNew = DateTime.UtcNow;
            tasks.Add(callService(baseUrl, scope, "/Export/new", log, newSetting));

            // Start Update sync  
            newSetting = new SettingsEntity(settingsPartitionKey, settingsLastSynchronisationChangedRowKey);
            newSetting.LastSynchronisationChanged = DateTime.UtcNow;
            tasks.Add(callService(baseUrl, scope, "/Export/updated", log, newSetting));

            // Start Delete sync  
            newSetting = new SettingsEntity(settingsPartitionKey, settingsLastSynchronisationDeletedRowKey);
            newSetting.LastSynchronisationDeleted = DateTime.UtcNow;
            tasks.Add(callService(baseUrl, scope, "/Export/deleted", log, newSetting));

            // Wait for all to finish
            await Task.WhenAll(tasks);
        }

        private async Task callService(string baseUrl, string scope, string url, ILogger log, SettingsEntity setting)
        {
            try
            {
                HttpResponseMessage response;
                string token = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);
                using (var client = new HttpClient())
                {
                    var method = HttpMethod.Get;
                    var request = new HttpRequestMessage(method, baseUrl + url);
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    response = await client.SendAsync(request);
                }

                // If all files are succesfully added/updated/deleted then we update the last synchronisation date.
                if (response.IsSuccessStatusCode)
                {
                    await _settingsRepository.SaveSettingAsync(setting);
                }
            }
            catch
            {
                log.LogError("Could not start sync for url " + url);
                throw;
            }
        }
    }
}