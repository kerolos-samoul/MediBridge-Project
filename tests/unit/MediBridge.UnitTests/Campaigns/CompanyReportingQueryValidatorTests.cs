using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Campaigns;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CompanyReportingQueryValidatorTests
{
    [Fact]
    public void Pagination_DefaultsToPageOneAndPageSizeTwenty()
    {
        var pagination = new CompanyReportingPaginationValidator().Resolve(null, null);

        Assert.Equal(1, pagination.PageNumber);
        Assert.Equal(20, pagination.PageSize);
        Assert.Equal(0, pagination.Skip);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Pagination_RejectsOutOfBoundsValues(int pageNumber, int pageSize)
    {
        Assert.Throws<Phase5ValidationException>(() => new CompanyReportingPaginationValidator().Resolve(pageNumber, pageSize));
    }

    [Fact]
    public void DeliveryFilters_NormalizeSupportedValues()
    {
        var filters = new CompanyReportingFilterValidator().ResolveDeliveryFilters(
            "accepted",
            "Read",
            " Cardiology ",
            " Cairo ");

        Assert.Equal(DeliveryStatus.Accepted, filters.Status);
        Assert.Equal(CompanyReportingDeliveryState.Read, filters.State);
        Assert.Equal("Cardiology", filters.DoctorSpecialization);
        Assert.Equal("Cairo", filters.DoctorLocation);
    }

    [Theory]
    [InlineData("Completed", null)]
    [InlineData("Accepted", "Opened")]
    public void DeliveryFilters_RejectInvalidValues(string? status, string? state)
    {
        Assert.Throws<Phase5ValidationException>(() => new CompanyReportingFilterValidator().ResolveDeliveryFilters(status, state, null, null));
    }

    [Fact]
    public void FeedbackFilters_NormalizeSupportedValues()
    {
        var filters = new CompanyReportingFilterValidator().ResolveFeedbackFilters(
            "Rejected",
            "eligible",
            " Oncology ",
            " Giza ");

        Assert.Equal(CompanyReportingFeedbackOutcome.Rejected, filters.Outcome);
        Assert.Equal(CompanyReportingFeedbackEligibility.Eligible, filters.FeedbackEligibility);
        Assert.Equal("Oncology", filters.DoctorSpecialization);
        Assert.Equal("Giza", filters.DoctorLocation);
    }

    [Theory]
    [InlineData("Active", null)]
    [InlineData("Accepted", "Maybe")]
    public void FeedbackFilters_RejectInvalidValues(string? outcome, string? eligibility)
    {
        Assert.Throws<Phase5ValidationException>(() => new CompanyReportingFilterValidator().ResolveFeedbackFilters(outcome, eligibility, null, null));
    }

    [Fact]
    public void Filters_RejectDoctorFiltersLongerThanOpenApiLimit()
    {
        var tooLong = new string('a', 121);

        Assert.Throws<Phase5ValidationException>(() => new CompanyReportingFilterValidator().ResolveDeliveryFilters(null, null, tooLong, null));
        Assert.Throws<Phase5ValidationException>(() => new CompanyReportingFilterValidator().ResolveFeedbackFilters(null, null, null, tooLong));
    }
}
