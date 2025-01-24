using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CPS_Jobs
{
    public class SynchronisationFunction
    {
        private readonly ILogger<SynchronisationFunction> _logger;
        private readonly IConfiguration _configuration;
        private readonly AppService _appService;

        public SynchronisationFunction(ILogger<SynchronisationFunction> logger, IConfiguration config, AppService appService)
        {
            _logger = logger;
            _configuration = config;
            _appService = appService;
        }

        [Function("SynchronisationFunction")]
        public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer)
        {
            _logger.LogInformation("CPS Timer trigger function started at: {Now}", DateTime.Now);

            var scope = _configuration.GetValue<string>("Settings:Scope");
            var baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            if (string.IsNullOrEmpty(scope)) throw new CpsException("Scope cannot be empty");
            if (string.IsNullOrEmpty(baseUrl)) throw new CpsException("BaseUrl cannot be empty");

            List<Task> tasks = new List<Task>();
            // Start New sync     
            tasks.Add(_appService.GetAsync(baseUrl, scope, "/Export/new"));

            // Start Update sync  
            tasks.Add(_appService.GetAsync(baseUrl, scope, "/Export/updated"));

            // Start Delete sync  
            tasks.Add(_appService.GetAsync(baseUrl, scope, "/Export/deleted"));

            // Wait for all to finish
            await Task.WhenAll(tasks);
        }
    }
}