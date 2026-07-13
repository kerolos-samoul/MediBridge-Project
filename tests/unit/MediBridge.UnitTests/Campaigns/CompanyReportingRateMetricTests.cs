using MediBridge.Services.DTOs.Campaigns;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CompanyReportingRateMetricTests
{
    [Fact]
    public void CreateRateMetric_ReturnsZeroWithDenominatorMetadataWhenNoEligibleRecordsExist()
    {
        var metric = CampaignReportingDtoMapper.CreateRateMetric(0, 0);

        Assert.Equal(0m, metric.Value);
        Assert.Equal(0, metric.Numerator);
        Assert.Equal(0, metric.Denominator);
        Assert.False(metric.HasEligibleRecords);
    }

    [Fact]
    public void CreateRateMetric_CalculatesPercentageWhenDenominatorExists()
    {
        var metric = CampaignReportingDtoMapper.CreateRateMetric(1, 3);

        Assert.Equal(33.33m, metric.Value);
        Assert.Equal(1, metric.Numerator);
        Assert.Equal(3, metric.Denominator);
        Assert.True(metric.HasEligibleRecords);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void CreateRateMetric_RejectsNegativeInputs(int numerator, int denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CampaignReportingDtoMapper.CreateRateMetric(numerator, denominator));
    }
}
