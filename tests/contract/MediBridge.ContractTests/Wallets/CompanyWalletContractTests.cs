using System.Net;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests.Wallets;

public sealed class CompanyWalletContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task CompanyWallet_WithCompanyToken_ReturnsWalletEnvelopeAnd200()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);

        using var response = await client.GetAsync(WalletCampaignWorkflowRoutes.CompanyWallet);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(ids.CompanyProfileId, data.GetProperty("companyId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("walletId").GetString()));
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task CompanyWallet_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(WalletCampaignWorkflowRoutes.CompanyWallet);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Fact]
    public async Task CompanyWallet_WithDoctorToken_ReturnsForbiddenEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateDoctorClient(factory, ids.DoctorUserId);

        using var response = await client.GetAsync(WalletCampaignWorkflowRoutes.CompanyWallet);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task CompanyWallet_WhenWalletRowMissing_ReturnsOkNotNotFound()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateCompanyClient(factory, ids.CompanyUserId);

        using var response = await client.GetAsync(WalletCampaignWorkflowRoutes.CompanyWallet);

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertEnvelopeAsync(response, 200, "Success");
    }
}
