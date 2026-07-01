using System.Text.Json;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class ConfigurationSecretGuardTests
{
    [Fact]
    public void TrackedAppSettings_DoNotContainDeployableCredentials()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configurationFiles = new[]
        {
            Path.Combine(repositoryRoot, "MediBridge.APIs", "appsettings.json"),
            Path.Combine(repositoryRoot, "MediBridge.APIs", "appsettings.Development.json")
        };

        foreach (var configurationFile in configurationFiles)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configurationFile));
            var root = document.RootElement;
            var connectionString = root.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString() ?? string.Empty;
            var jwtSigningKey = root.GetProperty("Jwt").GetProperty("SigningKey").GetString() ?? string.Empty;
            var smtpPassword = root.GetProperty("Email").GetProperty("Smtp").GetProperty("Password").GetString() ?? string.Empty;

            Assert.False(connectionString.Contains("Password=", StringComparison.OrdinalIgnoreCase), $"Credential-bearing database connection string must not be committed in {Path.GetFileName(configurationFile)}.");
            Assert.False(connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase), $"Credential-bearing database connection string must not be committed in {Path.GetFileName(configurationFile)}.");
            Assert.True(string.IsNullOrEmpty(jwtSigningKey), $"JWT signing keys must not be committed in {Path.GetFileName(configurationFile)}.");
            Assert.True(string.IsNullOrEmpty(smtpPassword), $"SMTP password must not be committed in {Path.GetFileName(configurationFile)}.");

            if (root.TryGetProperty("CloudinaryStorage", out var cloudinaryStorage)
                && cloudinaryStorage.TryGetProperty("CloudinaryUrl", out var cloudinaryUrl))
            {
                Assert.True(
                    string.IsNullOrEmpty(cloudinaryUrl.GetString()),
                    $"Cloud storage credential URL must not be committed in {Path.GetFileName(configurationFile)}.");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediBridge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
