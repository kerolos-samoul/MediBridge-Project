using System.Collections.Generic;
using MediBridge.APIs.Config;
using MediBridge.Services.Config;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Core.Interfaces.Identity;
using Xunit;

namespace MediBridge.IntegrationTests;

public class ConfigurationBindingTests
{
    [Fact]
    public void ApplicationStartsWithCloudinaryProvider_WhenUploadsAreEnabledAndCloudinaryUrlExists()
    {
        using var cloudinaryUrl = new EnvironmentVariableScope(
            CloudinaryStorageOptions.SecretEnvironmentVariableName,
            "configured-cloudinary-url");
        var inMemory = new Dictionary<string, string?>
        {
            ["FileStorage:UploadsEnabled"] = "true"
        };

        using var factory = new WebAppFactory();
        var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(inMemory));
        });

        using var scope = configuredFactory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IFileStorageProvider>();

        Assert.Equal("CloudinaryFileStorageProvider", provider.GetType().Name);
    }

    [Fact]
    public void ApplicationStartsWithCloudinaryProvider_WhenUploadsAreEnabledAndCloudinaryUrlIsConfigured()
    {
        using var cloudinaryUrl = new EnvironmentVariableScope(
            CloudinaryStorageOptions.SecretEnvironmentVariableName,
            null);
        var inMemory = new Dictionary<string, string?>
        {
            ["FileStorage:UploadsEnabled"] = "true",
            ["CloudinaryStorage:CloudinaryUrl"] = "cloudinary" + "://config-key:config-secret@example"
        };

        using var factory = new WebAppFactory();
        var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(inMemory));
        });

        using var scope = configuredFactory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IFileStorageProvider>();

        Assert.Equal("CloudinaryFileStorageProvider", provider.GetType().Name);
    }

    [Fact]
    public void ApplicationStartupFailsClearly_WhenUploadsAreEnabledAndCloudinaryUrlIsMissing()
    {
        using var cloudinaryUrl = new EnvironmentVariableScope(
            CloudinaryStorageOptions.SecretEnvironmentVariableName,
            null);
        var inMemory = new Dictionary<string, string?>
        {
            ["FileStorage:UploadsEnabled"] = "true",
            ["CloudinaryStorage:CloudinaryUrl"] = ""
        };

        using var factory = new WebAppFactory();
        var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(inMemory));
        });

        var exception = Assert.Throws<OptionsValidationException>(() => configuredFactory.Services.CreateScope());

        Assert.Contains(
            "CLOUDINARY_URL environment variable is required when uploads are enabled.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationStartsWithDisabledProvider_WhenUploadsAreDisabledAndCloudinaryUrlIsMissing()
    {
        using var cloudinaryUrl = new EnvironmentVariableScope(
            CloudinaryStorageOptions.SecretEnvironmentVariableName,
            null);
        var inMemory = new Dictionary<string, string?>
        {
            ["FileStorage:UploadsEnabled"] = "false"
        };

        using var factory = new WebAppFactory();
        var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(inMemory));
        });

        using var scope = configuredFactory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IFileStorageProvider>();

        Assert.Equal("DisabledFileStorageProvider", provider.GetType().Name);
    }

    [Fact]
    public void JwtAndDatabaseOptions_AreBoundFromConfiguration()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-aud",
            ["Jwt:SigningKey"] = "super-secret-key",
            ["ConnectionStrings:DefaultConnection"] = "Server=.;Database=Test;Trusted_Connection=True;",
            ["Identity:SeedDevelopmentAdmin"] = "false"
        };

        using var factory = new WebAppFactory();
        var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(inMemory));
        });

        using var scope = configuredFactory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;
        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();
        var db = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.Equal("test-aud", jwt.Audience);
        Assert.Equal("super-secret-key", jwt.SigningKey);
        Assert.Equal(TimeSpan.FromMinutes(60), tokenService.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(7), tokenService.RefreshTokenLifetime);
        Assert.Equal("Server=.;Database=Test;Trusted_Connection=True;", db.DefaultConnection);
    }

    [Fact]
    public void IdentityTokenService_UsesDefaultTokenLifetimesWhenConfigurationOmitsLifetimeKeys()
    {
        using var factory = new WebAppFactory();
        using var scope = factory.Services.CreateScope();

        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();

        Assert.Equal(TimeSpan.FromMinutes(60), tokenService.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(7), tokenService.RefreshTokenLifetime);
    }

    [Fact]
    public void JwtAndDatabaseOptions_RejectMissingRequiredValues()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "",
            ["Jwt:Audience"] = "",
            ["Jwt:SigningKey"] = "",
            ["ConnectionStrings:DefaultConnection"] = "",
            ["Identity:SeedDevelopmentAdmin"] = "false"
        };

        using var factory = new WebAppFactory();
        var configuredFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(inMemory));
        });

        var exception = Assert.Throws<AggregateException>(() => configuredFactory.Services.CreateScope());

        Assert.Contains(exception.InnerExceptions, inner => inner is OptionsValidationException validation &&
            validation.OptionsType == typeof(JwtOptions));
        Assert.Contains(exception.InnerExceptions, inner => inner is OptionsValidationException validation &&
            validation.OptionsType == typeof(DatabaseOptions));
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private readonly string name;
        private readonly string? previousValue;
        private bool disposed;

        public EnvironmentVariableScope(string name, string? value)
        {
            Gate.Wait();
            this.name = name;
            previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Environment.SetEnvironmentVariable(name, previousValue);
            Gate.Release();
            disposed = true;
        }
    }
}
