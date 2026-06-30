using System.Reflection;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CompanyCampaignRevisionContractTests
{
    [Fact]
    public void WorkflowInterface_ExposesUpdateSubmitAndOutcomeUseCases()
    {
        var assembly = typeof(ICampaignWorkflowService).Assembly;
        var updateType = assembly.GetType("MediBridge.Services.DTOs.Campaigns.UpdateCampaignRequestDto");
        var submissionType = assembly.GetType("MediBridge.Services.DTOs.Campaigns.CampaignSubmissionDto");
        var outcomeType = assembly.GetType("MediBridge.Services.DTOs.Campaigns.CompanyReviewOutcomeDto");

        Assert.NotNull(updateType);
        Assert.NotNull(submissionType);
        Assert.NotNull(outcomeType);

        Assert.Contains(typeof(ICampaignWorkflowService).GetMethods(), method =>
            method.Name == "UpdateCampaignAsync"
            && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                [typeof(string), typeof(string), updateType!, typeof(CancellationToken)]));
        Assert.Contains(typeof(ICampaignWorkflowService).GetMethods(), method =>
            method.Name == "SubmitCampaignAsync"
            && method.ReturnType == typeof(Task<>).MakeGenericType(submissionType!)
            && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                [typeof(string), typeof(string), typeof(string), typeof(CancellationToken)]));
        Assert.Contains(typeof(ICampaignWorkflowService).GetMethods(), method =>
            method.Name == "GetReviewOutcomeAsync"
            && method.ReturnType == typeof(Task<>).MakeGenericType(outcomeType!));
    }

    [Fact]
    public void SubmissionAndOutcomeContracts_UseCanonicalPublicJsonNames()
    {
        var assembly = typeof(ICampaignWorkflowService).Assembly;
        var submissionType = assembly.GetType("MediBridge.Services.DTOs.Campaigns.CampaignSubmissionDto")!;
        var outcomeType = assembly.GetType("MediBridge.Services.DTOs.Campaigns.CompanyReviewOutcomeDto")!;

        var submission = Activator.CreateInstance(
            submissionType,
            "campaign-1",
            "PendingReview",
            2,
            75m,
            "EGP",
            new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));
        var outcome = Activator.CreateInstance(
            outcomeType,
            "campaign-1",
            "RevisionRequired",
            "Please revise.",
            new DateTime(2026, 6, 30, 13, 0, 0, DateTimeKind.Utc),
            true,
            true,
            0);

        var submissionJson = JsonSerializer.Serialize(submission, submissionType);
        var outcomeJson = JsonSerializer.Serialize(outcome, outcomeType);
        Assert.Contains("\"estimatedCost\":75", submissionJson);
        Assert.Contains("\"submittedAtUtc\"", submissionJson);
        Assert.DoesNotContain("reservedAmount", submissionJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"status\":\"RevisionRequired\"", outcomeJson);
        Assert.Contains("\"publicReason\":\"Please revise.\"", outcomeJson);
        Assert.DoesNotContain("notes", outcomeJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CampaignMapper_UsesAuthoritativeSubmissionTimestamp()
    {
        var submittedAtUtc = new DateTime(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
        var campaign = new Campaign
        {
            Id = "campaign-1",
            CompanyId = "company-1",
            Title = "Title",
            Description = "Description",
            Status = CampaignStatus.PendingReview,
            CreatedAtUtc = submittedAtUtc.AddDays(-1),
            SubmittedAtUtc = submittedAtUtc
        };

        var summary = CampaignDtoMapper.ToSummary(campaign, 1);
        var detail = CampaignDtoMapper.ToDetail(campaign, [], []);

        Assert.Equal(submittedAtUtc, summary.SubmittedAtUtc);
        Assert.Equal(submittedAtUtc, detail.SubmittedAtUtc);
    }
}
