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

public sealed class MockCheckoutIdempotencyTests
{
    [Fact]
    public async Task MockCheckout_ReplayReturnsOriginalResultAndConflictDoesNotMutateBalance()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        var idempotencyKey = $"replay-{Guid.NewGuid():N}";

        using var firstResponse = await client.SendAsync(CreateTopUpRequest(40m, "EGP", idempotencyKey));
        using var replayResponse = await client.SendAsync(CreateTopUpRequest(40m, "EGP", idempotencyKey));
        using var conflictResponse = await client.SendAsync(CreateTopUpRequest(41m, "EGP", idempotencyKey));

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        using var firstDocument = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        using var replayDocument = JsonDocument.Parse(await replayResponse.Content.ReadAsStringAsync());
        var firstData = firstDocument.RootElement.GetProperty("Data");
        var replayData = replayDocument.RootElement.GetProperty("Data");
        Assert.Equal(firstData.GetProperty("paymentId").GetString(), replayData.GetProperty("paymentId").GetString());
        Assert.Equal(firstData.GetProperty("transactionReference").GetString(), replayData.GetProperty("transactionReference").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(wallet => wallet.OwnerType == WalletOwnerType.Company && wallet.OwnerId == ids.CompanyProfileId);
        Assert.Equal(40m, wallet.AvailableBalance);
        Assert.Equal(1, await context.MockPaymentTransactions.CountAsync(payment => payment.CompanyId == ids.CompanyProfileId));
        Assert.Equal(1, await context.WalletTransactions.CountAsync(transaction => transaction.OperationType == WalletTransactionType.TopUp));
    }

    private static HttpRequestMessage CreateTopUpRequest(decimal amount, string currency, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
        {
            Content = JsonContent.Create(new { Amount = amount, Currency = currency })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
