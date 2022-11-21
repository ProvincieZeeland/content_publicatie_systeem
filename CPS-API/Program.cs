using CPS_API.Repositories;
using CPS_API.Services;
using Microsoft.AspNetCore.Http.Features;

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

// Configure for large file uploads
builder.Services.Configure<FormOptions>(opt =>
{
    opt.MultipartBodyLengthLimit = int.MaxValue;
});

// Application Insights
builder.Services.AddApplicationInsightsTelemetry();

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

app.MapControllers();

app.Run();