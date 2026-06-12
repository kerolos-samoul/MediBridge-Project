using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.Config;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class DoctorRegistrationIntegrationTests
{
    [Fact]
    public async Task RegisterDoctor_CreatesPendingUserAndDoctorProfile()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-{Guid.NewGuid():N}@example.com";
        var phoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}";
        var request = CreateDoctorRequest(email, phoneNumber);

        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var profile = await db.DoctorProfiles.SingleAsync(candidate => candidate.UserId == user.Id);

        Assert.Equal("Pending", user.AccountStatus.ToString());
        Assert.Equal("Doctor", user.Role.ToString());
        Assert.False(user.IsDeleted);
        Assert.NotNull(user.PasswordHash);

        Assert.Equal("Cardiology", profile.Specialization);
        Assert.Equal(5, profile.ExperienceYears);
        Assert.Equal("Lagos", profile.Location);
        Assert.Equal("License", profile.VerificationDocumentType);
        Assert.Equal("license.pdf", profile.VerificationOriginalFileName);
        Assert.Equal("application/pdf", profile.VerificationContentType);
        Assert.Equal(1024, profile.VerificationSizeBytes);
        Assert.StartsWith("ref-", profile.VerificationReference, StringComparison.Ordinal);

        Assert.Empty(await db.RefreshCredentials.Where(token => token.UserId == user.Id).ToListAsync());
        var flow = await db.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == user.Id);
        Assert.Equal("Email", flow.Channel.ToString());
        Assert.False(string.IsNullOrWhiteSpace(flow.TokenHash));
        Assert.False(flow.TokenHash.All(char.IsDigit));
        Assert.Equal(flow.TokenHash, flow.TokenHash.Trim());
        Assert.InRange(flow.ExpiresAtUtc - flow.CreatedAtUtc, TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(11));
        Assert.Null(flow.ConsumedAtUtc);

        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();
        var emailSink = factory.Services.GetRequiredService<TestEmailSink>();
        var message = Assert.Single(emailSink.Messages);
        var otp = Regex.Match(message.Body, "\\b\\d{6}\\b").Value;
        Assert.False(string.IsNullOrWhiteSpace(otp));
        Assert.Equal("medibridge7@gmail.com", message.RecipientEmail);
        Assert.Contains(email, message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(flow.TokenHash, tokenService.HashToken(otp));
        Assert.NotEqual(otp, flow.TokenHash);
        Assert.NotEmpty(await db.AuthenticationAuditEvents.Where(eventItem => eventItem.TargetUserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task RegisterDoctor_WhenOverrideDisabled_SendsOtpToRegisteredEmail()
    {
        await using var factory = new RegistrationOptionsFactory(new Dictionary<string, string?>
        {
            ["ContactVerification:AllowOverrideRecipientEmail"] = "false"
        });
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = $"doctor-no-override-{Guid.NewGuid():N}@example.com";
        var request = CreateDoctorRequest(email, $"555{Random.Shared.Next(1000000, 9999999)}");

        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var emailSink = factory.Services.GetRequiredService<TestEmailSink>();
        var message = Assert.Single(emailSink.Messages);
        Assert.Equal(email, message.RecipientEmail);
        Assert.Contains(email, message.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterDoctor_EmailDeliveryFailureLogsDiagnosticsWithoutRawOtpPasswordOrSecrets()
    {
        var logSink = new TestLogSink();
        await using var factory = new RegistrationOptionsFactory(
            new Dictionary<string, string?>
            {
                ["ContactVerification:AllowOverrideRecipientEmail"] = "true",
                ["ContactVerification:OverrideRecipientEmail"] = "medibridge7@gmail.com"
            },
            logSink);
        await factory.InitializeDatabaseAsync();

        var emailSink = factory.Services.GetRequiredService<TestEmailSink>();
        emailSink.ThrowOnSend = true;
        using var client = factory.CreateClient();

        var email = $"doctor-log-safety-{Guid.NewGuid():N}@example.com";
        const string password = "Password1!";
        using var response = await client.PostAsJsonAsync("/api/auth/register-doctor", new
        {
            Email = email,
            Password = password,
            PhoneNumber = $"555{Random.Shared.Next(1000000, 9999999)}",
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationMetadata = Phase6IdentityTestHelpers.CreateVerificationMetadata()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var flow = await db.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == user.Id);
        var logs = string.Join(Environment.NewLine, logSink.Messages);

        Assert.Contains("Email OTP delivery failed", logs, StringComparison.Ordinal);
        Assert.Contains(user.Id, logs, StringComparison.Ordinal);
        Assert.Contains("medibridge7@gmail.com", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(flow.TokenHash, logs, StringComparison.Ordinal);
        Assert.DoesNotMatch("\\b\\d{6}\\b", logs);
        Assert.DoesNotContain("SmtpPassword", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SMTP password", logs, StringComparison.OrdinalIgnoreCase);
    }

    private static object CreateDoctorRequest(string email, string phoneNumber)
    {
        return new
        {
            Email = email,
            Password = "Password1!",
            PhoneNumber = phoneNumber,
            Specialization = "Cardiology",
            ExperienceYears = 5,
            Location = "Lagos",
            VerificationMetadata = new
            {
                DocumentType = "License",
                OriginalFileName = "license.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                Reference = $"ref-{Guid.NewGuid():N}"
            }
        };
    }

    private sealed class RegistrationOptionsFactory : ConfiguredWebAppFactory
    {
        private readonly IReadOnlyDictionary<string, string?> configuration;
        private readonly TestLogSink? logSink;

        public RegistrationOptionsFactory(IReadOnlyDictionary<string, string?> configuration, TestLogSink? logSink = null)
        {
            this.configuration = configuration;
            this.logSink = logSink;
        }

        protected override void ConfigureAppConfigurationCore(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
        }

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices((context, services) =>
            {
                var options = new ContactVerificationOptions();
                context.Configuration.GetSection(ContactVerificationOptions.SectionName).Bind(options);
                services.RemoveAll<ContactVerificationOptions>();
                services.AddSingleton(options);
            });

            if (logSink is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(new TestLoggerProvider(logSink)));
            }
        }
    }

    private sealed class TestLogSink
    {
        private readonly List<string> messages = [];
        private readonly object gate = new();

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (gate)
                {
                    return messages.ToArray();
                }
            }
        }

        public void Add(string message)
        {
            lock (gate)
            {
                messages.Add(message);
            }
        }
    }

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        private readonly TestLogSink sink;

        public TestLoggerProvider(TestLogSink sink)
        {
            this.sink = sink;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger(sink, categoryName);
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger : ILogger
    {
        private readonly TestLogSink sink;
        private readonly string categoryName;

        public TestLogger(TestLogSink sink, string categoryName)
        {
            this.sink = sink;
            this.categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            sink.Add($"{logLevel}: {categoryName}: {formatter(state, exception)} {exception?.GetType().Name}: {exception?.Message}");
        }
    }
}
