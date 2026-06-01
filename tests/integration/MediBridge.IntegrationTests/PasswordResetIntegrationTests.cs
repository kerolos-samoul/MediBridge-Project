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

public sealed class PasswordResetIntegrationTests
{
    [Fact]
    public async Task ResetPassword_WithValidTokenConsumesFlowAndRevokesRefreshCredentials()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        _ = await Phase6IdentityTestHelpers.LoginAndGetRefreshTokenAsync(client, email);
        var resetToken = await CreateResetFlowAsync(factory, email, DateTime.UtcNow.AddHours(1));

        using var response = await client.PostAsJsonAsync("/api/auth/reset-password", new { ResetToken = resetToken, NewPassword = "NewPassword1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var flow = await db.PasswordResetFlows.SingleAsync(candidate => candidate.UserId == user.Id);
        var refreshCredential = await db.RefreshCredentials.SingleAsync(candidate => candidate.UserId == user.Id);

        Assert.NotNull(flow.ConsumedAtUtc);
        Assert.NotNull(refreshCredential.RevokedAtUtc);
        Assert.Equal("PasswordChanged", refreshCredential.RevocationReason);
    }

    internal static async Task<string> CreateResetFlowAsync(WebAppFactory factory, string email, DateTime expiresAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<IAuthTokenService>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        var (plaintext, hash) = tokenService.CreateOneTimeToken();

        db.PasswordResetFlows.Add(new PasswordResetFlow
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            RequestCorrelationId = Guid.NewGuid().ToString("N")
        });

        await db.SaveChangesAsync();
        return plaintext;
    }
}
