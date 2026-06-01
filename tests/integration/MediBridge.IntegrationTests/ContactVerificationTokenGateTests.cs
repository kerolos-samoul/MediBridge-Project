using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class ContactVerificationTokenGateTests
{
    [Fact]
    public async Task Login_AllowsApprovedAccountWithoutContactVerification()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await db.Users.SingleAsync(candidate => candidate.Email == email);
        Assert.False(user.EmailVerified);
        Assert.False(user.PhoneVerified);

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = email, Password = "Password1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
