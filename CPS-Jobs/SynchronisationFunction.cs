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
        private readonly IConfiguration _configuration;
        private readonly AppService _appService;

        public SynchronisationFunction(IConfiguration config,
                                       AppService appService)
        {
            _configuration = config;
            _appService = appService;
        }

        [Function("SynchronisationFunction")]
        public async Task Run([TimerTrigger("0 */5 * * * *")] TimerInfo timer, ILogger log)
        {
            log.LogInformation($"CPS Timer trigger function started at: {DateTime.Now}");

            string scope = _configuration.GetValue<string>("Settings:Scope");
            string baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            if (string.IsNullOrEmpty(scope)) throw new CpsException("Scope cannot be empty");
            if (string.IsNullOrEmpty(baseUrl)) throw new CpsException("BaseUrl cannot be empty");

            List<Task> tasks = new List<Task>();
            // Start New sync     
            tasks.Add(_appService.callService(baseUrl, scope, "/Export/new", log));

            // Start Update sync  
            tasks.Add(_appService.callService(baseUrl, scope, "/Export/updated", log));

            // Start Delete sync  
            tasks.Add(_appService.callService(baseUrl, scope, "/Export/deleted", log));

            // Wait for all to finish
            await Task.WhenAll(tasks);
        }
    }
}