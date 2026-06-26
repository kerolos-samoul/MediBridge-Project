using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class CompanyWalletContractTests
{
    [Fact]
    public async Task GetCompanyWallet_ReturnsSuccessEnvelopeBalanceAndTransactionPage()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedCompanyWalletAsync(factory, availableBalance: 250m, reservedBalance: 25m);
        await SeedWalletTransactionAsync(factory, seed.WalletId, amount: 100m);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        var response = await client.GetAsync("/api/company/wallet?PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal(seed.WalletId, data.GetProperty("WalletId").GetString());
        Assert.Equal(250m, data.GetProperty("AvailableBalance").GetDecimal());
        Assert.Equal(25m, data.GetProperty("ReservedBalance").GetDecimal());
        Assert.Equal("EGP", data.GetProperty("Currency").GetString());
        var transactions = data.GetProperty("Transactions");
        Assert.True(transactions.TryGetProperty("Page", out var page));
        Assert.True(transactions.TryGetProperty("Items", out var items));
        Assert.Equal(1, page.GetProperty("PageNumber").GetInt32());
        Assert.Equal(20, page.GetProperty("PageSize").GetInt32());
        Assert.Equal(1, page.GetProperty("TotalCount").GetInt32());
        Assert.Equal("TopUp", items[0].GetProperty("OperationType").GetString());
    }

    [Theory]
    [InlineData("/api/company/wallet?PageNumber=0")]
    [InlineData("/api/company/wallet?PageSize=0")]
    [InlineData("/api/company/wallet?PageSize=101")]
    public async Task GetCompanyWallet_WithInvalidPagination_Returns400Envelope(string path)
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedCompanyWalletAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
    }

    [Fact]
    public async Task CompanyWalletEndpoints_RequireCompanyAuthorization()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var anonymousClient = factory.CreateClient();
        using var doctorClient = factory.CreateClient();
        doctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor"));

        var unauthorizedGet = await anonymousClient.GetAsync("/api/company/wallet");
        var forbiddenGet = await doctorClient.GetAsync("/api/company/wallet");
        using var topUp = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/topup")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(Phase5ContractTestHelpers.CreateTopUpRequest(100m))
        };
        Phase5ContractTestHelpers.AddIdempotencyKey(topUp);
        var forbiddenPost = await doctorClient.SendAsync(topUp);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenGet.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenPost.StatusCode);
    }

    [Fact]
    public async Task PostCompanyWalletTopUp_ReturnsSuccessEnvelopeAndDuplicateReplayEnvelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedCompanyWalletAsync(factory, availableBalance: 50m);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        var key = $"idem-{Guid.NewGuid():N}";

        var firstResponse = await SendTopUpAsync(client, 125.50m, key);
        var replayResponse = await SendTopUpAsync(client, 125.50m, key);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await firstResponse.Content.ReadAsStreamAsync()))
        {
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal(seed.WalletId, data.GetProperty("WalletId").GetString());
            Assert.True(data.TryGetProperty("TransactionId", out _));
            Assert.Equal(175.50m, data.GetProperty("AvailableBalance").GetDecimal());
            Assert.Equal(0m, data.GetProperty("ReservedBalance").GetDecimal());
            Assert.Equal("EGP", data.GetProperty("Currency").GetString());
            Assert.Equal("Created", data.GetProperty("IdempotencyStatus").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        using (var document = await JsonDocument.ParseAsync(await replayResponse.Content.ReadAsStreamAsync()))
        {
            Assert.Equal("Replayed", Phase5ContractTestHelpers.AssertDataEnvelope(document, 200).GetProperty("IdempotencyStatus").GetString());
        }
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("below-minimum")]
    [InlineData("too-many-decimals")]
    public async Task PostCompanyWalletTopUp_WithInvalidRequest_Returns400Envelope(string scenario)
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedCompanyWalletAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        var amount = scenario switch
        {
            "below-minimum" => 99.99m,
            "too-many-decimals" => 100.001m,
            _ => 100m
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/topup")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(Phase5ContractTestHelpers.CreateTopUpRequest(amount))
        };
        if (scenario != "missing-key")
        {
            Phase5ContractTestHelpers.AddIdempotencyKey(request);
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 400);
    }

    [Fact]
    public async Task PostCompanyWalletTopUp_WithConflictingIdempotency_Returns409Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedCompanyWalletAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);
        var key = $"idem-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.OK, (await SendTopUpAsync(client, 100m, key)).StatusCode);

        var conflict = await SendTopUpAsync(client, 150m, key);

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var document = await JsonDocument.ParseAsync(await conflict.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 409);
    }

    [Fact]
    public async Task PostCompanyWalletTopUp_AfterRateLimit_Returns429Envelope()
    {
        using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();
        var seed = await SeedCompanyWalletAsync(factory);
        Phase5ContractTestHelpers.AuthorizeAsCompany(client, seed.CompanyUserId);

        HttpResponseMessage response = new(HttpStatusCode.OK);
        for (var index = 0; index < 11; index++)
        {
            response = await SendTopUpAsync(client, 100m + index, $"idem-{Guid.NewGuid():N}");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 429);
    }

    private static async Task<HttpResponseMessage> SendTopUpAsync(HttpClient client, decimal amount, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/topup")
        {
            Content = Phase5ContractTestHelpers.CreateJsonContent(Phase5ContractTestHelpers.CreateTopUpRequest(amount))
        };
        Phase5ContractTestHelpers.AddIdempotencyKey(request, idempotencyKey);
        return await client.SendAsync(request);
    }

    private static async Task<WalletContractSeed> SeedCompanyWalletAsync(ContractWebAppFactory factory, decimal availableBalance = 0m, decimal reservedBalance = 0m, AccountStatus status = AccountStatus.Approved)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        var user = CreateUser($"phase5-wallet-company-{suffix}@example.test", UserRole.Company, status);
        var company = new CompanyProfile
        {
            Id = $"company-{suffix}",
            UserId = user.Id,
            CompanyName = "Phase 5 Wallet Pharma",
            LicenseNumber = $"license-{suffix}",
            ContactName = "Contact",
            VerificationDocumentType = "License",
            VerificationOriginalFileName = "license.pdf",
            VerificationContentType = "application/pdf",
            VerificationSizeBytes = 1024,
            VerificationReference = $"verification/{suffix}"
        };
        var wallet = new Wallet
        {
            Id = $"wallet-{suffix}",
            OwnerType = WalletOwnerType.Company,
            OwnerId = company.Id,
            OwnerUserId = user.Id,
            AvailableBalance = availableBalance,
            ReservedBalance = reservedBalance,
            Currency = "EGP"
        };

        await context.Users.AddAsync(user);
        await context.CompanyProfiles.AddAsync(company);
        await context.Wallets.AddAsync(wallet);
        await context.SaveChangesAsync();
        return new WalletContractSeed(user.Id, company.Id, wallet.Id);
    }

    private static async Task SeedWalletTransactionAsync(ContractWebAppFactory factory, string walletId, decimal amount)
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

    private static MediBridgeIdentityUser CreateUser(string email, UserRole role, AccountStatus status)
    {
        var normalized = email.ToUpperInvariant();
        return new MediBridgeIdentityUser
        {
            Id = Guid.NewGuid().ToString("N"),
            UserName = email,
            NormalizedUserName = normalized,
            Email = email,
            NormalizedEmail = normalized,
            Role = role,
            AccountStatus = status,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private sealed record WalletContractSeed(string CompanyUserId, string CompanyId, string WalletId);
}
