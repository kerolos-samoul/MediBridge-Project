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

public sealed class AdminRejectionIntegrationTests
{
    [Fact]
    public async Task RejectDecision_RequiresReasonChangesStatusPreservesLoginDenialAndStoresReason()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var target = await Phase6IdentityTestHelpers.FindUserByEmailAsync(factory.Services, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var invalidResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new { Decision = "Reject" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var decisionResponse = await client.PutAsJsonAsync($"/api/admin/accounts/{target.Id}/decision", new
        {
            Decision = "Reject",
            Reason = "License document was unreadable."
        });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var user = await db.Users.SingleAsync(candidate => candidate.Id == target.Id);
            var decision = await db.AdminAccountDecisions.SingleAsync(candidate => candidate.TargetUserId == target.Id);
            Assert.Equal(AccountStatus.Rejected, user.AccountStatus);
            Assert.Equal("License document was unreadable.", decision.Reason);
        }

        client.DefaultRequestHeaders.Authorization = null;
        using var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });
        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
    }
}
