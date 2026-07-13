using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Services.DTOs.Campaigns;
using Xunit;

namespace MediBridge.UnitTests.Campaigns;

public sealed class CompanyReportingPrivacyMapperTests
{
    private static readonly string[] ForbiddenPropertyFragments =
    [
        "Name",
        "Email",
        "Phone",
        "Contact",
        "Wallet",
        "Balance",
        "Verification",
        "Account",
        "Storage",
        "Idempotency"
    ];

    [Fact]
    public void PublicDoctorSummaryMapping_ExposesOnlyAllowedDoctorFields()
    {
        var model = new PublicDoctorSummaryReadModel(
            "doctor-public-1",
            "Cardiology",
            "6-10 years",
            "Cairo");

        var dto = CampaignReportingDtoMapper.ToPublicDoctorSummary(model);

        Assert.Equal("doctor-public-1", dto.PublicDoctorId);
        Assert.Equal("Cardiology", dto.Specialization);
        Assert.Equal("6-10 years", dto.ExperienceBand);
        Assert.Equal("Cairo", dto.Location);
        AssertForbiddenPropertiesAbsent(typeof(PublicDoctorSummaryDto));
    }

    [Fact]
    public void DeliveryAndFeedbackRows_DoNotDeclarePrivateDoctorOrWalletFields()
    {
        AssertForbiddenPropertiesAbsent(typeof(CampaignDeliveryReportRowDto));
        AssertForbiddenPropertiesAbsent(typeof(CampaignFeedbackReportRowDto));

        var doctor = new PublicDoctorSummaryReadModel("public-id", "Oncology", "0-5 years", "Giza");
        var delivery = CampaignReportingDtoMapper.ToDeliveryReportRow(new CampaignDeliveryReportRowReadModel(
            "delivery-1",
            "campaign-1",
            doctor,
            new DateOnly(2026, 7, 13),
            DateTime.SpecifyKind(new DateTime(2026, 7, 13, 9, 0, 0), DateTimeKind.Utc),
            null,
            DeliveryStatus.Active,
            null,
            100m,
            100m,
            0m,
            0m,
            0m));
        var feedback = CampaignReportingDtoMapper.ToFeedbackReportRow(new CampaignFeedbackReportRowReadModel(
            "delivery-1",
            "campaign-1",
            CompanyReportingFeedbackOutcome.Accepted,
            "Useful",
            DateTime.SpecifyKind(new DateTime(2026, 7, 13, 10, 0, 0), DateTimeKind.Utc),
            false,
            doctor));

        Assert.Equal("public-id", delivery.PublicDoctorId);
        Assert.Equal("public-id", feedback.PublicDoctorId);
    }

    private static void AssertForbiddenPropertiesAbsent(Type type)
    {
        var propertyNames = type.GetProperties().Select(property => property.Name).ToArray();
        foreach (var forbidden in ForbiddenPropertyFragments)
        {
            Assert.DoesNotContain(propertyNames, property => property.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}
