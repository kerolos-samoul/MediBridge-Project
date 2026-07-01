using System.Net;
using System.Net.Http.Json;
using MediBridge.ContractTests.TestHost;
using Xunit;

namespace MediBridge.ContractTests.Admin;

public sealed class AdminDoctorPricingContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task DoctorPricing_ValidPrice_ReturnsSuccessEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateAdminClient(factory, ids.AdminUserId);

        using var response = await PutPriceAsync(client, ids.DoctorProfileId, 50.25m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(ids.DoctorProfileId, data.GetProperty("doctorId").GetString());
        Assert.Equal(50.25m, data.GetProperty("pricePerMessage").GetDecimal());
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(1.001)]
    public async Task DoctorPricing_InvalidPrice_ReturnsValidationEnvelope(double? price)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateAdminClient(factory, ids.AdminUserId);

        using var response = await client.PutAsJsonAsync(
            Route(ids.DoctorProfileId),
            new { PricePerMessage = price, Reason = "Contract validation" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertEnvelopeAsync(response, 400);
    }

    [Fact]
    public async Task DoctorPricing_WithoutToken_ReturnsUnauthorizedEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = factory.CreateClient();

        using var response = await PutPriceAsync(client, ids.DoctorProfileId, 50m);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, 401);
    }

    [Theory]
    [InlineData("Company")]
    [InlineData("Doctor")]
    public async Task DoctorPricing_WithNonAdminToken_ReturnsForbiddenEnvelope(string role)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = role == "Company"
            ? CreateCompanyClient(factory, ids.CompanyUserId)
            : CreateDoctorClient(factory, ids.DoctorUserId);

        using var response = await PutPriceAsync(client, ids.DoctorProfileId, 50m);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertEnvelopeAsync(response, 403);
    }

    [Fact]
    public async Task DoctorPricing_ForUnknownDoctor_ReturnsNotFoundEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await CreateApprovedActorsAsync(factory);
        using var client = CreateAdminClient(factory, ids.AdminUserId);

        using var response = await PutPriceAsync(client, Guid.NewGuid().ToString("N"), 50m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, 404);
    }

    private static Task<HttpResponseMessage> PutPriceAsync(HttpClient client, string doctorId, decimal? price)
        => client.PutAsJsonAsync(Route(doctorId), new { PricePerMessage = price, Reason = "Contract price" });

    private static string Route(string doctorId)
        => WalletCampaignWorkflowRoutes.AdminDoctorPrice.Replace("{doctorId}", doctorId, StringComparison.Ordinal);
}
