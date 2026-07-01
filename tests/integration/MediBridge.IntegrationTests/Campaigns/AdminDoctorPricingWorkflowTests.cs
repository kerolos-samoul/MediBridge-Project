using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminDoctorPricingWorkflowTests
{
    [Fact]
    public async Task AdminDoctorPricing_PositivePriceCreatesHistoryAndEnablesTargeting()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, ids.AdminUserId);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);

        using var priceResponse = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{ids.DoctorProfileId}/price",
            new { PricePerMessage = 64.25m, Reason = "Initial campaign rate" });

        Assert.Equal(HttpStatusCode.OK, priceResponse.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var doctor = await context.DoctorProfiles.AsNoTracking().SingleAsync(profile => profile.Id == ids.DoctorProfileId);
        var history = await context.DoctorPriceHistories.AsNoTracking().SingleAsync(item => item.DoctorId == ids.DoctorProfileId);
        Assert.Equal(64.25m, doctor.PricePerMessage);
        Assert.Null(history.PreviousPricePerMessage);
        Assert.Equal(64.25m, history.NewPricePerMessage);
        Assert.Equal(ids.AdminUserId, history.ChangedByAdminUserId);
        Assert.Equal("Initial campaign rate", history.Reason);

        using var draftResponse = await company.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Priced targets", Description = "Doctor price enables targeting" });
        var campaignId = await ReadDataStringAsync(draftResponse, "campaignId");
        using var previewResponse = await company.GetAsync($"/api/company/campaigns/{campaignId}/target-preview");

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var data = preview.RootElement.GetProperty("Data");
        Assert.Equal(1, data.GetProperty("eligibleDoctorCount").GetInt32());
        Assert.Equal(64.25m, data.GetProperty("estimatedTotalCost").GetDecimal());
    }

    private static async Task<string> ReadDataStringAsync(HttpResponseMessage response, string propertyName)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("Data").GetProperty(propertyName).GetString()!;
    }
}
