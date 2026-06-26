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
        "submission",
        "queue",
        "delivery",
        "wallet",
        "settlement",
        "payout",
        "message",
        "report",
        "gallery",
        "malware",
        "pricing",
        "platform-fee",
        "platformfee"
    ];

    private static readonly string[] OutOfScopeImplementationMarkers =
    [
        "CampaignSubmission",
        "CampaignQueue",
        "Delivery",
        "DoctorMessage",
        "MessageViewing",
        "ReportingReadModel",
        "PublicGallery",
        "MalwareScan",
        "Wallet",
        "Settlement",
        "Payout",
        "Pricing",
        "PlatformFee",
        "Ledger",
        "Escrow"
    ];

    [Fact]
    public void Phase2Controllers_Should_NotExposeLaterPhaseRoutes()
    {
        var phase2IdentityControllers = new[]
        {
            typeof(AuthController),
            typeof(AdminAccountsController)
        };

        var routeTemplates = phase2IdentityControllers
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
        var phase2IdentityTypes = new[]
        {
            typeof(MediBridge.Services.Services.AuthService),
            typeof(MediBridge.Services.Services.AuthTokenService),
            typeof(MediBridge.Services.Services.AdminAccountService),
            typeof(AuthController),
            typeof(AdminAccountsController)
        };

        var declaredTypeNames = phase2IdentityTypes
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
