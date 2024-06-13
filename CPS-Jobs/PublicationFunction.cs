using System;
using System.Threading.Tasks;
using CPS_Jobs.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CPS_Jobs
{
    public class PublicationFunction
    {
        private readonly IConfiguration _configuration;
        private readonly AppService _appService;

        public PublicationFunction(IConfiguration config,
                                   AppService appService)
        {
            _configuration = config;
            _appService = appService;
        }

        [FunctionName("PublicationFunction")]
        public async Task Run([TimerTrigger("0 0 0 * * *")] TimerInfo timer, ILogger log)
        {
            log.LogInformation($"CPS Publication Timer trigger function started at: {DateTime.Now}");

            string scope = _configuration.GetValue<string>("Settings:Scope");
            string baseUrl = _configuration.GetValue<string>("Settings:BaseUrl");

            if (string.IsNullOrEmpty(scope)) throw new Exception("Scope cannot be empty");
            if (string.IsNullOrEmpty(baseUrl)) throw new Exception("BaseUrl cannot be empty");

            await _appService.callService(baseUrl, scope, "/Export/publish", log);
        }
    }
}