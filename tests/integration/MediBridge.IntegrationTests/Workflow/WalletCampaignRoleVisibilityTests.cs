using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignRoleVisibilityTests
{
    [Fact]
    public async Task WorkflowRoleVisibility_EnforcesCompanyAdminDoctorBoundaries()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var other = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var ownerCompany = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, owner.CompanyUserId);
        using var otherCompany = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, other.CompanyUserId);
        using var doctor = WalletCampaignWorkflowTestHelpers.CreateDoctorClient(factory, owner.DoctorUserId);

        using var draft = await ownerCompany.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Private campaign", Description = "Ownership boundary smoke test." });
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        using var document = JsonDocument.Parse(await draft.Content.ReadAsStringAsync());
        var campaignId = document.RootElement.GetProperty("Data").GetProperty("campaignId").GetString()!;

        using var companyQueueRows = await ownerCompany.GetAsync($"/api/admin/campaigns/{campaignId}/queue");
        Assert.Equal(HttpStatusCode.Forbidden, companyQueueRows.StatusCode);

        using var doctorWallet = await doctor.GetAsync("/api/company/wallet");
        Assert.Equal(HttpStatusCode.Forbidden, doctorWallet.StatusCode);

        using var doctorCampaign = await doctor.GetAsync($"/api/company/campaigns/{campaignId}/target-preview");
        Assert.Equal(HttpStatusCode.Forbidden, doctorCampaign.StatusCode);

        using var otherSummary = await otherCompany.GetAsync($"/api/company/campaigns/{campaignId}/queue-summary");
        Assert.Equal(HttpStatusCode.Forbidden, otherSummary.StatusCode);
        using var summaryDocument = JsonDocument.Parse(await otherSummary.Content.ReadAsStringAsync());
        Assert.Equal(403, summaryDocument.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, summaryDocument.RootElement.GetProperty("Data").ValueKind);
    }
}
