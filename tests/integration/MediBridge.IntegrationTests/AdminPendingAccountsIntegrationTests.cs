using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminPendingAccountsIntegrationTests
{
    [Fact]
    public async Task PendingAccounts_ReturnsRequestedPageAndTotalPendingCount()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        for (var index = 0; index < 3; index++)
        {
            await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.GetAsync("/api/admin/pending-accounts?PageNumber=2&PageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(2, data.GetProperty("PageNumber").GetInt32());
        Assert.Equal(2, data.GetProperty("PageSize").GetInt32());
        Assert.Equal(3, data.GetProperty("TotalCount").GetInt32());
        Assert.Single(data.GetProperty("Items").EnumerateArray());
    }

    [Fact]
    public async Task PendingAccounts_ExcludesSoftDeletedPendingAccounts()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var admin = await Phase6IdentityTestHelpers.CreateAdminAsync(factory.Services);
        await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        var deletedEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var deletedUser = await db.Users.SingleAsync(candidate => candidate.Email == deletedEmail);
            deletedUser.IsDeleted = true;
            deletedUser.DeletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", admin.Id));

        using var response = await client.GetAsync("/api/admin/pending-accounts?PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        var items = data.GetProperty("Items").EnumerateArray().ToList();

        Assert.Equal(1, data.GetProperty("TotalCount").GetInt32());
        Assert.Single(items);
    }
}
