using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using CPS_API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

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

            // Add Repos
            services.AddScoped<IFilesRepository, FilesRepository>();
            services.AddScoped<IObjectIdRepository, ObjectIdRepository>();
            services.AddScoped<IDriveRepository, DriveRepository>();
            services.AddSingleton<ISettingsRepository, SettingsRepository>();

            // Add Custom Services
            services.AddSingleton<FileStorageService, FileStorageService>();
            services.AddSingleton<StorageTableService, StorageTableService>();
            services.AddSingleton<XmlExportSerivce, XmlExportSerivce>();

            // Configure for large file uploads
            services.Configure<FormOptions>(opt =>
            {
                opt.MultipartBodyLengthLimit = int.MaxValue;
            });

            // Application Insights
            services.AddApplicationInsightsTelemetry();
            services.AddSingleton<ILoggerFactory, LoggerFactory>();

            // Add GlobalSettings
            var globalSettings = Configuration.GetSection("GlobalSettings");
            services.Configure<GlobalSettings>(globalSettings);

            string[] initialScopes = Configuration.GetValue<string>("DownstreamApi:Scopes")?.Split(' ');

            services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(Configuration)
                .EnableTokenAcquisitionToCallDownstreamApi(initialScopes)
                .AddMicrosoftGraph(Configuration.GetSection("DownstreamApi"))
                .AddInMemoryTokenCaches(options => options.AbsoluteExpirationRelativeToNow = new TimeSpan(0, 30, 0)); // cache 30 min max

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
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(Configuration)
                .EnableTokenAcquisitionToCallDownstreamApi()
                .AddInMemoryTokenCaches();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
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
