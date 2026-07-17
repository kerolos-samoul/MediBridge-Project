using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class WithdrawalPrivacyIntegrationTests : IClassFixture<WebAppFactory>
{
    private static readonly string[] ForbiddenFragments =
    [
        "payoutDestination",
        "bankAccount",
        "iban",
        "cardNumber",
        "mobileWallet",
        "savedPayoutMethod",
        "providerPayload",
        "providerCredential",
        "idempotencyKey",
        "storageKey",
        "stackTrace",
        "walletId",
        "availableBalance",
        "reservedBalance",
        "concurrencyToken"
    ];

    private readonly WebAppFactory factory;

    public WithdrawalPrivacyIntegrationTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task DoctorAndAdminWithdrawalListsExposeNoPayoutDestinationOrWalletInternals()
    {
        await factory.InitializeDatabaseAsync();
        var seed = await WithdrawalIntegrationTestHelpers.SeedWithdrawalAsync(
            factory.Services,
            WithdrawalRequestStatus.Paid,
            reservedBalance: 0m,
            payoutReference: "ops-ref-visible");

        using var doctorClient = factory.CreateClient();
        doctorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", seed.DoctorUserId));
        using var doctorResponse = await doctorClient.GetAsync("/api/doctor/withdrawals?PageNumber=1&PageSize=20");
        var doctorJson = await doctorResponse.Content.ReadAsStringAsync();

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", seed.AdminUserId));
        using var adminResponse = await adminClient.GetAsync("/api/admin/withdrawals?PageNumber=1&PageSize=20");
        var adminJson = await adminResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, doctorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        Assert.Contains("ops-ref-visible", doctorJson);
        Assert.Contains("ops-ref-visible", adminJson);
        AssertNoForbiddenFragments(doctorJson);
        AssertNoForbiddenFragments(adminJson);
    }

    private static void AssertNoForbiddenFragments(string json)
    {
        foreach (var fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(fragment, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
