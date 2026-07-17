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

public sealed class WithdrawalAuthorizationIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public WithdrawalAuthorizationIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData(AccountStatus.Pending, DoctorMarketplaceStatus.Active)]
    [InlineData(AccountStatus.Rejected, DoctorMarketplaceStatus.Active)]
    [InlineData(AccountStatus.Approved, DoctorMarketplaceStatus.Warned)]
    [InlineData(AccountStatus.Approved, DoctorMarketplaceStatus.Suspended)]
    public async Task IneligibleDoctorStatesCannotCreateWithdrawal(AccountStatus accountStatus, DoctorMarketplaceStatus marketplaceStatus)
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        await SetDoctorStateAsync(doctor.UserId, doctor.DoctorId, accountStatus, marketplaceStatus);
        await WithdrawalIntegrationTestHelpers.SeedDoctorWalletAsync(factory.Services, doctor.DoctorId, doctor.UserId, 100m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));

        using var response = await client.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 50m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNoWithdrawalSideEffectsAsync(doctor.DoctorId);
    }

    [Fact]
    public async Task NonOwnerNonDoctorAndUnauthenticatedCallersCannotCreateWithdrawal()
    {
        await factory.InitializeDatabaseAsync();
        var owner = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var otherDoctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        await WithdrawalIntegrationTestHelpers.SeedDoctorWalletAsync(factory.Services, owner.DoctorId, owner.UserId, 100m);

        using var noProfileClient = factory.CreateClient();
        noProfileClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", $"missing-{Guid.NewGuid():N}"));
        using var noProfileResponse = await noProfileClient.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 50m });

        using var otherDoctorClient = factory.CreateClient();
        otherDoctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", otherDoctor.UserId));
        using var otherDoctorResponse = await otherDoctorClient.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 50m });

        using var companyClient = factory.CreateClient();
        companyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", company.UserId));
        using var companyResponse = await companyClient.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 50m });

        using var anonymousClient = factory.CreateClient();
        using var anonymousResponse = await anonymousClient.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 50m });

        Assert.Equal(HttpStatusCode.Forbidden, noProfileResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otherDoctorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, companyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        await AssertNoWithdrawalSideEffectsAsync(owner.DoctorId);
    }

    private async Task SetDoctorStateAsync(string userId, string doctorId, AccountStatus accountStatus, DoctorMarketplaceStatus marketplaceStatus)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await context.Users.SingleAsync(item => item.Id == userId);
        var profile = await context.DoctorProfiles.SingleAsync(item => item.Id == doctorId);
        user.AccountStatus = accountStatus;
        profile.Status = marketplaceStatus;
        await context.SaveChangesAsync();
    }

    private async Task AssertNoWithdrawalSideEffectsAsync(string doctorId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.False(await context.WithdrawalRequests.AnyAsync(item => item.DoctorId == doctorId));
        Assert.False(await context.WalletTransactions.AnyAsync(item => item.WithdrawalRequestId != null));
        Assert.False(await context.WalletLedgerEntries.AnyAsync(item => item.WithdrawalRequestId != null));
    }
}
