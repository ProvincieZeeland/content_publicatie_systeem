using CPS_API.Helpers;
using CPS_API.Models;
using CPS_API.Repositories;
using Microsoft.AspNetCore.Http.Features;
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Repos
builder.Services.AddSingleton<IFilesRepository, FilesRepository>();
builder.Services.AddSingleton<IContentIdRepository, ContentIdRepository>();

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
builder.Services.AddOptions();
var globalSettings = builder.Configuration.GetSection("GlobalSettings");
builder.Services.Configure<GlobalSettings>(globalSettings);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

// Add MSAL

// Add MSGraphCLient
await GraphHelper.SignInUserAndInitializeGraphUsingMSAL();

app.MapControllers();

app.Run();