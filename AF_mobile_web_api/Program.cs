using AF_mobile_web_api.Mappings;
using AF_mobile_web_api.Middleware;
using AF_mobile_web_api.Repositories;
using AF_mobile_web_api.Repositories.Interfaces;
using AF_mobile_web_api.Services;
using AF_mobile_web_api.Services.Interfaces;
using ApplicationDatabase;
using Microsoft.EntityFrameworkCore;
using System;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Get the port from environment variable (Render sets this automatically)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5016";
builder.WebHost.UseUrls($"http://*:{port}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("ConnectionString"),
        new MySqlServerVersion(new Version(7, 0, 0)) // adjust version
    ));
builder.Services.AddAutoMapper(typeof(MappingProfile));


builder.Services.AddHttpClient(); // Still needed for the factory!
builder.Services.AddTransient<IHTTPClientServices, HTTPClientServices>();

builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

builder.Services.AddScoped<IPropertyDataRepository, PropertyDataRepository>();
builder.Services.AddScoped<IRealEstateRepository, RealEstateRepository>();

builder.Services.AddScoped<IOLXAPIService, OLXAPIService>();
builder.Services.AddScoped<INieruchomosciOnlineService, NieruchomosciOnlineService>();
builder.Services.AddScoped<IMorizonApiService, MorizonApiService>();
builder.Services.AddScoped<IRealEstateServices, RealEstateServices>();
builder.Services.AddScoped<IStatisticServices, StatisticServices>();


builder.Services.AddMemoryCache();//TODO add redis cache for better performance and scalability

builder.Services.AddControllers();  

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventSourceLogger();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

var allowedFrontendUrl = builder.Configuration["AllowedOrigins:Frontend"]
                         ?? "http://localhost:4200";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder => builder
            .WithOrigins(allowedFrontendUrl)
            .AllowAnyMethod()
            .AllowAnyHeader());
});

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

app.UseCors("AllowSpecificOrigin");

app.UseAuthorization();

app.MapControllers();

app.Run();
