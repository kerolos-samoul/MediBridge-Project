using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class CampaignPreviewAndQueueSummaryIntegrationTests
{
    [Fact]
    public async Task TargetPreview_UsesCurrentEligibleDoctorCountAndPriceTotal()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 40.25m);
        await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 60m);
        await Phase5CampaignQueueTestHelpers.SeedZeroPriceDoctorAsync(factory.Services);
        await Phase5CampaignQueueTestHelpers.SeedSuspendedDoctorAsync(factory.Services);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(
            factory.Services,
            company.CompanyId,
            CampaignStatus.Draft);
        using var client = CreateCompanyClient(factory, company.UserId);

        using var response = await client.GetAsync($"/api/company/campaigns/{campaignId}/target-preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(2, data.GetProperty("eligibleDoctorCount").GetInt32());
        Assert.Equal(100.25m, data.GetProperty("estimatedTotalCost").GetDecimal());
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task QueueSummary_ReturnsOwnedAggregateCountsWithoutDoctorDetails()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctors = new[]
        {
            await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services),
            await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services),
            await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services)
        };
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(
            factory.Services,
            company.CompanyId,
            CampaignStatus.Approved);
        await SeedQueueItemsAsync(
            factory,
            campaignId,
            (doctors[0].DoctorId, QueueItemStatus.Queued),
            (doctors[1].DoctorId, QueueItemStatus.Activated),
            (doctors[2].DoctorId, QueueItemStatus.Cancelled));
        using var client = CreateCompanyClient(factory, company.UserId);

        using var response = await client.GetAsync($"/api/company/campaigns/{campaignId}/queue-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("doctorId", responseJson, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(responseJson);
        var data = document.RootElement.GetProperty("Data");
        Assert.Equal(campaignId, data.GetProperty("campaignId").GetString());
        Assert.Equal(1, data.GetProperty("queuedCount").GetInt32());
        Assert.Equal(1, data.GetProperty("activatedCount").GetInt32());
        Assert.Equal(1, data.GetProperty("cancelledCount").GetInt32());
        Assert.Equal(0, data.GetProperty("expiredCount").GetInt32());
        Assert.True(data.TryGetProperty("generatedAtUtc", out _));
    }

    [Fact]
    public async Task CampaignPreviewAndQueueSummary_HideOtherCompanyCampaigns()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var owner = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var other = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var campaignId = await Phase5CampaignQueueTestHelpers.SeedCampaignAsync(factory.Services, owner.CompanyId);
        using var client = CreateCompanyClient(factory, other.UserId);

        using var preview = await client.GetAsync($"/api/company/campaigns/{campaignId}/target-preview");
        using var summary = await client.GetAsync($"/api/company/campaigns/{campaignId}/queue-summary");

        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, summary.StatusCode);
    }

    private static HttpClient CreateCompanyClient(WebAppFactory factory, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", userId));
        return client;
    }

    private static async Task SeedQueueItemsAsync(
        WebAppFactory factory,
        string campaignId,
        params (string DoctorId, QueueItemStatus Status)[] items)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var now = DateTime.UtcNow;
        await context.DoctorMessageQueues.AddRangeAsync(items.Select(item => new DoctorMessageQueue
        {
            Id = Guid.NewGuid().ToString("N"),
            CampaignId = campaignId,
            DoctorId = item.DoctorId,
            QueuedAtUtc = now,
            CampaignSubmittedAtUtc = now.AddMinutes(-1),
            Status = item.Status,
            CreatedAtUtc = now
        }));
        await context.SaveChangesAsync();
    }
}
