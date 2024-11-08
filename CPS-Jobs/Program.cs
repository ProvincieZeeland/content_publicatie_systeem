using CPS_Jobs.Helpers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddScoped<AppService, AppService>();

        var configuration = context.Configuration;
        services
            .AddAuthentication(sharedOptions =>
            {
                sharedOptions.DefaultScheme = Constants.Bearer;
                sharedOptions.DefaultChallengeScheme = Constants.Bearer;
            })
            .AddMicrosoftIdentityWebApi(configuration)
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
    })
    .ConfigureLogging((context, logging) =>
    {
        string? appInsightsConnectionString = context.Configuration.GetValue<string>("APPLICATIONINSIGHTS_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
        {
            logging.AddApplicationInsights(
                configureTelemetryConfiguration: (config) =>
                    config.ConnectionString = appInsightsConnectionString,
                    configureApplicationInsightsLoggerOptions: (options) => { }
                );
        }
    })
    .Build();

host.Run();