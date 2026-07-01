using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignEnvelopeTests
{
    [Fact]
    public async Task WalletCampaignWorkflow_AllResponsesUseStandardEnvelope()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        using var doctor = WalletCampaignWorkflowTestHelpers.CreateDoctorClient(factory, actors.DoctorUserId);
        using var anonymous = factory.CreateClient();

        using var success = await company.GetAsync("/api/company/wallet");
        await AssertEnvelopeAsync(success, HttpStatusCode.OK);

        using var validation = await SendTopUpAsync(company, "envelope-invalid-001", 0m, "EGP");
        await AssertEnvelopeAsync(validation, HttpStatusCode.BadRequest);

        using var unauthorized = await anonymous.GetAsync("/api/company/wallet");
        await AssertEnvelopeAsync(unauthorized, HttpStatusCode.Unauthorized);

        using var forbidden = await doctor.GetAsync("/api/company/wallet");
        await AssertEnvelopeAsync(forbidden, HttpStatusCode.Forbidden);

        using var notFound = await company.GetAsync($"/api/company/campaigns/{Guid.NewGuid():N}/target-preview");
        await AssertEnvelopeAsync(notFound, HttpStatusCode.NotFound);

        const string conflictKey = "envelope-conflict-001";
        using var initial = await SendTopUpAsync(company, conflictKey, 10m, "EGP");
        await AssertEnvelopeAsync(initial, HttpStatusCode.OK);
        using var conflict = await SendTopUpAsync(company, conflictKey, 11m, "EGP");
        await AssertEnvelopeAsync(conflict, HttpStatusCode.Conflict);
    }

    private static Task<HttpResponseMessage> SendTopUpAsync(HttpClient client, string key, decimal amount, string currency)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
        {
            Content = JsonContent.Create(new { Amount = amount, Currency = currency })
        };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task AssertEnvelopeAsync(HttpResponseMessage response, HttpStatusCode statusCode)
    {
        Assert.Equal(statusCode, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal((int)statusCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.String, root.GetProperty("Message").ValueKind);
        Assert.True(root.TryGetProperty("Data", out _));
        Assert.Equal(3, root.EnumerateObject().Count());
    }
}
