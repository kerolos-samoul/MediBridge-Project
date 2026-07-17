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

public sealed class WithdrawalConcurrencyIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public WithdrawalConcurrencyIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task RetriedDoctorSubmissionHasAtMostOneWalletHoldWhenBalanceIsFullyReserved()
    {
        await factory.InitializeDatabaseAsync();
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var walletId = await WithdrawalIntegrationTestHelpers.SeedDoctorWalletAsync(factory.Services, doctor.DoctorId, doctor.UserId, 100m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));

        using var first = await client.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 100m });
        using var retry = await client.PostAsJsonAsync("/api/doctor/withdrawals", new { Amount = 100m });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.SingleAsync(item => item.Id == walletId);
        Assert.Equal(0m, wallet.AvailableBalance);
        Assert.Equal(100m, wallet.ReservedBalance);
        Assert.Equal(1, await context.WithdrawalRequests.CountAsync(item => item.DoctorId == doctor.DoctorId));
        var withdrawalId = await context.WithdrawalRequests.Where(item => item.DoctorId == doctor.DoctorId).Select(item => item.Id).SingleAsync();
        Assert.Equal(1, await context.WalletTransactions.CountAsync(item => item.WithdrawalRequestId == withdrawalId && item.OperationType == WalletTransactionType.WithdrawalHold));
        Assert.Equal(2, await context.WalletLedgerEntries.CountAsync(item => item.WithdrawalRequestId == withdrawalId));
    }

    [Fact]
    public async Task RetriedAdminPayoutDecisionHasAtMostOneWalletEffect()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await WithdrawalIntegrationTestHelpers.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Approved);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", seed.AdminUserId));

        using var first = await client.PutAsJsonAsync($"/api/admin/withdrawals/{seed.WithdrawalId}/mark-paid", new { PayoutReference = "ops-ref-001" });
        using var retry = await client.PutAsJsonAsync($"/api/admin/withdrawals/{seed.WithdrawalId}/mark-paid", new { PayoutReference = "ops-ref-001" });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        var balances = await WithdrawalIntegrationTestHelpers.GetWalletBalancesAsync(factory.Services, seed.WalletId);
        Assert.Equal(150m, balances.Available);
        Assert.Equal(0m, balances.Reserved);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await context.WalletTransactions.CountAsync(item => item.WithdrawalRequestId == seed.WithdrawalId && item.OperationType == WalletTransactionType.WithdrawalFinalizePayout));
    }

    [Fact]
    public async Task WorkQueueWithdrawalActionsTrackRequestedApprovedAndTerminalStates()
    {
        await factory.InitializeDatabaseAsync();
        var requested = await WithdrawalIntegrationTestHelpers.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested);
        var approved = await WithdrawalIntegrationTestHelpers.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Approved, adminUserId: requested.AdminUserId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", requested.AdminUserId));

        var requestedQueue = await ReadQueueTextAsync(client);
        Assert.Contains(requested.WithdrawalId, requestedQueue);
        Assert.Contains("\"nextActions\":[\"Approve\",\"Reject\"]", requestedQueue);
        Assert.Contains(approved.WithdrawalId, requestedQueue);
        Assert.Contains("\"nextActions\":[\"MarkPaid\",\"MarkFailed\"]", requestedQueue);

        using var approve = await client.PutAsJsonAsync($"/api/admin/withdrawals/{requested.WithdrawalId}/approve", new { Note = "Approved" });
        using var paid = await client.PutAsJsonAsync($"/api/admin/withdrawals/{approved.WithdrawalId}/mark-paid", new { PayoutReference = "ops-ref-queue" });

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        var transitionedQueue = await ReadQueueTextAsync(client);
        Assert.Contains(requested.WithdrawalId, transitionedQueue);
        Assert.Contains("\"nextActions\":[\"MarkPaid\",\"MarkFailed\"]", transitionedQueue);
        Assert.DoesNotContain(approved.WithdrawalId, transitionedQueue);
    }

    private static async Task<string> ReadQueueTextAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/admin/work-queue?category=Withdrawal&PageNumber=1&PageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
