using MediBridge.Services.Interfaces;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class AdminCampaignReviewReadContractTests
{
    [Fact]
    public void ServicesAssembly_ExposesAdminReviewReadContracts()
    {
        var assembly = typeof(ICampaignWorkflowService).Assembly;
        var service = assembly.GetType("MediBridge.Services.Interfaces.IAdminCampaignReviewService");

        Assert.NotNull(service);
        Assert.NotNull(service!.GetMethod("ListPendingCampaignsAsync"));
        Assert.NotNull(service.GetMethod("GetReviewDetailAsync"));
        Assert.NotNull(service.GetMethod("ReviewCampaignAsync"));
        Assert.NotNull(service.GetMethod("GetQueueRowsAsync"));

        foreach (var typeName in new[]
        {
            "PendingCampaignSummaryDto",
            "PendingCampaignPageDto",
            "CampaignReviewDetailDto",
            "ReviewFileDto",
            "ReviewDecisionRequestDto",
            "CampaignReviewResultDto",
            "QueueRowDto"
        })
        {
            Assert.NotNull(assembly.GetType($"MediBridge.Services.DTOs.Campaigns.{typeName}"));
        }

        Assert.NotNull(assembly.GetType(
            "MediBridge.Services.Validators.Campaigns.ReviewDecisionRequestDtoValidator"));
    }
}
