using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Wallets;

public sealed class CompanyWalletConcurrencyTests
{
    [Fact]
    public async Task ConcurrentFirstTopUps_CreateOneWalletAndApplyEachUniqueTopUpOnce()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        var amounts = Enumerable.Range(1, 6).Select(index => index * 10m).ToArray();

        var responses = await Task.WhenAll(amounts.Select((amount, index) =>
            client.SendAsync(CreateTopUpRequest(amount, $"concurrent-first-{index}-{Guid.NewGuid():N}"))));

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var wallets = await context.Wallets
                .AsNoTracking()
                .Where(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == ids.CompanyProfileId)
                .ToListAsync();
            var wallet = Assert.Single(wallets);

            Assert.Equal(amounts.Sum(), wallet.AvailableBalance);
            Assert.Equal(amounts.Length, await context.MockPaymentTransactions.CountAsync(payment => payment.CompanyId == ids.CompanyProfileId));
            Assert.Equal(amounts.Length, await context.WalletTransactions.CountAsync(transaction => transaction.WalletId == wallet.Id));
            Assert.Equal(amounts.Length, await context.WalletLedgerEntries.CountAsync(entry => entry.WalletId == wallet.Id));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task ConcurrentIdenticalTopUps_ReturnOriginalResultAndApplyCreditOnce()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        var idempotencyKey = $"concurrent-replay-{Guid.NewGuid():N}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => client.SendAsync(CreateTopUpRequest(25m, idempotencyKey))));

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var paymentIds = new List<string>();
            foreach (var response in responses)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                paymentIds.Add(document.RootElement.GetProperty("Data").GetProperty("paymentId").GetString()!);
            }

            Assert.Single(paymentIds.Distinct(StringComparer.Ordinal));

            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            var wallet = await context.Wallets
                .AsNoTracking()
                .SingleAsync(candidate => candidate.OwnerType == WalletOwnerType.Company && candidate.OwnerId == ids.CompanyProfileId);

            Assert.Equal(25m, wallet.AvailableBalance);
            Assert.Equal(1, await context.MockPaymentTransactions.CountAsync(payment => payment.CompanyId == ids.CompanyProfileId));
            Assert.Equal(1, await context.WalletTransactions.CountAsync(transaction => transaction.WalletId == wallet.Id));
            Assert.Equal(1, await context.WalletLedgerEntries.CountAsync(entry => entry.WalletId == wallet.Id));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static HttpRequestMessage CreateTopUpRequest(decimal amount, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
        {
            Content = JsonContent.Create(new { Amount = amount, Currency = "EGP" })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
