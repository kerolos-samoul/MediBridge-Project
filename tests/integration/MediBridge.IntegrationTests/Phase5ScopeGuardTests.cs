using System.Reflection;
using MediBridge.APIs.Controllers;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase5ScopeGuardTests
{
    private static readonly Type[] Phase5ControllerTypes =
    [
        typeof(CompanyDoctorsController),
        typeof(CompanyCampaignsController),
        typeof(CompanyWalletController)
    ];

    private static readonly Type[] Phase5ServiceTypes =
    [
        typeof(CompanyDoctorSearchService),
        typeof(CampaignWorkflowService),
        typeof(CompanyWalletService)
    ];

    [Fact]
    public void Phase5CompanyControllers_ExposeOnlyCampaignQueueAndWalletRoutes()
    {
        var routes = Phase5ControllerTypes
            .SelectMany(GetControllerRoutes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "api/company/campaigns",
                "api/company/campaigns/{campaignId}",
                "api/company/campaigns/{campaignId}/queue-summary",
                "api/company/campaigns/{campaignId}/review-outcome",
                "api/company/campaigns/{campaignId}/submit",
                "api/company/campaigns/{campaignId}/target-preview",
                "api/company/doctors",
                "api/company/wallet",
                "api/company/wallet/mock-checkout",
                "api/company/wallet/topup"
            ],
            routes);
    }

    [Fact]
    public void ProductionRoutes_DoNotExposePhase5OutOfScopeEndpoints()
    {
        using var factory = new ProductionWebAppFactory();
        var endpointSources = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();
        var routes = endpointSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(routes, route => route.Contains("daily-inject", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            [
                "api/admin/delivery-jobs/run-expiry",
                "api/admin/delivery-jobs/run-injector",
                "api/admin/delivery-jobs/status"
            ],
            routes
                .Where(route => route.Contains("delivery-jobs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(route => route, StringComparer.Ordinal)
                .ToArray());
        Assert.DoesNotContain(routes, route => route.Contains("jobs/retry", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Equals("jobs", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            [
                "api/doctor/messages/today",
                "api/doctor/messages/{deliveryId}/assets/{fileId}/access",
                "api/doctor/messages/{deliveryId}/interact",
                "api/doctor/messages/{deliveryId}/read"
            ],
            routes
                .Where(route => route.Contains("doctor/messages", StringComparison.OrdinalIgnoreCase))
                .OrderBy(route => route, StringComparer.Ordinal)
                .ToArray());
        Assert.DoesNotContain(routes, route => route.Contains("settlement", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Contains("analytics", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Contains("withdraw", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Contains("weekly-enforcement", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Contains("activity-score", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(routes, route => route.Contains("payment-gateway", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Phase5Services_DoNotDeclareFutureJobDeliverySettlementAnalyticsOrWithdrawalBehavior()
    {
        var forbiddenMarkers = new[]
        {
            "DailyInjector",
            "ExpiryJob",
            "DoctorInbox",
            "ReadMessage",
            "Interact",
            "Settlement",
            "Reporting",
            "Analytics",
            "Withdrawal",
            "WeeklyEnforcement",
            "ActivityScoreJob",
            "ProductionGateway",
            "PaymentGateway"
        };

        var memberNames = Phase5ServiceTypes
            .SelectMany(type => new[] { type.Name }.Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Select(method => method.Name)))
            .ToArray();

        foreach (var marker in forbiddenMarkers)
        {
            Assert.DoesNotContain(memberNames, name => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static IEnumerable<string> GetControllerRoutes(Type controllerType)
    {
        var controllerRoute = controllerType.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
        foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            foreach (var httpAttribute in method.GetCustomAttributes<HttpMethodAttribute>())
            {
                yield return CombineRoute(controllerRoute, httpAttribute.Template);
            }
        }
    }

    private static string CombineRoute(string controllerRoute, string? actionRoute)
    {
        return string.IsNullOrWhiteSpace(actionRoute)
            ? controllerRoute
            : $"{controllerRoute.TrimEnd('/')}/{actionRoute.TrimStart('/')}";
    }
}
