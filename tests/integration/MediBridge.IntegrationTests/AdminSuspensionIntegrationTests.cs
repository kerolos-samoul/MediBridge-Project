using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminSuspensionIntegrationTests
{
    [Fact]
    public async Task SuspendDecision_ChangesStatusAndRevokesActiveRefreshCredentials()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        _ = await Phase6IdentityTestHelpers.LoginAndGetRefreshTokenAsync(client, email);
        var target = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = "Suspend",
            Reason = "Security review."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == target.Id);
        var credential = await db.RefreshCredentials.SingleAsync(candidate => candidate.UserId == target.Id);

        Assert.Equal(AccountStatus.Suspended, user.AccountStatus);
        Assert.NotNull(credential.RevokedAtUtc);
        Assert.Equal("Suspended", credential.RevocationReason);
    }

    [Fact]
    public async Task InactivateDecision_ChangesStatusAndRevokesActiveRefreshCredentials()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);
        _ = await Phase6IdentityTestHelpers.LoginAndGetRefreshTokenAsync(client, email);
        var target = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = "Inactivate",
            Notes = "Account owner requested deactivation."
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Id == target.Id);
        var credential = await db.RefreshCredentials.SingleAsync(candidate => candidate.UserId == target.Id);

        Assert.Equal(AccountStatus.Inactive, user.AccountStatus);
        Assert.NotNull(credential.RevokedAtUtc);
        Assert.Equal("Inactive", credential.RevocationReason);
    }
}
