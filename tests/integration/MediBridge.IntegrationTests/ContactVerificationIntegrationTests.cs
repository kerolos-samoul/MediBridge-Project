using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class ContactVerificationIntegrationTests
{
    [Fact]
    public async Task VerifyContact_ConsumesOnceSetsEmailVerifiedAndRejectsReplay()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var token = await CreateVerificationFlowAsync(factory, email, ContactVerificationChannel.Email, DateTime.UtcNow.AddHours(1));

        using var response = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", VerificationToken = token });
        using var replayResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", VerificationToken = token });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var flow = await db.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == user.Id);

        Assert.True(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        Assert.NotNull(flow.ConsumedAtUtc);
    }

    [Fact]
    public async Task VerifyContact_WithPhoneChannelSetsPhoneVerified()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var token = await CreateVerificationFlowAsync(factory, email, ContactVerificationChannel.Phone, DateTime.UtcNow.AddHours(1));

        using var response = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Phone", VerificationToken = token });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);

        Assert.True(user.PhoneVerified);
    }

    [Fact]
    public async Task VerifyContact_ConcurrentRequestsConsumeTokenOnce()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var token = await CreateVerificationFlowAsync(factory, email, ContactVerificationChannel.Email, DateTime.UtcNow.AddHours(1));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 6)
            .Select(async _ =>
            {
                await ready.Task;
                using var response = await client.PostAsJsonAsync("/api/auth/verify-contact", new
                {
                    Channel = "Email",
                    VerificationToken = token
                });
                return response.StatusCode;
            })
            .ToArray();

        ready.SetResult();
        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.BadRequest));
    }

    internal static async Task<string> CreateVerificationFlowAsync(
        WebAppFactory factory,
        string email,
        ContactVerificationChannel channel,
        DateTime expiresAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var destination = channel == ContactVerificationChannel.Email ? user.Email! : user.PhoneNumber!;
        var (plaintext, hash) = tokenService.CreateOneTimeToken();

        db.ContactVerificationFlows.Add(new ContactVerificationFlow
        {
            UserId = user.Id,
            Channel = channel,
            DestinationHash = tokenService.HashToken(destination),
            TokenHash = hash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return plaintext;
    }
}
