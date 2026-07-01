using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CompanyCampaignOwnershipTests
{
    [Fact]
    public async Task Company_CannotUploadAssetToAnotherCompanyCampaign()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var other = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var ownerClient = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, owner.CompanyUserId);
        using var otherClient = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, other.CompanyUserId);

        using var draftResponse = await ownerClient.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Owned draft", Description = "Owner-only asset upload" });
        using var document = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        var campaignId = document.RootElement.GetProperty("Data").GetProperty("campaignId").GetString()!;

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 1 });
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "campaign.png");
        using var response = await otherClient.PostAsync($"/api/company/campaigns/{campaignId}/assets", form);

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.False(await context.StoredFiles.AnyAsync(file => file.OwnerId == campaignId));
    }
}
