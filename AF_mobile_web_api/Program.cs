using AF_mobile_web_api.Mappings;
using AF_mobile_web_api.Middleware;
using AF_mobile_web_api.Repositories;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO.Compression;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Get the port from environment variable (Render sets this automatically)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5016";
builder.WebHost.UseUrls($"http://*:{port}");

// Optional override, e.g. "8.0.36-mysql" or "10.6.14-mariadb"; the fallback version is
// deliberately kept as-is - it selects the conservative feature set today's prod SQL was
// generated with, and changing it blindly could alter query translation on a live DB.
var serverVersionSetting = builder.Configuration["Database:ServerVersion"];
var serverVersion = string.IsNullOrWhiteSpace(serverVersionSetting)
    ? new MySqlServerVersion(new Version(7, 0, 0))
    : ServerVersion.Parse(serverVersionSetting);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("ConnectionString"),
        serverVersion
    ));
builder.Services.AddAutoMapper(typeof(MappingProfile));


builder.Services.AddHttpClient(); // Still needed for the factory!
builder.Services.AddTransient<IHTTPClientServices, HTTPClientServices>();

builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

builder.Services.AddScoped<IPropertyDataRepository, PropertyDataRepository>();

builder.Services.AddScoped<IPropertyComparer, PropertyComparer>();

builder.Services.AddScoped<IOLXAPIService, OLXAPIService>();
builder.Services.AddScoped<INieruchomosciOnlineService, NieruchomosciOnlineService>();
builder.Services.AddScoped<IMorizonApiService, MorizonApiService>();
builder.Services.AddScoped<IRealEstateServices, RealEstateServices>();
builder.Services.AddScoped<IStatisticServices, StatisticServices>();
builder.Services.AddScoped<IPropertyListService, PropertyListService>();

// Singleton on purpose: it owns the "one scrape at a time" flag shared by all requests.
builder.Services.AddSingleton<IScrapeJobRunner, ScrapeJobRunner>();

// Singleton on purpose: the deduplicated offers set costs a full-table scan to build, so
// every request shares one copy of it until a scrape replaces the rows it was built from.
builder.Services.AddSingleton<IOfferSnapshotCache, OfferSnapshotCache>();

// Builds the caches in the background at startup, so the first visitor after a restart
// (on Render, that is every visitor after an idle period) does not pay for them.
builder.Services.AddHostedService<CacheWarmupService>();

builder.Services.AddMemoryCache();//TODO add redis cache for better performance and scalability

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    // Brotli first: better ratio than gzip, supported by every modern browser
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

// The default for both providers is CompressionLevel.Fastest, which on this API's biggest
// response (the map points) spent its speed on nothing: Optimal compresses the same payload
// to well under half the bytes for a few milliseconds more, and these responses are served
// from cache, so the bytes on the wire are what the visitor actually waits for.
builder.Services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);
builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Optimal);

builder.Services.AddControllers();  

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventSourceLogger();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// "AllowedOrigins:Frontend" may hold several origins separated by commas
var allowedOrigins = (builder.Configuration["AllowedOrigins:Frontend"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (allowedOrigins.Length == 0)
{
    allowedOrigins = new[] { "http://localhost:4200" };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder => builder
            .WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader());
});

var app = builder.Build();

app.UseResponseCompression();

app.UseMiddleware<ExceptionMiddleware>();

app.UseCors("AllowSpecificOrigin");

app.UseAuthorization();

app.MapControllers();

app.Run();
