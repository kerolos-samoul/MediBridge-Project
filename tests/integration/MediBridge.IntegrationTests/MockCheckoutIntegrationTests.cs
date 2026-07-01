using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.DTOs.Payments;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class MockCheckoutIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public MockCheckoutIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task MockCheckout_CreditsAvailableBalanceAndPersistsPaymentTransactionAndLedger()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var walletId = await SeedWalletAsync(company, availableBalance: 25m, reservedBalance: 10m);
        using var client = CreateCompanyClient(company.UserId);

        using var response = await SendCheckoutAsync(client, 150.25m, $"checkout-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        var paymentId = data.GetProperty("paymentId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(paymentId));
        Assert.Equal(25m, data.GetProperty("walletBalanceBefore").GetDecimal());
        Assert.Equal(175.25m, data.GetProperty("walletBalanceAfter").GetDecimal());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var payment = await context.MockPaymentTransactions.AsNoTracking().SingleAsync(item => item.PaymentId == paymentId);
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == walletId);
        var transaction = await context.WalletTransactions.AsNoTracking().SingleAsync(item => item.Id == payment.WalletTransactionId);
        var ledger = await context.WalletLedgerEntries.AsNoTracking().SingleAsync(item => item.WalletTransactionId == transaction.Id);

        Assert.Equal(company.CompanyId, payment.CompanyId);
        Assert.Equal(150.25m, payment.Amount);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(175.25m, wallet.AvailableBalance);
        Assert.Equal(10m, wallet.ReservedBalance);
        Assert.Equal(WalletTransactionType.TopUp, transaction.OperationType);
        Assert.Equal(WalletLedgerEntryDirection.Credit, ledger.Direction);
        Assert.Equal(WalletBalanceType.Available, ledger.BalanceType);
        Assert.Equal(company.CompanyId, ledger.CompanyId);
    }

    [Fact]
    public async Task MockCheckout_ReplayReturnsOriginalPaymentAndConflictDoesNotMutateWallet()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var walletId = await SeedWalletAsync(company);
        using var client = CreateCompanyClient(company.UserId);
        var key = $"checkout-{Guid.NewGuid():N}";

        using var first = await SendCheckoutAsync(client, 100m, key);
        using var replay = await SendCheckoutAsync(client, 100m, key);
        using var conflict = await SendCheckoutAsync(client, 125m, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var firstDocument = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
        using var replayDocument = await JsonDocument.ParseAsync(await replay.Content.ReadAsStreamAsync());
        Assert.Equal(
            firstDocument.RootElement.GetProperty("Data").GetProperty("paymentId").GetString(),
            replayDocument.RootElement.GetProperty("Data").GetProperty("paymentId").GetString());

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(100m, (await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == walletId)).AvailableBalance);
        Assert.Equal(1, await context.MockPaymentTransactions.CountAsync(item => item.CompanyId == company.CompanyId));
        Assert.Equal(1, await context.WalletTransactions.CountAsync(item => item.WalletId == walletId));
        Assert.Equal(1, await context.WalletLedgerEntries.CountAsync(item => item.WalletId == walletId));
    }

    [Fact]
    public async Task MockCheckout_ForApprovedCompanyWithoutWallet_CreatesWalletAndCreditsIt()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        using var client = CreateCompanyClient(company.UserId);

        using var response = await SendCheckoutAsync(client, 100m, $"checkout-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(
            item => item.OwnerType == WalletOwnerType.Company && item.OwnerId == company.CompanyId);
        Assert.Equal(company.UserId, wallet.OwnerUserId);
        Assert.Equal(100m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);
    }

    [Fact]
    public async Task MockCheckoutService_ForApprovedCompanyWithoutWallet_Completes()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyWalletService>();

        var result = await service.CreateMockTopUpAsync(
            company.UserId,
            $"checkout-{Guid.NewGuid():N}",
            new MockTopUpRequestDto(100m, "EGP"));

        Assert.Equal(100m, result.WalletBalanceAfter);
    }

    private HttpClient CreateCompanyClient(string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", userId));
        return client;
    }

    private static async Task<HttpResponseMessage> SendCheckoutAsync(HttpClient client, decimal amount, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { Amount = amount, Currency = "EGP" }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    private async Task<string> SeedWalletAsync(Phase5CompanySeed company, decimal availableBalance = 0m, decimal reservedBalance = 0m)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = new Wallet
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = WalletOwnerType.Company,
            OwnerId = company.CompanyId,
            OwnerUserId = company.UserId,
            AvailableBalance = availableBalance,
            ReservedBalance = reservedBalance,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow
        };
        await context.Wallets.AddAsync(wallet);
        await context.SaveChangesAsync();
        return wallet.Id;
    }
}
