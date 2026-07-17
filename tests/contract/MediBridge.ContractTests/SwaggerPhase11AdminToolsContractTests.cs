using System.Reflection;
using MediBridge.APIs.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class SwaggerPhase11AdminToolsContractTests
{
    public static IEnumerable<object[]> Phase11Routes()
    {
        yield return [typeof(AdminWorkQueueController), "GET", "api/admin/work-queue"];
        yield return [typeof(DoctorWithdrawalsController), "POST", "api/doctor/withdrawals"];
        yield return [typeof(DoctorWithdrawalsController), "GET", "api/doctor/withdrawals"];
        yield return [typeof(AdminWithdrawalsController), "GET", "api/admin/withdrawals"];
        yield return [typeof(AdminWithdrawalsController), "PUT", "api/admin/withdrawals/{withdrawalId}/approve"];
        yield return [typeof(AdminWithdrawalsController), "PUT", "api/admin/withdrawals/{withdrawalId}/reject"];
        yield return [typeof(AdminWithdrawalsController), "PUT", "api/admin/withdrawals/{withdrawalId}/mark-paid"];
        yield return [typeof(AdminWithdrawalsController), "PUT", "api/admin/withdrawals/{withdrawalId}/mark-failed"];
        yield return [typeof(AdminPricingController), "PUT", "api/admin/doctors/{doctorId}/price/deactivate"];
        yield return [typeof(AdminStatisticsController), "GET", "api/admin/statistics"];
    }

    [Theory]
    [MemberData(nameof(Phase11Routes))]
    public void Phase11RoutesExposeHttpAttributesAndSwaggerResponseMetadata(Type controllerType, string method, string route)
    {
        var controllerRoute = controllerType.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
        var action = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate => HttpRoutes(candidate, controllerRoute).Any(item => item.Method == method && item.Route == route));

        Assert.NotNull(action);
        var responseCodes = action!.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(attribute => attribute.StatusCode).ToHashSet();
        Assert.True(responseCodes.Contains(200) || responseCodes.Contains(201));
        Assert.Contains(401, responseCodes);
        Assert.Contains(403, responseCodes);
    }

    private static IEnumerable<(string Method, string Route)> HttpRoutes(MethodInfo method, string controllerRoute)
    {
        foreach (var attribute in method.GetCustomAttributes<HttpMethodAttribute>())
        {
            var actionRoute = attribute.Template ?? string.Empty;
            var route = string.IsNullOrWhiteSpace(actionRoute) ? controllerRoute : $"{controllerRoute}/{actionRoute}";
            yield return (attribute.HttpMethods.Single().ToUpperInvariant(), route);
        }
    }
}
