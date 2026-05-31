using System.Collections.Concurrent;
using System.Net.Http;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MediBridge.IntegrationTests;

public class RequestLoggingPolicyTests
{
    [Fact]
    public async Task RequestLogging_EmitsOnlyMetadataAndExcludesSensitiveValues()
    {
        using var provider = new CapturingLoggerProvider();
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(provider);
            });
        }).CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/weatherforecast");
        request.Headers.Add("Authorization", "Bearer dummy-token");
        request.Headers.Add("X-Correlation-ID", "phase5-log-test");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        Assert.Contains(provider.Entries, entry =>
            entry.Message.StartsWith("Request received", StringComparison.Ordinal) &&
            entry.Properties.ContainsKey("Method") &&
            entry.Properties.ContainsKey("Path") &&
            entry.Properties.ContainsKey("CorrelationId"));
        Assert.Contains(provider.Entries, entry =>
            entry.Message.StartsWith("Request completed", StringComparison.Ordinal) &&
            entry.Properties.ContainsKey("Method") &&
            entry.Properties.ContainsKey("Path") &&
            entry.Properties.ContainsKey("StatusCode") &&
            entry.Properties.ContainsKey("DurationMs") &&
            entry.Properties.ContainsKey("CorrelationId"));
        Assert.DoesNotContain(provider.Entries, entry => entry.Message.Contains("dummy-token", StringComparison.Ordinal));
        Assert.DoesNotContain(provider.Entries, entry => entry.Message.Contains("Authorization", StringComparison.Ordinal));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentBag<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentBag<LogEntry> _entries;

        public CapturingLogger(ConcurrentBag<LogEntry> entries)
        {
            _entries = entries;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var value in values)
                {
                    properties[value.Key] = value.Value;
                }
            }

            _entries.Add(new LogEntry(formatter(state, exception), properties));
        }
    }

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> Properties);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
