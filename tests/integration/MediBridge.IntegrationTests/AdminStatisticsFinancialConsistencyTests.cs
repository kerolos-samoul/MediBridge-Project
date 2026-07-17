using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class AdminStatisticsFinancialConsistencyTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public AdminStatisticsFinancialConsistencyTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task InconsistentFinancialEvidenceWithholdsWalletTotalsButReturnsNonFinancialTotals()
    {
        await factory.InitializeDatabaseAsync();
        var now = DateTime.UtcNow;
        var seed = await AdminStatisticsIntegrationTests.SeedStatisticsSourcesAsync(factory.Services, now);
        await AddBrokenWithdrawalTransactionAsync(seed.DoctorId, seed.DoctorUserId, now);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.GetAsync($"/api/admin/statistics?fromDateEgypt={seed.BusinessDate:yyyy-MM-dd}&toDateEgypt={seed.BusinessDate:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("walletMovementSummary").ValueKind);
        Assert.Contains(data.GetProperty("withheldFinancialScopes").EnumerateArray(), item => item.GetString() == "WalletMovementSummary");
        Assert.Equal(1, data.GetProperty("campaignCounts").GetProperty("PendingReview").GetInt32());
        Assert.Equal(2, data.GetProperty("withdrawalStatusCounts").GetProperty("Requested").GetInt32());
    }

    private async Task AddBrokenWithdrawalTransactionAsync(string doctorId, string doctorUserId, DateTime nowUtc)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = context.Wallets.Single(item => item.OwnerType == WalletOwnerType.Doctor && item.OwnerId == doctorId);
        var withdrawal = new WithdrawalRequest
        {
            Id = $"withdrawal-broken-{Guid.NewGuid():N}",
            DoctorId = doctorId,
            Amount = 25m,
            Status = WithdrawalRequestStatus.Requested,
            RequestedAtUtc = nowUtc
        };
        context.WithdrawalRequests.Add(withdrawal);
        context.WalletTransactions.Add(new WalletTransaction
        {
            Id = $"tx-broken-{Guid.NewGuid():N}",
            WalletId = wallet.Id,
            OperationType = WalletTransactionType.WithdrawalHold,
            IdempotencyKey = $"withdrawal:{withdrawal.Id}:hold",
            Amount = 25m,
            WithdrawalRequestId = withdrawal.Id,
            CreatedAtUtc = nowUtc
        });
        await context.SaveChangesAsync();
    }
}
