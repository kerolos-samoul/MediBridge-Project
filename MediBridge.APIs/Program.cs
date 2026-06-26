using MediBridge.APIs.Contracts;
using MediBridge.APIs.Extensions;
using MediBridge.Repository.Data.Identity;
using MediBridge.Repository.Extensions;
using MediBridge.Services.Config;
using MediBridge.Services.Extensions;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

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
builder.Services.AddScoped<IWeatherForecastQueryService, WeatherForecastQueryService>();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = null;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();
Program.LogTemporaryStartupDiagnostics(app, builder.Configuration);

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

public partial class Program
{
    public static void LogTemporaryStartupDiagnostics(WebApplication app, IConfiguration configuration)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TemporaryStartupDiagnostics");

        var smtp = new SmtpEmailOptions();
        configuration.GetSection(SmtpEmailOptions.SectionName).Bind(smtp);
        var contactVerification = new ContactVerificationOptions();
        configuration.GetSection(ContactVerificationOptions.SectionName).Bind(contactVerification);
        var fileStorage = new FileStorageOptions();
        configuration.GetSection(FileStorageOptions.SectionName).Bind(fileStorage);
        var cloudinary = new CloudinaryStorageOptions();
        configuration.GetSection(CloudinaryStorageOptions.SectionName).Bind(cloudinary);

        var cloudinaryUrl = cloudinary.ResolveCloudinaryUrl();
        var cloudinaryUrlCloudName = TryReadCloudName(cloudinaryUrl);

        logger.LogInformation(
            "TEMP SMTP config bound. Host: {Host}, Port: {Port}, Username: {Username}, FromEmail: {FromEmail}, PasswordConfigured: {PasswordConfigured}",
            smtp.Host,
            smtp.Port,
            smtp.Username,
            smtp.FromEmail,
            !string.IsNullOrWhiteSpace(smtp.Password));

        logger.LogInformation(
            "TEMP contact verification config bound. AllowOverrideRecipientEmail: {AllowOverrideRecipientEmail}, OverrideRecipientEmail: {OverrideRecipientEmail}",
            contactVerification.AllowOverrideRecipientEmail,
            contactVerification.OverrideRecipientEmail);

        logger.LogInformation(
            "TEMP Cloudinary config bound. UploadsEnabled: {UploadsEnabled}, ConfigCloudName: {ConfigCloudName}, CloudinaryUrlConfigured: {CloudinaryUrlConfigured}, CloudinaryUrlCloudName: {CloudinaryUrlCloudName}, UseSecureUrls: {UseSecureUrls}, FolderPrefix: {FolderPrefix}",
            fileStorage.UploadsEnabled,
            cloudinary.CloudName,
            !string.IsNullOrWhiteSpace(cloudinaryUrl),
            cloudinaryUrlCloudName,
            cloudinary.UseSecureUrls,
            cloudinary.FolderPrefix);
    }

    private static string? TryReadCloudName(string? cloudinaryUrl)
    {
        if (string.IsNullOrWhiteSpace(cloudinaryUrl) || !Uri.TryCreate(cloudinaryUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Host;
    }
}
