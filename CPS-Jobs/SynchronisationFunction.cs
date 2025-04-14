using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CPS_Jobs
{
    public class SynchronisationFunction
    {
        private readonly ILogger<SynchronisationFunction> _logger;
        private readonly GlobalSettings _globalSettings;
        private readonly AppService _appService;

        public SynchronisationFunction(ILogger<SynchronisationFunction> logger, IOptions<GlobalSettings> config, AppService appService)
        {
            _logger = logger;
            _globalSettings = config.Value;
            _appService = appService;
        }

        [Function("SynchronisationFunction")]
        public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer)
        {
            _logger.LogInformation("CPS Timer trigger function started at: {Now}", DateTime.Now);

            if (string.IsNullOrEmpty(_globalSettings.Scope)) throw new CpsException("Scope cannot be empty");
            if (string.IsNullOrEmpty(_globalSettings.BaseUrl)) throw new CpsException("BaseUrl cannot be empty");

            List<Task> tasks = new List<Task>();
            // Start New sync     
            tasks.Add(_appService.GetAsync(_globalSettings.BaseUrl, _globalSettings.Scope, "/Export/new"));

            // Start Update sync  
            tasks.Add(_appService.GetAsync(_globalSettings.BaseUrl, _globalSettings.Scope, "/Export/updated"));

            // Start Delete sync  
            tasks.Add(_appService.GetAsync(_globalSettings.BaseUrl, _globalSettings.Scope, "/Export/deleted"));

            // Wait for all to finish
            await Task.WhenAll(tasks);
        }
    }
}