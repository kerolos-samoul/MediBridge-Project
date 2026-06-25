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

public sealed class AdminApprovalIntegrationTests
{
    [Fact]
    public async Task ApproveDecision_BeforeEmailVerificationReturnsConflictAndKeepsPending()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var target = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var decisionResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = "Approve",
            Notes = "Documents verified."
        });

        Assert.Equal(HttpStatusCode.Conflict, decisionResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Id == target.Id);
            Assert.Equal(AccountStatus.Pending, user.AccountStatus);
            Assert.Null(user.ApprovedAtUtc);
        }
    }

    [Fact]
    public async Task ApproveDecision_AfterEmailVerificationChangesStatusAndEnablesLogin()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var target = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        var otp = factory.GetLatestContactVerificationCode(email);
        using var verification = await client.PostAsJsonAsync("/api/auth/verify-contact", new
        {
            Contact = email,
            Channel = "Email",
            VerificationToken = otp
        });
        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));
        using var decisionResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = "Approve",
            Notes = "Documents verified."
        });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }
}
