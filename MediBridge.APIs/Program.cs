using MediBridge.APIs.Contracts;
using MediBridge.APIs.Extensions;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
            ApiEnvelopeFactory.Create<object?>(
                StatusCodes.Status400BadRequest,
                "Validation failed.",
                null));
    });

// Register API explorer always; enable Swagger only in Development to avoid
// exposing API documentation in non-development environments.
builder.Services.AddEndpointsApiExplorer();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen();
}

builder.Services.AddFoundationServices(builder.Configuration);
builder.Services.AddScoped<IWeatherForecastQueryService, WeatherForecastQueryService>();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseFoundationPipeline();
app.UseRouting();
app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
