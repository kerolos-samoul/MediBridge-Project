using System.Reflection;
using MediBridge.APIs.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class IdentityScopeGuardTests
{
    private static readonly string[] OutOfScopeRouteMarkers =
    [
        "campaign",
        "queue",
        "delivery",
        "wallet",
        "settlement",
        "payout",
        "upload",
        "storage",
        "file",
        "review",
        "pricing",
        "platform-fee",
        "platformfee"
    ];

    private static readonly string[] OutOfScopeImplementationMarkers =
    [
        "Campaign",
        "Delivery",
        "Wallet",
        "Settlement",
        "Payout",
        "Pricing",
        "PlatformFee",
        "Ledger",
        "Escrow",
        "FileUpload",
        "FileStorage",
        "FileReview"
    ];

    [Fact]
    public void Phase2Controllers_Should_NotExposeLaterPhaseRoutes()
    {
        var routeTemplates = typeof(AuthController).Assembly
            .GetTypes()
            .Where(type => type.IsClass && type.Name.EndsWith("Controller", StringComparison.Ordinal))
            .SelectMany(GetRouteTemplates)
            .ToArray();

        foreach (var marker in OutOfScopeRouteMarkers)
        {
            Assert.DoesNotContain(routeTemplates, template => template.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Phase2ApplicationAssemblies_Should_NotDeclareLaterPhaseWorkflowTypes()
    {
        var productionAssemblies = new[]
        {
            typeof(MediBridge.Services.Services.AuthService).Assembly,
            typeof(AuthController).Assembly
        };

        var declaredTypeNames = productionAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        foreach (var marker in OutOfScopeImplementationMarkers)
        {
            Assert.DoesNotContain(declaredTypeNames, typeName => typeName.Contains(marker, StringComparison.Ordinal));
        }
    }

    private static IEnumerable<string> GetRouteTemplates(Type controllerType)
    {
        var controllerRoutes = controllerType.GetCustomAttributes<RouteAttribute>()
            .Select(attribute => attribute.Template ?? string.Empty)
            .DefaultIfEmpty(string.Empty);

        var actionRoutes = controllerType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(attribute => attribute.Template is null ? [""] : attribute.HttpMethods.Select(_ => attribute.Template));

        foreach (var controllerRoute in controllerRoutes)
        {
            foreach (var actionRoute in actionRoutes.DefaultIfEmpty(string.Empty))
            {
                yield return $"{controllerRoute}/{actionRoute}".Trim('/');
            }
        }
    }
}
