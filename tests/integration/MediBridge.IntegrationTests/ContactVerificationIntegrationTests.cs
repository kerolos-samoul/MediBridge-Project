using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
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
        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();
        var tokenHash = tokenService.HashToken(token);
        var flow = await db.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == user.Id && candidate.TokenHash == tokenHash);

        Assert.True(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        Assert.NotNull(flow.ConsumedAtUtc);
    }

    [Fact]
    public async Task VerifyContact_WithValidEmailOtp_ConsumesFlowSetsEmailVerifiedAndKeepsApprovalSeparate()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var otp = ReadLatestOtp(factory);

        using var response = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = otp });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var flow = await db.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == user.Id);

        Assert.True(user.EmailVerified);
        Assert.Equal(AccountStatus.Pending, user.AccountStatus);
        Assert.Null(user.ApprovedAtUtc);
        Assert.NotNull(flow.ConsumedAtUtc);
    }

    [Fact]
    public async Task VerifyContact_WithReusedEmailOtp_ReturnsBadRequest()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var otp = ReadLatestOtp(factory);

        using var firstResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = otp });
        using var replayResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = otp });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replayResponse.StatusCode);
    }

    [Fact]
    public async Task VerifyContact_WithExpiredEmailOtp_ReturnsBadRequest()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var otp = ReadLatestOtp(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
            var flow = await db.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == user.Id);
            flow.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = otp });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyContact_WithInvalidEmailOtp_ReturnsBadRequestAndLeavesEmailUnverified()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);

        using var response = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task VerifyContact_AfterMaxInvalidOtpAttemptsRejectsValidOtp()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var otp = ReadLatestOtp(factory);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var invalidResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = "000000" });
            Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        }

        using (var attemptScope = factory.Services.CreateScope())
        {
            var attemptDb = attemptScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var attemptUser = await attemptDb.Users.SingleAsync(candidate => candidate.Email == email);
            var attemptFlow = await attemptDb.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == attemptUser.Id);
            Assert.Equal(5, attemptFlow.FailedAttemptCount);
            Assert.NotNull(attemptFlow.MaxAttemptsReachedAtUtc);
        }

        using var validAfterLockResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = otp });

        Assert.Equal(HttpStatusCode.BadRequest, validAfterLockResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        Assert.False(user.EmailVerified);
    }

    [Fact]
    public async Task RequestContactVerification_ResendsOtpAndInvalidatesOldOtp()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var oldOtp = ReadLatestOtp(factory);
        await MoveLatestFlowOutsideCooldownAsync(factory, email);

        using var response = await client.PostAsJsonAsync("/api/auth/request-contact-verification", new { Email = email, Channel = "Email" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var newOtp = ReadLatestOtp(factory);
        Assert.NotEqual(oldOtp, newOtp);

        using var oldOtpResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = oldOtp });
        using var newOtpResponse = await client.PostAsJsonAsync("/api/auth/verify-contact", new { Channel = "Email", Email = email, Otp = newOtp });

        Assert.Equal(HttpStatusCode.BadRequest, oldOtpResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newOtpResponse.StatusCode);
    }

    [Fact]
    public async Task RequestContactVerification_ImmediateResendReturnsTooManyRequests()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);

        using var response = await client.PostAsJsonAsync("/api/auth/request-contact-verification", new { Email = email, Channel = "Email" });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
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

    private static string ReadLatestOtp(WebAppFactory factory)
    {
        var message = factory.Services.GetRequiredService<TestEmailSink>().Messages.Last();
        return Regex.Match(message.Body, "\\b\\d{6}\\b").Value;
    }

    private static async Task MoveLatestFlowOutsideCooldownAsync(WebAppFactory factory, string email)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var flow = await db.ContactVerificationFlows.SingleAsync(candidate => candidate.UserId == user.Id);
        flow.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2);
        flow.LastSentAtUtc = DateTime.UtcNow.AddMinutes(-2);
        await db.SaveChangesAsync();
    }
}
