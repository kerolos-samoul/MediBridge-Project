using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Payments;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignMockPaymentNoProviderTests
{
    private static readonly string[] ForbiddenTerms =
    [
        "redirect", "webhook", "callback", "credential", "providerconfig", "providerstatus", "externalprovider"
    ];

    [Fact]
    public async Task MockPaymentWorkflow_DoesNotExposeProviderIntegrationSurface()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
        {
            Content = JsonContent.Create(new { Amount = 25m, Currency = "EGP" })
        };
        request.Headers.Add("Idempotency-Key", "no-provider-smoke-001");

        using var response = await company.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ForbiddenTerms, term => Normalize(responseJson).Contains(term, StringComparison.Ordinal));
        using var document = JsonDocument.Parse(responseJson);
        var data = document.RootElement.GetProperty("Data");
        var paymentId = data.GetProperty("paymentId").GetString()!;
        Assert.Equal("Succeeded", data.GetProperty("status").GetString());

        var payment = await WalletCampaignWorkflowTestHelpers.FindPaymentAsync(factory, paymentId);
        Assert.NotNull(payment);
        Assert.DoesNotContain(
            typeof(MockPaymentTransaction).GetProperties(),
            property => ForbiddenTerms.Any(term => Normalize(property.Name).Contains(term, StringComparison.Ordinal)));
        Assert.All(
            typeof(MockPaymentTransaction).GetProperties().Where(property => property.PropertyType == typeof(string)),
            property => Assert.DoesNotContain(
                ForbiddenTerms,
                term => Normalize(property.GetValue(payment)?.ToString() ?? string.Empty).Contains(term, StringComparison.Ordinal)));
    }

    private static string Normalize(string value)
        => value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}
