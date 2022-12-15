using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Graph;
using Microsoft.Identity.Web;
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
builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

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
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();