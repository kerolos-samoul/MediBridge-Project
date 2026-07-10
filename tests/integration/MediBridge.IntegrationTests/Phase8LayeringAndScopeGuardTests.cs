using System.Xml.Linq;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8LayeringAndScopeGuardTests
{
    [Fact]
    public void CoreAndServices_DoNotReferenceEfCoreOrHttpInfrastructure()
    {
        var root = FindRepositoryRoot();

        AssertProjectDoesNotReference(root, "MediBridge.Core", "Microsoft.EntityFrameworkCore");
        AssertProjectDoesNotReference(root, "MediBridge.Core", "Microsoft.AspNetCore");
        AssertProjectDoesNotReference(root, "MediBridge.Services", "Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public void DoctorMessagesController_RemainsHttpAdapterWithoutBusinessWalletOrDateLogic()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "MediBridge.APIs", "Controllers", "DoctorMessagesController.cs"));

        Assert.Contains("MarkDeliveryReadAsync", controller, StringComparison.Ordinal);
        Assert.Contains("InteractWithDeliveryAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ReservedBalance", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("AvailableBalance", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliveryDateEgypt", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("DateOnly", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("WalletTransaction", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase8_DoesNotAddForbiddenLaterScopeRoutesServicesOrEntities()
    {
        var root = FindRepositoryRoot();
        var forbiddenTerms = new[]
        {
            "CompanyFeedback",
            "FeedbackReport",
            "WeeklyEnforcement",
            "ActivityScoreRecalculation",
            "Notification",
            "Payout",
            "WithdrawalApproval",
            "AdminPayout",
            "ActivateQueued",
            "ReleaseExpired"
        };

        var phase8Files = new[]
        {
            Path.Combine(root, "MediBridge.APIs", "Controllers", "DoctorMessagesController.cs"),
            Path.Combine(root, "MediBridge.APIs", "Middleware", "GlobalExceptionMiddleware.cs"),
            Path.Combine(root, "MediBridge.Core", "Entities", "Messaging", "DeliveryInteractionOperation.cs"),
            Path.Combine(root, "MediBridge.Core", "Entities", "Messaging", "DoctorAdDelivery.cs"),
            Path.Combine(root, "MediBridge.Services", "Services", "DoctorMessageService.cs")
        }
            .Where(File.Exists)
            .ToArray();

        foreach (var file in phase8Files)
        {
            var content = File.ReadAllText(file);
            foreach (var term in forbiddenTerms)
            {
                Assert.DoesNotContain(term, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static void AssertProjectDoesNotReference(string root, string projectName, string forbiddenReference)
    {
        var projectPath = Path.Combine(root, projectName, $"{projectName}.csproj");
        var document = XDocument.Load(projectPath);
        var references = document.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference" or "FrameworkReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty);

        Assert.DoesNotContain(references, reference => reference.Contains(forbiddenReference, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediBridge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
