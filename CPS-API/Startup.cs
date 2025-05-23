﻿using Azure.Core;
using Azure.Identity;
using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using CPS_API.Services;
using IExperts.SocialIntranet.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Azure;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Constants = Microsoft.Identity.Web.Constants;

namespace CPS_API
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add services to the container.
            services.AddControllers();
            services.AddEndpointsApiExplorer();
            services.AddMemoryCache();

            // Add Repos
            services.AddScoped<IFilesRepository, FilesRepository>();
            services.AddScoped<IObjectIdRepository, ObjectIdRepository>();
            services.AddScoped<IDriveRepository, DriveRepository>();
            services.AddScoped<IMetadataRepository, MetadataRepository>();
            services.AddSingleton<ISettingsRepository, SettingsRepository>();
            services.AddScoped<IWebHookRepository, WebHookRepository>();
            services.AddScoped<IExportRepository, ExportRepository>();
            services.AddScoped<IPublicationRepository, PublicationRepository>();
            services.AddScoped<ICallbackRepository, CallbackRepository>();
            services.AddScoped<IListRepository, ListRepository>();
            services.AddScoped<ISharePointRepository, SharePointRepository>();

            // Add Custom Services
            services.AddSingleton<FileStorageService, FileStorageService>();
            services.AddSingleton<StorageTableService, StorageTableService>();
            services.AddSingleton<XmlExportSerivce, XmlExportSerivce>();
            services.AddSingleton<EmailService, EmailService>();
            services.AddSingleton<CertificateService, CertificateService>();
            services.AddSingleton<IRestClient, RestClient>();

            services
                .AddHttpClient("restClient")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    EnableMultipleHttp2Connections = true
                });

            // Configure for large file uploads
            services.Configure<FormOptions>(opt =>
            {
                opt.ValueLengthLimit = int.MaxValue;
                opt.MultipartBodyLengthLimit = int.MaxValue;
            });

            // Application Insights
            services.AddApplicationInsightsTelemetry();

            // Add GlobalSettings
            var globalSettings = Configuration.GetSection("GlobalSettings");
            services.Configure<GlobalSettings>(globalSettings);

            string[]? initialScopes = Configuration.GetValue<string>("DownstreamApi:Scopes")?.Split(' ');

            services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(Configuration.GetSection("AzureAd"))
                .EnableTokenAcquisitionToCallDownstreamApi(initialScopes)
                .AddMicrosoftGraph(Configuration.GetSection("DownstreamApi"))
                .AddInMemoryTokenCaches(options => options.AbsoluteExpirationRelativeToNow = new TimeSpan(0, 30, 0)); // cache 30 min max

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

            services.AddControllersWithViews(options =>
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
                options.Filters.Add(new AuthorizeFilter(policy));
            });

            services.AddRazorPages()
             .AddMicrosoftIdentityUI();

            // Add the UI support to handle claims challenges
            services.AddServerSideBlazor()
               .AddMicrosoftIdentityConsentHandler();

            // For API calls
            services
                 .AddAuthentication(sharedOptions =>
                 {
                     sharedOptions.DefaultScheme = Constants.Bearer;
                     sharedOptions.DefaultChallengeScheme = Constants.Bearer;
                 })
                 .AddMicrosoftIdentityWebApi(Configuration)
                 .EnableTokenAcquisitionToCallDownstreamApi()
                 .AddInMemoryTokenCaches();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                ExceptionHandler = async ctx =>
                {
                    var feature = ctx.Features.Get<IExceptionHandlerFeature>();
                    if (feature?.Error is MsalUiRequiredException
                        or { InnerException: MsalUiRequiredException }
                        or { InnerException.InnerException: MsalUiRequiredException })
                    {
                        ctx.Response.Cookies.Delete($"{CookieAuthenticationDefaults.CookiePrefix}{CookieAuthenticationDefaults.AuthenticationScheme}");
                        ctx.Response.Redirect(ctx.Request.GetEncodedPathAndQuery());
                    }
                    else
                    {
                        ctx.Response.Redirect("/Home/Error");
                    }
                }
            });

            if (!env.IsDevelopment())
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapRazorPages();
            });
        }
    }
}