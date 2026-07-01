using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignScopeGuardTests
{
    private static readonly string[] ProductionProjects =
    [
        "MediBridge.Core",
        "MediBridge.Repository",
        "MediBridge.Services",
        "MediBridge.APIs"
    ];

    private static readonly string[] ForbiddenPaymentProviderMarkers =
    [
        "Paymob",
        "Stripe",
        "Webhook",
        "CallbackUrl",
        "RedirectUrl",
        "ApiKey",
        "ProviderSecret",
        "PaymentGateway"
    ];

    [Fact]
    public void WalletCampaignProductionSource_Should_Not_Contain_PaymentProvider_Integration_Markers()
    {
        var root = GetRepositoryRoot();
        var violations = ProductionProjects
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*", SearchOption.AllDirectories))
            .Where(IsScannableSourceFile)
            .Where(path => !IsExcludedPath(path))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .SelectMany(candidate => ForbiddenPaymentProviderMarkers
                .Where(marker => candidate.Source.Contains(marker, StringComparison.OrdinalIgnoreCase))
                .Select(marker => $"{Path.GetRelativePath(root, candidate.Path)} -> {marker}"))
            .ToArray();

        Assert.Empty(violations);
    }

    private static bool IsScannableSourceFile(string path)
        => Path.GetExtension(path) is ".cs" or ".json";

    private static bool IsExcludedPath(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
           path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
           path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string GetRepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !File.Exists(Path.Combine(root, "MediBridge.slnx")); i++)
        {
            root = Directory.GetParent(root)?.FullName ?? root;
        }

        return root;
    }
}
