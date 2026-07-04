using MediBridge.APIs.Contracts;
using MediBridge.APIs.Extensions;
using MediBridge.Repository.Data.Identity;
using MediBridge.Repository.Extensions;
using MediBridge.Services.Config;
using MediBridge.Services.Extensions;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using MediBridge.APIs.Config;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

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
builder.Services.AddMediBridgeRepository(builder.Configuration);
builder.Services.AddMediBridgeIdentityServices(builder.Configuration);
builder.Services.AddMediBridgeDeliveryJobs(builder.Configuration);
builder.Services.AddScoped<IWeatherForecastQueryService, WeatherForecastQueryService>();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

var deliveryJobOptions = app.Services.GetRequiredService<IOptions<DeliveryJobOptions>>().Value;
if (deliveryJobOptions.Enabled)
{
    app.Services.GetRequiredService<RecurringDeliveryJobRegistrar>().Register();
    using var recoveryScope = app.Services.CreateScope();
    await recoveryScope.ServiceProvider
        .GetRequiredService<IDeliveryJobRecoveryCoordinator>()
        .RecoverAsync(app.Lifetime.ApplicationStopping);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Identity:SeedDevelopmentAdmin"))
{
    await AdminIdentitySeed.EnsureDevelopmentAdminAsync(app.Services, builder.Configuration);
}

app.UseFoundationPipeline();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseHttpsRedirection();
app.UseEnvelopeStatusCodePages();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
