using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class DoctorWithdrawalContractTests
{
    [Fact]
    public async Task CreateWithdrawal_ReturnsCreatedEnvelopeAndSchema()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var doctor = await WithdrawalContractTestData.SeedDoctorAsync(factory.Services);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor", doctor.UserId));

        using var response = await client.PostAsync(AdminToolsRoutes.DoctorWithdrawals, Phase5ContractTestHelpers.CreateJsonContent(new { amount = 75m }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 201);
        Assert.Equal("Requested", data.GetProperty("status").GetString());
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
        Assert.Equal(75m, data.GetProperty("amount").GetDecimal());
        Assert.True(data.TryGetProperty("withdrawalId", out _));
        Assert.True(data.TryGetProperty("requestedAtUtc", out _));
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(10.123, HttpStatusCode.BadRequest)]
    [InlineData(999, HttpStatusCode.Conflict)]
    public async Task CreateWithdrawal_ReturnsStandardValidationAndConflictEnvelopes(decimal amount, HttpStatusCode expectedStatus)
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var doctor = await WithdrawalContractTestData.SeedDoctorAsync(factory.Services, walletAvailable: 25m);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor", doctor.UserId));

        using var response = await client.PostAsync(AdminToolsRoutes.DoctorWithdrawals, Phase5ContractTestHelpers.CreateJsonContent(new { amount }));

        Assert.Equal(expectedStatus, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Phase5ContractTestHelpers.AssertEnvelope(document.RootElement, (int)expectedStatus);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
    }

    [Fact]
    public async Task CreateWithdrawal_RequiresDoctorAuthorization()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.PostAsync(AdminToolsRoutes.DoctorWithdrawals, Phase5ContractTestHelpers.CreateJsonContent(new { amount = 10m }));

        using var company = factory.CreateClient();
        company.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Company"));
        using var forbidden = await company.PostAsync(AdminToolsRoutes.DoctorWithdrawals, Phase5ContractTestHelpers.CreateJsonContent(new { amount = 10m }));

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var unauthorizedDocument = JsonDocument.Parse(await unauthorized.Content.ReadAsStringAsync());
        using var forbiddenDocument = JsonDocument.Parse(await forbidden.Content.ReadAsStringAsync());
        Phase5ContractTestHelpers.AssertEnvelope(unauthorizedDocument.RootElement, 401);
        Phase5ContractTestHelpers.AssertEnvelope(forbiddenDocument.RootElement, 403);
    }

    [Fact]
    public async Task ListDoctorWithdrawals_ReturnsDoctorOwnedPagedAndStatusFilteredItems()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var doctor = await WithdrawalContractTestData.SeedDoctorAsync(factory.Services);
        var otherDoctor = await WithdrawalContractTestData.SeedDoctorAsync(factory.Services);
        var adminUserId = await WithdrawalContractTestData.SeedAdminAsync(factory.Services);
        var requested = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested, adminUserId, doctor.UserId, doctor.DoctorId);
        await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Paid, adminUserId, doctor.UserId, doctor.DoctorId);
        await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested, adminUserId, otherDoctor.UserId, otherDoctor.DoctorId);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor", doctor.UserId));

        using var response = await client.GetAsync($"{AdminToolsRoutes.DoctorWithdrawals}?status=Requested&PageNumber=1&PageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
        Assert.Equal(1, data.GetProperty("page").GetProperty("totalCount").GetInt32());
        var item = data.GetProperty("items")[0];
        Assert.Equal(requested.WithdrawalId, item.GetProperty("withdrawalId").GetString());
        Assert.False(item.TryGetProperty("doctorId", out _));
    }
}
