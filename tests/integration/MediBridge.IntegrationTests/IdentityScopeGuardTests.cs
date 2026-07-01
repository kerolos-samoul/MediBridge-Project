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
    public void IdentityControllers_Should_NotExposeWorkflowRoutes()
    {
        var identityControllerTypes = new[]
        {
            typeof(AuthController),
            typeof(AdminAccountsController)
        };
        var routeTemplates = identityControllerTypes
            .SelectMany(GetRouteTemplates)
            .ToArray();

        foreach (var marker in OutOfScopeRouteMarkers)
        {
            Assert.DoesNotContain(routeTemplates, template => template.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void IdentityNamespaces_Should_NotDeclareWorkflowTypes()
    {
        var identityNamespaces = new[]
        {
            "MediBridge.Services.DTOs.Auth",
            "MediBridge.Services.DTOs.Admin",
            "MediBridge.Services.Validators.Auth",
            "MediBridge.Services.Validators.Admin"
        };
        var declaredTypeNames = typeof(MediBridge.Services.Services.AuthService).Assembly
            .GetTypes()
            .Where(type => identityNamespaces.Contains(type.Namespace, StringComparer.Ordinal))
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
