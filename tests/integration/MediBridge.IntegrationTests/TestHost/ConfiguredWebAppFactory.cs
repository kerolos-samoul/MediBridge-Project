using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using MediBridge.Core.Interfaces.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.IntegrationTests.TestHost;

public abstract class ConfiguredWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"MediBridge.IntegrationTests_{Guid.NewGuid():N}";
    private readonly TestEnvironmentScope environmentScope;

    protected ConfiguredWebAppFactory()
    {
        environmentScope = new TestEnvironmentScope(new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = ConnectionString,
            ["Jwt__Issuer"] = "MediBridge.IntegrationTests",
            ["Jwt__Audience"] = "MediBridge.IntegrationTests.ApiClients",
            ["Jwt__SigningKey"] = "IntegrationTestSigningKey-ReplaceBeforeProduction-32Chars",
            ["Identity__SeedDevelopmentAdmin"] = "false",
            ["FileStorage__UploadsEnabled"] = "false"
        });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Test:InstanceId"] = Guid.NewGuid().ToString("N"),
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Jwt:Issuer"] = "MediBridge.IntegrationTests",
                ["Jwt:Audience"] = "MediBridge.IntegrationTests.ApiClients",
                ["Jwt:SigningKey"] = "IntegrationTestSigningKey-ReplaceBeforeProduction-32Chars",
                ["Identity:SeedDevelopmentAdmin"] = "false",
                ["FileStorage:UploadsEnabled"] = "false"
            });
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFileStorageProvider>();
            services.AddSingleton<IFileStorageProvider, IntegrationFileStorageProvider>();
            services.AddSingleton<IStartupFilter, ForceHttpsStartupFilter>();
            services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
        });

        ConfigureWebHostCore(builder);
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.MigrateAsync();
    }

    protected override void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = new Uri("https://localhost");
    }

    private string ConnectionString => $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;";

    private sealed class IntegrationFileStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResult> UploadAsync(
            FileStorageUpload request,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            var storageKey = $"integration/{Guid.NewGuid():N}/{Path.GetFileName(request.OriginalFileName)}";
            return Task.FromResult(new FileStorageUploadResult(storageKey, "raw"));
        }

        public Task<SignedFileUrl> CreateSignedReadUrlAsync(
            string storageKey,
            string resourceType,
            TimeSpan lifetime,
            CancellationToken cancellationToken = default)
        {
            var url = new Uri($"https://files.test/{Uri.EscapeDataString(storageKey)}");
            return Task.FromResult(new SignedFileUrl(url, DateTime.UtcNow.Add(lifetime)));
        }

        public Task DeleteAsync(
            string storageKey,
            string resourceType,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    protected virtual void ConfigureWebHostCore(IWebHostBuilder builder)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            environmentScope.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class TestEnvironmentScope : IDisposable
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private readonly Dictionary<string, string?> previousValues;
        private bool disposed;

        public TestEnvironmentScope(IReadOnlyDictionary<string, string?> values)
        {
            Gate.Wait();

            previousValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var pair in values)
                {
                    previousValues[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                    Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                }
            }
            catch
            {
                Restore();
                Gate.Release();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Restore();
            Gate.Release();
            disposed = true;
        }

        private void Restore()
        {
            foreach (var pair in previousValues)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
