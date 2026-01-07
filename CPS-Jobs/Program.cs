using System;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using CPS_Jobs.Helpers;
using CPS_Jobs.Models;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using OpenTelemetry.Trace;


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

        // Analytics
        services.AddOpenTelemetry()
                 .UseAzureMonitor(options =>
                 {
                     options.Credential = new DefaultAzureCredential();
                     options.EnableLiveMetrics = true;
                 })
                 .WithTracing(traceBuilder =>
                    traceBuilder
                    .AddAspNetCoreInstrumentation()
                    .AddProcessor(
                        new AppInsightsTelemetryProcessor(
                            services.BuildServiceProvider().GetRequiredService<ILogger<AppInsightsTelemetryProcessor>>()
                        )
                    )
                )
                 .UseFunctionsWorkerDefaults();



    })
    .Build();

host.Run();