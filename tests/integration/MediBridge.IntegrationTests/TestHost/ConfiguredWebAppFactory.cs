using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MediBridge.Repository.Data;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using MediBridge.TestInfrastructure;

namespace MediBridge.IntegrationTests.TestHost;

public abstract class ConfiguredWebAppFactory : WebApplicationFactory<Program>
{
    private readonly LocalDbTestDatabase testDatabase = new("MediBridge.IntegrationTests");
    private readonly TestEnvironmentScope environmentScope;
    private int asyncDisposalStarted;

    protected ConfiguredWebAppFactory()
    {
        environmentScope = new TestEnvironmentScope(new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = ConnectionString,
            ["Jwt__Issuer"] = "MediBridge.IntegrationTests",
            ["Jwt__Audience"] = "MediBridge.IntegrationTests.ApiClients",
            ["Jwt__SigningKey"] = "IntegrationTestSigningKey-ReplaceBeforeProduction-32Chars",
            ["Email__Smtp__Host"] = "localhost",
            ["Email__Smtp__Port"] = "2525",
            ["Email__Smtp__Username"] = "integration-test-smtp-user",
            ["Email__Smtp__Password"] = "integration-test-smtp-password",
            ["Email__Smtp__FromEmail"] = "no-reply.integration@example.com",
            ["FileStorage__UploadsEnabled"] = "false",
            ["DeliveryJobs__Enabled"] = "false",
            ["Identity__SeedDevelopmentAdmin"] = "false"
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
                ["Email:Smtp:Host"] = "localhost",
                ["Email:Smtp:Port"] = "2525",
                ["Email:Smtp:Username"] = "integration-test-smtp-user",
                ["Email:Smtp:Password"] = "integration-test-smtp-password",
                ["Email:Smtp:FromEmail"] = "no-reply.integration@example.com",
                ["FileStorage:UploadsEnabled"] = "false",
                ["DeliveryJobs:Enabled"] = "false",
                ["Identity:SeedDevelopmentAdmin"] = "false"
            });
        });
        ConfigureAppConfigurationCore(builder);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStartupFilter, ForceHttpsStartupFilter>();
            services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<TestEmailSink>();
            services.AddSingleton<IEmailSender, TestEmailSender>();
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

    private string ConnectionString => testDatabase.ConnectionString;

    protected virtual void ConfigureWebHostCore(IWebHostBuilder builder)
    {
    }

    protected virtual void ConfigureAppConfigurationCore(IWebHostBuilder builder)
    {
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing && Volatile.Read(ref asyncDisposalStarted) == 0)
            {
                try
                {
                    testDatabase.Delete();
                }
                finally
                {
                    environmentScope.Dispose();
                }
            }
        }
    }

    public override async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref asyncDisposalStarted, 1);

        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            try
            {
                await testDatabase.DeleteAsync();
            }
            finally
            {
                environmentScope.Dispose();
            }
        }
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
