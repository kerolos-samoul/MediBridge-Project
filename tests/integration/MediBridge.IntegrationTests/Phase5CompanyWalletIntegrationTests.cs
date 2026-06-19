using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase5CompanyWalletIntegrationTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public Phase5CompanyWalletIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task TopUp_CreditsAvailableBalanceOnlyAndLeavesReservedBalanceUnchanged()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var walletId = await SeedCompanyWalletAsync(company.CompanyId, company.UserId, availableBalance: 25m, reservedBalance: 15m);
        using var client = CreateCompanyClient(company.UserId);

        var response = await SendTopUpAsync(client, 150.25m, $"idem-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.SingleAsync(candidate => candidate.Id == walletId);
        Assert.Equal(175.25m, wallet.AvailableBalance);
        Assert.Equal(15m, wallet.ReservedBalance);
    }

    [Fact]
    public async Task TopUp_CreatesOneAppendOnlyTransactionAndImmutableAvailableLedgerEntry()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var walletId = await SeedCompanyWalletAsync(company.CompanyId, company.UserId);
        using var client = CreateCompanyClient(company.UserId);

        var response = await SendTopUpAsync(client, 200m, $"idem-{Guid.NewGuid():N}", "Safe top-up reference");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var transaction = await context.WalletTransactions.SingleAsync(transaction => transaction.WalletId == walletId);
        var ledger = await context.WalletLedgerEntries.SingleAsync(entry => entry.WalletId == walletId);
        Assert.Equal(WalletTransactionType.TopUp, transaction.OperationType);
        Assert.Equal(200m, transaction.Amount);
        Assert.Equal("Safe top-up reference", transaction.Description);
        Assert.Equal(transaction.Id, ledger.WalletTransactionId);
        Assert.Equal(WalletLedgerEntryDirection.Credit, ledger.Direction);
        Assert.Equal(WalletBalanceType.Available, ledger.BalanceType);
        Assert.Equal(200m, ledger.Amount);
        Assert.Equal(company.CompanyId, ledger.CompanyId);
        Assert.Equal("EGP", ledger.Currency);
    }

    [Fact]
    public async Task DuplicateTopUpRetry_ProducesNoDuplicateBalanceTransactionOrLedgerEffect()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var walletId = await SeedCompanyWalletAsync(company.CompanyId, company.UserId, availableBalance: 10m);
        using var client = CreateCompanyClient(company.UserId);
        var key = $"idem-{Guid.NewGuid():N}";

        var firstResponse = await SendTopUpAsync(client, 100m, key);
        var replayResponse = await SendTopUpAsync(client, 100m, key);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.SingleAsync(candidate => candidate.Id == walletId);
        Assert.Equal(110m, wallet.AvailableBalance);
        Assert.Equal(1, await context.WalletTransactions.CountAsync(transaction => transaction.WalletId == walletId));
        Assert.Equal(1, await context.WalletLedgerEntries.CountAsync(entry => entry.WalletId == walletId));
    }

    [Fact]
    public async Task ConcurrentDistinctTopUps_SerializeBalanceUpdatesAndAllCommit()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var walletId = await SeedCompanyWalletAsync(company.CompanyId, company.UserId);
        using var startGate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 8)
            .Select(index => Task.Run(async () =>
            {
                startGate.Wait();
                await TopUpThroughServiceScopeAsync(company.UserId, $"idem-{Guid.NewGuid():N}", 100m + index);
            }))
            .ToArray();

        startGate.Set();
        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks));

        Assert.Null(exception);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.SingleAsync(candidate => candidate.Id == walletId);
        Assert.Equal(828m, wallet.AvailableBalance);
        Assert.Equal(8, await context.WalletTransactions.CountAsync(transaction => transaction.WalletId == walletId));
        Assert.Equal(8, await context.WalletLedgerEntries.CountAsync(entry => entry.WalletId == walletId));
    }

    [Fact]
    public async Task ConcurrentDuplicateTopUpRetries_ReplayWithoutDuplicateFinancialEffects()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var walletId = await SeedCompanyWalletAsync(company.CompanyId, company.UserId);
        var idempotencyKey = $"idem-{Guid.NewGuid():N}";
        using var startGate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                startGate.Wait();
                await TopUpThroughServiceScopeAsync(company.UserId, idempotencyKey, 125m);
            }))
            .ToArray();

        startGate.Set();
        var exception = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks));

        Assert.Null(exception);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.SingleAsync(candidate => candidate.Id == walletId);
        Assert.Equal(125m, wallet.AvailableBalance);
        Assert.Equal(1, await context.WalletTransactions.CountAsync(transaction => transaction.WalletId == walletId));
        Assert.Equal(1, await context.WalletLedgerEntries.CountAsync(entry => entry.WalletId == walletId));
    }

    [Fact]
    public async Task WalletQueryAndTopUp_DenyNonCompanyUnapprovedAndAnonymousUsers()
    {
        await factory.InitializeDatabaseAsync();
        var approvedCompany = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        await SeedCompanyWalletAsync(approvedCompany.CompanyId, approvedCompany.UserId);
        var pendingCompany = await SeedCompanyAsync(AccountStatus.Pending);
        await SeedCompanyWalletAsync(pendingCompany.CompanyId, pendingCompany.UserId);
        using var anonymousClient = factory.CreateClient();
        using var doctorClient = factory.CreateClient();
        doctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", $"doctor-{Guid.NewGuid():N}"));
        using var pendingClient = CreateCompanyClient(pendingCompany.UserId);

        var anonymousGet = await anonymousClient.GetAsync("/api/company/wallet");
        var doctorGet = await doctorClient.GetAsync("/api/company/wallet");
        var pendingGet = await pendingClient.GetAsync("/api/company/wallet");
        var pendingTopUp = await SendTopUpAsync(pendingClient, 100m, $"idem-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, doctorGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, pendingGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, pendingTopUp.StatusCode);
    }

    [Fact]
    public async Task WalletQuery_ReturnsOnlyActorCompanyWalletAndTransactions()
    {
        await factory.InitializeDatabaseAsync();
        var firstCompany = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var secondCompany = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var firstWalletId = await SeedCompanyWalletAsync(firstCompany.CompanyId, firstCompany.UserId, availableBalance: 10m);
        var secondWalletId = await SeedCompanyWalletAsync(secondCompany.CompanyId, secondCompany.UserId, availableBalance: 999m);
        await SeedTransactionAsync(firstWalletId, 100m);
        await SeedTransactionAsync(secondWalletId, 300m);
        using var client = CreateCompanyClient(firstCompany.UserId);

        var response = await client.GetAsync("/api/company/wallet?PageNumber=1&PageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(firstWalletId, data.GetProperty("WalletId").GetString());
        Assert.Equal(10m, data.GetProperty("AvailableBalance").GetDecimal());
        var items = data.GetProperty("Transactions").GetProperty("Items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal(100m, items[0].GetProperty("Amount").GetDecimal());
    }

    [Theory]
    [InlineData("missing-key", 100)]
    [InlineData("below-minimum", 99.99)]
    [InlineData("too-many-decimals", 100.001)]
    [InlineData("zero", 0)]
    [InlineData("negative", -100)]
    public async Task TopUp_RejectsInvalidRequestsWithoutBalanceChanges(string scenario, decimal amount)
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var walletId = await SeedCompanyWalletAsync(company.CompanyId, company.UserId, availableBalance: 75m);
        using var client = CreateCompanyClient(company.UserId);

        var response = await SendTopUpAsync(client, amount, scenario == "missing-key" ? null : $"idem-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.SingleAsync(candidate => candidate.Id == walletId);
        Assert.Equal(75m, wallet.AvailableBalance);
        Assert.Equal(0, await context.WalletTransactions.CountAsync(transaction => transaction.WalletId == walletId));
        Assert.Equal(0, await context.WalletLedgerEntries.CountAsync(entry => entry.WalletId == walletId));
    }

    private HttpClient CreateCompanyClient(string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", userId));
        return client;
    }

    private async Task<HttpResponseMessage> SendTopUpAsync(HttpClient client, decimal amount, string? idempotencyKey, string? description = "Phase 5 top-up")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/topup")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { Amount = amount, Description = description }), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private async Task<string> SeedCompanyWalletAsync(string companyId, string companyUserId, decimal availableBalance = 0m, decimal reservedBalance = 0m)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = new Wallet
        {
            Id = Guid.NewGuid().ToString("N"),
            OwnerType = WalletOwnerType.Company,
            OwnerId = companyId,
            OwnerUserId = companyUserId,
            AvailableBalance = availableBalance,
            ReservedBalance = reservedBalance,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow
        };

        await context.Wallets.AddAsync(wallet);
        await context.SaveChangesAsync();
        return wallet.Id;
    }

    private async Task TopUpThroughServiceScopeAsync(string companyUserId, string idempotencyKey, decimal amount)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICompanyWalletService>();
        await service.TopUpCompanyWalletAsync(
            companyUserId,
            idempotencyKey,
            new TopUpCompanyWalletRequestDto { Amount = amount, Description = "Concurrent top-up" },
            CancellationToken.None);
    }

    private async Task SeedTransactionAsync(string walletId, decimal amount)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.WalletTransactions.AddAsync(new WalletTransaction
        {
            Id = Guid.NewGuid().ToString("N"),
            WalletId = walletId,
            OperationType = WalletTransactionType.TopUp,
            IdempotencyKey = $"idem-{Guid.NewGuid():N}",
            Amount = amount,
            Description = "Seeded top-up",
            CreatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    private async Task<Phase5CompanySeed> SeedCompanyAsync(AccountStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new Repository.Data.Identity.MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = $"phase5-wallet-company-{suffix}@example.test",
            NormalizedUserName = $"PHASE5-WALLET-COMPANY-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            Email = $"phase5-wallet-company-{suffix}@example.test",
            NormalizedEmail = $"PHASE5-WALLET-COMPANY-{suffix.ToUpperInvariant()}@EXAMPLE.TEST",
            Role = UserRole.Company,
            AccountStatus = status,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        var company = new Core.Entities.Profiles.CompanyProfile
        {
            Id = $"company-{suffix}",
            UserId = user.Id,
            CompanyName = "Phase 5 Wallet Pending Pharma",
            LicenseNumber = $"license-{suffix}",
            ContactName = "Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };

        await context.Users.AddAsync(user);
        await context.CompanyProfiles.AddAsync(company);
        await context.SaveChangesAsync();
        return new Phase5CompanySeed(user.Id, company.Id);
    }
}
