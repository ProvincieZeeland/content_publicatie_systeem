﻿using CPS_Jobs.Helpers;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;

[assembly: FunctionsStartup(typeof(CPS_Jobs.Startup))]
namespace CPS_Jobs
{
    public class Startup : FunctionsStartup
    {
        public Startup()
        {
        }

        public override void Configure(IFunctionsHostBuilder builder)
        {
            var configuration = builder.GetContext().Configuration;

            // Add Custom Services
            builder.Services.AddScoped<AppService, AppService>();

            builder.Services
                .AddAuthentication(sharedOptions =>
                {
                    sharedOptions.DefaultScheme = Constants.Bearer;
                    sharedOptions.DefaultChallengeScheme = Constants.Bearer;
                })
                .AddMicrosoftIdentityWebApi(configuration)
                .EnableTokenAcquisitionToCallDownstreamApi()
                .AddInMemoryTokenCaches();
        }
    }

}

