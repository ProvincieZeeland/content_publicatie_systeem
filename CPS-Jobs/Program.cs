using System;
using Azure.Core;
using Azure.Identity;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;


IHost host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration((context, builder) =>
    {
        // Load local files for debugging
        builder.SetBasePath(context.HostingEnvironment.ContentRootPath)
#if DEBUG
               .AddJsonFile("local.settings.json", optional: true, reloadOnChange: false)
#else
               .AddJsonFile("settings.json", optional: true, reloadOnChange: false)
#endif
               .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        // Analytics
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Load Configuration
        IConfigurationSection globalSettings = context.Configuration.GetSection("GlobalSettings");
        services.Configure<GlobalSettings>(globalSettings);

        services.AddScoped<AppService, AppService>();

        services.AddAuthentication(sharedOptions =>
                {
                    sharedOptions.DefaultScheme = Constants.Bearer;
                    sharedOptions.DefaultChallengeScheme = Constants.Bearer;
                })
            .AddMicrosoftIdentityWebApi(context.Configuration)
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();

        // Setup keyvault
        services.AddAzureClients(builder =>
        {
            var keyVaultName = globalSettings["KeyVaultName"];
            var keyvaultUri = "https://" + keyVaultName + ".vault.azure.net";
            builder.AddSecretClient(new Uri(keyvaultUri));
            builder.ConfigureDefaults(options => options.Retry.Mode = RetryMode.Exponential);

#if DEBUG
            // For debugging, allow us to login via browser and use a local account to access keyvault
            var credential = new VisualStudioCredential();
#else
            var credential = new DefaultAzureCredential();
#endif
            builder.UseCredential(credential);
        });
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