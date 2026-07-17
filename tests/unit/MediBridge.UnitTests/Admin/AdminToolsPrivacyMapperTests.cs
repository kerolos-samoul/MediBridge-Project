using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Admin;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.DTOs.Wallets;
using Xunit;

namespace MediBridge.UnitTests.Admin;

public sealed class AdminToolsPrivacyMapperTests
{
    private static readonly string[] ForbiddenPropertyFragments =
    [
        "Destination",
        "Bank",
        "Card",
        "MobileWallet",
        "SavedPayout",
        "Idempotency",
        "StorageKey",
        "Credential",
        "StackTrace",
        "PrivateContact",
        "ProviderPayload"
    ];

    [Theory]
    [InlineData(typeof(AdminWorkQueueItemDto))]
    [InlineData(typeof(AdminWithdrawalDto))]
    [InlineData(typeof(AdminWithdrawalPageDto))]
    [InlineData(typeof(DoctorWithdrawalDto))]
    [InlineData(typeof(DoctorWithdrawalPageDto))]
    [InlineData(typeof(DoctorPricingDto))]
    [InlineData(typeof(AdminStatisticsDto))]
    public void Phase11Dtos_DoNotDeclareProhibitedFields(Type dtoType)
    {
        var propertyNames = dtoType.GetProperties().Select(property => property.Name).ToArray();

        foreach (var forbidden in ForbiddenPropertyFragments)
        {
            Assert.DoesNotContain(propertyNames, property => property.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void WithdrawalMapper_ExposesSafePayoutReferenceOnly()
    {
        var model = new AdminWithdrawalReadModel(
            "withdrawal-1",
            "doctor-1",
            "public-doctor-1",
            125.50m,
            "EGP",
            WithdrawalRequestStatus.Paid,
            DateTime.SpecifyKind(new DateTime(2026, 7, 13), DateTimeKind.Utc),
            DateTime.SpecifyKind(new DateTime(2026, 7, 14), DateTimeKind.Utc),
            "Approved",
            "OPS-REF-1",
            DateTime.SpecifyKind(new DateTime(2026, 7, 15), DateTimeKind.Utc));

        var dto = AdminToolsDtoMapper.ToAdminWithdrawal(model);

        Assert.Equal("OPS-REF-1", dto.PayoutReference);
        Assert.Equal("public-doctor-1", dto.DoctorPublicId);
    }
}
