using System.Net;
using System.Reflection;
using MediBridge.APIs.Config;
using MediBridge.APIs.Controllers;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class RateLimitPolicyRegistrationTests
{
    [Theory]
    [InlineData("/__test/rate-limit/login")]
    [InlineData("/__test/rate-limit/registration")]
    [InlineData("/__test/rate-limit/refresh")]
    [InlineData("/__test/rate-limit/company-top-up")]
    [InlineData("/__test/rate-limit/doctor-withdrawal")]
    [InlineData("/__test/rate-limit/doctor-interaction")]
    public async Task RequiredRateLimitPolicies_CanBeAttachedToEndpoints(string path)
    {
        await using var factory = new WebAppFactory();
        using var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void DoctorPhase8InteractionEndpoints_UseDoctorInteractionRateLimitPolicy()
    {
        var read = typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.MarkRead))!;
        var interact = typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.Interact))!;
        var today = typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.GetToday))!;
        var asset = typeof(DoctorMessagesController).GetMethod(nameof(DoctorMessagesController.GetAssetAccess))!;

        Assert.Null(typeof(DoctorMessagesController).GetCustomAttribute<EnableRateLimitingAttribute>());
        Assert.Equal(RateLimitPolicyNames.DoctorInteraction, read.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal(RateLimitPolicyNames.DoctorInteraction, interact.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal(RateLimitPolicyNames.Phase7DoctorMessagesRead, today.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal(RateLimitPolicyNames.Phase7DoctorMessagesRead, asset.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }
}
