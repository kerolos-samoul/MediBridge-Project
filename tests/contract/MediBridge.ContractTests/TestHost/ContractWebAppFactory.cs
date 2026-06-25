using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using MediBridge.Core.Interfaces.Files;
using MediBridge.Core.Interfaces.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.ContractTests.TestHost;

public sealed class ContractWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"MediBridge.ContractTests_{Guid.NewGuid():N}";
    private readonly TestEnvironmentScope environmentScope;

    public ContractWebAppFactory()
    {
        environmentScope = new TestEnvironmentScope(new Dictionary<string, string?>
        {
            ["ConnectionStrings__DefaultConnection"] = ConnectionString,
            ["Jwt__Issuer"] = "MediBridge.ContractTests",
            ["Jwt__Audience"] = "MediBridge.ContractTests.ApiClients",
            ["Jwt__SigningKey"] = "ContractTestSigningKey-ReplaceBeforeProduction-32Chars",
            ["Identity__SeedDevelopmentAdmin"] = "false",
            ["FileStorage__UploadsEnabled"] = "false",
            ["ContactVerification__OneTimeSecretHashingKey"] = "contract-test-contact-verification-hashing-key",
            ["ContactVerification__AllowOverrideRecipientEmail"] = "false",
            ["PasswordReset__ResetLinkBaseUri"] = "https://localhost/reset-password"
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
                ["Jwt:Issuer"] = "MediBridge.ContractTests",
                ["Jwt:Audience"] = "MediBridge.ContractTests.ApiClients",
                ["Jwt:SigningKey"] = "ContractTestSigningKey-ReplaceBeforeProduction-32Chars",
                ["Identity:SeedDevelopmentAdmin"] = "false",
                ["FileStorage:UploadsEnabled"] = "false",
                ["ContactVerification:OneTimeSecretHashingKey"] = "contract-test-contact-verification-hashing-key",
                ["ContactVerification:AllowOverrideRecipientEmail"] = "false",
                ["PasswordReset:ResetLinkBaseUri"] = "https://localhost/reset-password"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFileStorageProvider>();
            services.AddSingleton<IFileStorageProvider, ContractFileStorageProvider>();
            services.RemoveAll<IEmailDelivery>();
            services.AddSingleton<InMemoryEmailDelivery>();
            services.AddSingleton<IEmailDelivery>(provider => provider.GetRequiredService<InMemoryEmailDelivery>());
            services.AddSingleton<IStartupFilter, ForceHttpsStartupFilter>();
            services.Configure<HttpsRedirectionOptions>(options => options.HttpsPort = 443);
        });

    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.MigrateAsync();
    }

    public string GetLatestContactVerificationCode(string destination)
    {
        return Services.GetRequiredService<InMemoryEmailDelivery>()
            .GetLatestSecret(destination, "ContactVerification")
            ?? throw new InvalidOperationException("No contact verification message was delivered.");
    }

    public int DeliveredEmailCount => Services.GetRequiredService<InMemoryEmailDelivery>().Count;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            environmentScope.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void ConfigureClient(HttpClient client)
    {
        client.BaseAddress = new Uri("https://localhost");
    }

    private string ConnectionString => $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;";

    private sealed class ContractFileStorageProvider : IFileStorageProvider
    {
        public Task<FileStorageUploadResult> UploadAsync(
            FileStorageUpload request,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            var storageKey = $"contract/{Guid.NewGuid():N}/{Path.GetFileName(request.OriginalFileName)}";
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

    private sealed class InMemoryEmailDelivery : IEmailDelivery
    {
        private readonly List<DeliveredMessage> messages = new();
        private readonly object gate = new();

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return messages.Count;
                }
            }
        }

        public string? GetLatestSecret(string destination, string kind)
        {
            lock (gate)
            {
                return messages
                    .Where(message => string.Equals(message.Destination, destination, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(message.Kind, kind, StringComparison.Ordinal))
                    .OrderBy(message => message.ExpiresAtUtc)
                    .LastOrDefault()
                    ?.Secret;
            }
        }

        public Task SendContactVerificationAsync(
            string destination,
            string oneTimeCode,
            DateTime expiresAtUtc,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                messages.Add(new DeliveredMessage(destination, "ContactVerification", oneTimeCode, expiresAtUtc));
            }

            return Task.CompletedTask;
        }

        public Task SendPasswordResetAsync(
            string destination,
            string resetToken,
            DateTime expiresAtUtc,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                messages.Add(new DeliveredMessage(destination, "PasswordReset", resetToken, expiresAtUtc));
            }

            return Task.CompletedTask;
        }
    }

    private sealed record DeliveredMessage(string Destination, string Kind, string Secret, DateTime ExpiresAtUtc);

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
