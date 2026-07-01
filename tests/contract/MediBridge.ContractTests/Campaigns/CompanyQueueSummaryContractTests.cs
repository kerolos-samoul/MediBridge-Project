using System.Net;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.ContractTests.Campaigns;

public sealed class CompanyQueueSummaryContractTests : WalletCampaignContractTestBase
{
    [Fact]
    public async Task CompanyQueueSummary_HidesDoctorLevelDetails()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await CreateApprovedActorsAsync(factory);
        var secondActors = await CreateApprovedActorsAsync(factory);
        var campaignId = await SeedCampaignQueueAsync(
            factory,
            actors.CompanyProfileId,
            actors.DoctorProfileId,
            secondActors.DoctorProfileId);
        using var company = CreateCompanyClient(factory, actors.CompanyUserId);

        using var response = await company.GetAsync(Route(campaignId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await AssertEnvelopeAsync(response, 200, "Success");
        var data = envelope.GetProperty("Data");
        Assert.Equal(campaignId, data.GetProperty("campaignId").GetString());
        Assert.Equal(1, data.GetProperty("queuedCount").GetInt32());
        Assert.Equal(1, data.GetProperty("activatedCount").GetInt32());
        Assert.Equal(1, data.GetProperty("cancelledCount").GetInt32());
        Assert.Equal(0, data.GetProperty("expiredCount").GetInt32());
        Assert.DoesNotContain("doctorId", data.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SeedCampaignQueueAsync(
        ContractWebAppFactory factory,
        string companyId,
        string firstDoctorId,
        string secondDoctorId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var campaign = new Campaign
        {
            CompanyId = companyId,
            Title = "Queue summary contract",
            Description = "Aggregate queue visibility only.",
            Status = CampaignStatus.Approved
        };
        await context.Campaigns.AddAsync(campaign);
        await context.DoctorMessageQueues.AddRangeAsync(
            new DoctorMessageQueue { CampaignId = campaign.Id, DoctorId = firstDoctorId, Status = QueueItemStatus.Queued },
            new DoctorMessageQueue { CampaignId = campaign.Id, DoctorId = secondDoctorId, Status = QueueItemStatus.Activated },
            new DoctorMessageQueue { CampaignId = campaign.Id, DoctorId = firstDoctorId, Status = QueueItemStatus.Cancelled });
        await context.SaveChangesAsync();
        return campaign.Id;
    }

    private static string Route(string campaignId)
        => WalletCampaignWorkflowRoutes.CompanyCampaignQueueSummary.Replace("{campaignId}", campaignId, StringComparison.Ordinal);
}
