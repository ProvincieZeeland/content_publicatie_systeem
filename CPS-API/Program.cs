using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Repos
builder.Services.AddScoped<IFilesRepository, FilesRepository>();
builder.Services.AddScoped<IObjectIdRepository, ObjectIdRepository>();
builder.Services.AddScoped<IDriveRepository, DriveRepository>();
builder.Services.AddSingleton<ISettingsRepository, SettingsRepository>();

// Add Custom Services
builder.Services.AddSingleton<FileStorageService, FileStorageService>();
builder.Services.AddSingleton<StorageTableService, StorageTableService>();

// Configure for large file uploads
builder.Services.Configure<FormOptions>(opt =>
{
    opt.MultipartBodyLengthLimit = int.MaxValue;
});

// Application Insights
builder.Services.AddApplicationInsightsTelemetry();


// Add GlobalSettings
var globalSettings = builder.Configuration.GetSection("GlobalSettings");
builder.Services.Configure<GlobalSettings>(globalSettings);

// Set up authentication for Graph
//builder.Services.AddAuthorization(options =>
//{
//    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
//        .RequireAuthenticatedUser()
//        .Build();
//});

// For user calls
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
                .EnableTokenAcquisitionToCallDownstreamApi()
                .AddMicrosoftGraph()
                .AddMicrosoftGraphAppOnly(authenticationProvider => new GraphServiceClient(authenticationProvider))
                .AddInMemoryTokenCaches();

builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});
builder.Services.AddRazorPages()
     .AddMicrosoftIdentityUI();


// For API calls
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration)
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddMicrosoftGraphAppOnly(authenticationProvider => new GraphServiceClient(authenticationProvider))
    .AddInMemoryTokenCaches();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
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

app.Run();