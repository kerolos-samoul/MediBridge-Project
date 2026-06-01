using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class PasswordResetReplayTests
{
    [Fact]
    public async Task ResetPassword_RejectsReplayOfConsumedToken()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var token = await PasswordResetIntegrationTests.CreateResetFlowAsync(factory, email, DateTime.UtcNow.AddHours(1));

        using var firstResponse = await client.PostAsJsonAsync("/api/auth/reset-password", new { ResetToken = token, NewPassword = "NewPassword1!" });
        using var replayResponse = await client.PostAsJsonAsync("/api/auth/reset-password", new { ResetToken = token, NewPassword = "AnotherPassword1!" });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_RejectsExpiredToken()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var token = await PasswordResetIntegrationTests.CreateResetFlowAsync(factory, email, DateTime.UtcNow.AddMinutes(-1));

        using var response = await client.PostAsJsonAsync("/api/auth/reset-password", new { ResetToken = token, NewPassword = "NewPassword1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ConcurrentRequestsConsumeTokenOnce()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        var token = await PasswordResetIntegrationTests.CreateResetFlowAsync(factory, email, DateTime.UtcNow.AddHours(1));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 6)
            .Select(async index =>
            {
                await ready.Task;
                using var response = await client.PostAsJsonAsync("/api/auth/reset-password", new
                {
                    ResetToken = token,
                    NewPassword = $"NewPassword{index}!"
                });
                return response.StatusCode;
            })
            .ToArray();

        ready.SetResult();
        var statuses = await Task.WhenAll(tasks);

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.Unauthorized));
    }
}
