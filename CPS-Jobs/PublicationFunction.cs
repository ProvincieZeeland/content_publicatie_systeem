using System;
using System.Threading.Tasks;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CPS_Jobs
{
    public class PublicationFunction
    {
        private readonly ILogger<PublicationFunction> _logger;
        private readonly IConfiguration _configuration;
        private readonly AppService _appService;

        public PublicationFunction(ILogger<PublicationFunction> logger, IConfiguration config, AppService appService)
        {
            _logger = logger;
            _configuration = config;
            _appService = appService;
        }

        [Function("PublicationFunction")]
        public async Task Run([TimerTrigger("0 0 0 * * *")] TimerInfo timer)
        {
            _logger.LogInformation($"CPS Publication Timer trigger function started at: {DateTime.Now}");

            string scope = _configuration.GetValue<string>("Settings:Scope");
            string baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            if (string.IsNullOrEmpty(scope)) throw new CpsException("Scope cannot be empty");
            if (string.IsNullOrEmpty(baseUrl)) throw new CpsException("BaseUrl cannot be empty");

            await _appService.GetAsync(baseUrl, scope, "/Export/publish");
        }
    }
}