using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.DTOs.Wallets;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class WithdrawalPrivacyContractTests
{
    private static readonly string[] ProhibitedSchemaTerms =
    [
        "destination",
        "bank",
        "iban",
        "card",
        "mobileWallet",
        "savedPayout",
        "providerPayload",
        "credential",
        "idempotency",
        "storageKey",
        "stackTrace",
        "walletId",
        "availableBalance",
        "reservedBalance",
        "concurrencyToken"
    ];

    [Theory]
    [InlineData(typeof(CreateWithdrawalRequestDto))]
    [InlineData(typeof(DoctorWithdrawalDto))]
    [InlineData(typeof(DoctorWithdrawalPageDto))]
    [InlineData(typeof(AdminWithdrawalDto))]
    [InlineData(typeof(AdminWithdrawalPageDto))]
    [InlineData(typeof(AdminWithdrawalDecisionRequestDto))]
    [InlineData(typeof(MarkWithdrawalPaidRequestDto))]
    [InlineData(typeof(MarkWithdrawalFailedRequestDto))]
    public void WithdrawalDtosContainNoPayoutDestinationOrUnsafeOperationalFields(Type dtoType)
    {
        var propertyNames = dtoType.GetProperties().Select(property => property.Name).ToArray();
        foreach (var prohibited in ProhibitedSchemaTerms)
        {
            Assert.DoesNotContain(propertyNames, property => property.Contains(prohibited, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task WithdrawalResponsesContainPayoutReferenceOnlyNotDestinationData()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Paid, payoutReference: "ops-reference-safe");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Admin", seed.AdminUserId));

        using var response = await client.GetAsync(AdminToolsRoutes.AdminWithdrawals);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(json);
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, 200);
        Assert.Contains("ops-reference-safe", json);
        foreach (var prohibited in ProhibitedSchemaTerms)
        {
            Assert.DoesNotContain(prohibited, json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
