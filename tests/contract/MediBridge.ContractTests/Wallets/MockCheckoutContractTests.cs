using System.Net;
using System.Net.Http.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests.Wallets;

public sealed class CompanyWalletMockCheckoutContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task MockCheckout_WithValidRequest_ReturnsSucceededPaymentEnvelopeAnd200()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);
        using var request = CreateTopUpRequest(100m, "EGP", $"checkout-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(ids.CompanyProfileId, data.GetProperty("companyId").GetString());
        Assert.Equal("Succeeded", data.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("paymentId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("transactionReference").GetString()));
        Assert.Equal(0m, data.GetProperty("walletBalanceBefore").GetDecimal());
        Assert.Equal(100m, data.GetProperty("walletBalanceAfter").GetDecimal());
    }

    [Fact]
    public async Task MockCheckout_WithoutIdempotencyKey_ReturnsValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);

        using var response = await client.PostAsJsonAsync(WalletCampaignWorkflowRoutes.CompanyWalletMockCheckout, new { Amount = 50m, Currency = "EGP" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Theory]
    [InlineData(0, "EGP")]
    [InlineData(-1, "EGP")]
    [InlineData(10.123, "EGP")]
    [InlineData(10, "USD")]
    public async Task MockCheckout_WithInvalidAmountOrCurrency_ReturnsValidationEnvelope(decimal amount, string currency)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);
        using var request = CreateTopUpRequest(amount, currency, $"invalid-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task MockCheckout_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(WalletCampaignWorkflowRoutes.CompanyWalletMockCheckout, new { Amount = 50m, Currency = "EGP" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Fact]
    public async Task MockCheckout_WithDoctorToken_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateDoctorClient(factory, ids.DoctorUserId);
        using var request = CreateTopUpRequest(50m, "EGP", $"doctor-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task MockCheckout_WithConflictingReplay_ReturnsConflictEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);
        var idempotencyKey = $"conflict-{Guid.NewGuid():N}";
        using var firstRequest = CreateTopUpRequest(25m, "EGP", idempotencyKey);
        using var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var conflictRequest = CreateTopUpRequest(30m, "EGP", idempotencyKey);
        using var conflictResponse = await client.SendAsync(conflictRequest);

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        await AssertEnvelopeAsync(conflictResponse, 409);
        Assert.Equal(25m, await GetCompanyWalletBalanceAsync(factory, ids.CompanyProfileId));
    }

    private static HttpRequestMessage CreateTopUpRequest(decimal amount, string currency, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WalletCampaignWorkflowRoutes.CompanyWalletMockCheckout)
        {
            Content = JsonContent.Create(new { Amount = amount, Currency = currency })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
