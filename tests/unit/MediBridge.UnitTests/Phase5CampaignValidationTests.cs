using FluentValidation;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Validators.Campaigns;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase5CampaignValidationTests
{
    private readonly IValidator<CreateCampaignRequestDto> validator = new CreateCampaignRequestValidator();

    [Theory]
    [InlineData("", "description", "research")]
    [InlineData("title", "", "research")]
    public async Task Validator_RejectsMissingRequiredCampaignContent(string title, string description, string research)
    {
        var result = await validator.ValidateAsync(new CreateCampaignRequestDto
        {
            Title = title,
            Description = description,
            ClinicalResearchInfo = research,
            AssetIds = ["asset-1"],
            TargetDoctorIds = ["doctor-1"]
        });

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Validator_AllowsOptionalClinicalResearch(string? research)
    {
        var result = await validator.ValidateAsync(new CreateCampaignRequestDto
        {
            Title = "Campaign",
            Description = "Description",
            ClinicalResearchInfo = research,
            AssetIds = ["asset-1"],
            TargetDoctorIds = ["doctor-1"]
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Validator_RejectsInvalidTargetCounts(int targetCount)
    {
        var result = await validator.ValidateAsync(new CreateCampaignRequestDto
        {
            Title = "Campaign",
            Description = "Description",
            ClinicalResearchInfo = "Research",
            AssetIds = ["asset-1"],
            TargetDoctorIds = Enumerable.Range(0, targetCount).Select(index => $"doctor-{index}").ToArray()
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validator_RejectsDuplicateTargetsAndMissingCampaignAssets()
    {
        var result = await validator.ValidateAsync(new CreateCampaignRequestDto
        {
            Title = "Campaign",
            Description = "Description",
            ClinicalResearchInfo = "Research",
            AssetIds = [],
            TargetDoctorIds = ["doctor-1", "doctor-1"]
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("asset", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TargetPriceTotal_UsesAcceptedTargetSnapshotPricesOnly()
    {
        var targets = new[]
        {
            new CampaignTargetSnapshotDto { DoctorId = "doctor-1", PricePerMessage = 125.25m },
            new CampaignTargetSnapshotDto { DoctorId = "doctor-2", PricePerMessage = 74.75m }
        };

        Assert.Equal(200.00m, CampaignDtoMapper.CalculateTargetPriceTotal(targets));
    }
}
