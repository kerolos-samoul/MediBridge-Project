using System.Net;
using System.Net.Http.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class ContactVerificationResendTests
{
    [Fact]
    public async Task ResendContactVerification_CreatesFreshFlowAndSupersedesPreviousFlow()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var firstOtp = factory.GetLatestContactVerificationCode(email);
        await AllowImmediateResendAsync(factory, email);

        using var response = await client.PostAsJsonAsync("/api/auth/resend-contact-verification", new { Contact = email, Channel = "Email" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var secondOtp = factory.GetLatestContactVerificationCode(email);
        Assert.NotEqual(firstOtp, secondOtp);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var flows = await db.ContactVerificationFlows
            .Where(flow => flow.UserId == user.Id)
            .OrderBy(flow => flow.CreatedAtUtc)
            .ToListAsync();
        Assert.Equal(2, flows.Count);
        Assert.NotNull(flows[0].SupersededAtUtc);
        Assert.Null(flows[1].SupersededAtUtc);
    }

    [Fact]
    public async Task ResendContactVerification_UnknownContactReturnsAcceptedWithoutDelivery()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/auth/resend-contact-verification", new
        {
            Contact = $"missing-{Guid.NewGuid():N}@example.com",
            Channel = "Email"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, factory.DeliveredEmailCount);
    }

    [Fact]
    public async Task ResendContactVerification_WithinCooldownReturnsTooManyRequests()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);

        using var response = await client.PostAsJsonAsync("/api/auth/resend-contact-verification", new { Contact = email, Channel = "Email" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    private static async Task AllowImmediateResendAsync(WebAppFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var flow = await db.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == user.Id);
        flow.LastSentAtUtc = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
    }
}
