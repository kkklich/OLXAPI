using System;
using System.Reflection;
using AF_mobile_web_api.Mappings;
using AF_mobile_web_api.Services;
using ApplicationDatabase;
using Microsoft.EntityFrameworkCore;

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

// Add services to the container.
builder.Services
    .AddScoped<HTTPClientServices>()
    .AddScoped<RealEstateServices>()
    .AddScoped<StatisticServices>()
    .AddScoped<MorizonApiService>()
    .AddScoped<OLXAPIService>()
    .AddScoped<NieruchomosciOnlineService>();    

builder.Services.AddControllers();  

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.AddEventSourceLogger();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder => builder
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.UseSwagger();
    //app.UseSwaggerUI();
}

app.UseCors("AllowSpecificOrigin");

app.UseAuthorization();

app.MapControllers();

app.Run();
