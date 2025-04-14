using System;
using System.Threading.Tasks;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CPS_Jobs
{
    public class PublicationFunction
    {
        private readonly ILogger<PublicationFunction> _logger;
        private readonly GlobalSettings _globalSettings;
        private readonly AppService _appService;

        public PublicationFunction(ILogger<PublicationFunction> logger, IOptions<GlobalSettings> config, AppService appService)
        {
            _logger = logger;
            _appService = appService;
            _globalSettings = config.Value;
        }

        [Function("PublicationFunction")]
        public async Task Run([TimerTrigger("0 0 0 * * *")] TimerInfo timer)
        {
            _logger.LogInformation("CPS Publication Timer trigger function started at: {Now}", DateTime.Now);

            if (string.IsNullOrEmpty(_globalSettings.Scope)) throw new CpsException("Scope cannot be empty");
            if (string.IsNullOrEmpty(_globalSettings.BaseUrl)) throw new CpsException("BaseUrl cannot be empty");

            await _appService.GetAsync(_globalSettings.BaseUrl, _globalSettings.Scope, "/Export/publish");
        }
    }
}