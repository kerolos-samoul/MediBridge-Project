using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.ContractTests.TestHost;
using MediBridge.Core.Enums;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class AdminWithdrawalContractTests
{
    [Fact]
    public async Task AdminWithdrawalList_SupportsFiltersSchemaAndFullPaginationTraversal()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await WithdrawalContractTestData.SeedAdminAsync(factory.Services);
        var doctor = await WithdrawalContractTestData.SeedDoctorAsync(factory.Services);
        var first = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested, adminUserId, doctor.UserId, doctor.DoctorId, requestedAtUtc: DateTime.UtcNow.AddMinutes(-5));
        var second = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested, adminUserId, doctor.UserId, doctor.DoctorId, requestedAtUtc: DateTime.UtcNow.AddMinutes(-4));
        var third = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested, adminUserId, doctor.UserId, doctor.DoctorId, requestedAtUtc: DateTime.UtcNow.AddMinutes(-3));
        await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Approved, adminUserId, doctor.UserId, doctor.DoctorId);
        using var client = CreateAdminClient(factory, adminUserId);

        var ids = new List<string>();
        for (var pageNumber = 1; pageNumber <= 2; pageNumber++)
        {
            using var response = await client.GetAsync($"{AdminToolsRoutes.AdminWithdrawals}?status=Requested&doctorId={doctor.DoctorId}&PageNumber={pageNumber}&PageSize=2");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = Phase5ContractTestHelpers.AssertDataEnvelope(document, 200);
            Assert.Equal(3, data.GetProperty("page").GetProperty("totalCount").GetInt32());
            foreach (var item in data.GetProperty("items").EnumerateArray())
            {
                Assert.Equal("Requested", item.GetProperty("status").GetString());
                Assert.Equal(doctor.DoctorId, item.GetProperty("doctorId").GetString());
                ids.Add(item.GetProperty("withdrawalId").GetString()!);
            }
        }

        Assert.Equal(3, ids.Count);
        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            new[] { first.WithdrawalId, second.WithdrawalId, third.WithdrawalId }.OrderBy(id => id, StringComparer.Ordinal),
            ids.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AdminWithdrawalList_RequiresAdminAuthorizationAndValidationEnvelope()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await WithdrawalContractTestData.SeedAdminAsync(factory.Services);
        using var admin = CreateAdminClient(factory, adminUserId);
        using var invalid = await admin.GetAsync($"{AdminToolsRoutes.AdminWithdrawals}?PageNumber=0");
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.GetAsync(AdminToolsRoutes.AdminWithdrawals);
        using var doctor = factory.CreateClient();
        doctor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor"));
        using var forbidden = await doctor.GetAsync(AdminToolsRoutes.AdminWithdrawals);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var invalidDocument = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        using var unauthorizedDocument = JsonDocument.Parse(await unauthorized.Content.ReadAsStringAsync());
        using var forbiddenDocument = JsonDocument.Parse(await forbidden.Content.ReadAsStringAsync());
        Phase5ContractTestHelpers.AssertEnvelope(invalidDocument.RootElement, 400);
        Phase5ContractTestHelpers.AssertEnvelope(unauthorizedDocument.RootElement, 401);
        Phase5ContractTestHelpers.AssertEnvelope(forbiddenDocument.RootElement, 403);
    }

    [Fact]
    public async Task AdminWithdrawalTransitions_ReturnSuccessSchemaAndStandardErrorEnvelopes()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var adminUserId = await WithdrawalContractTestData.SeedAdminAsync(factory.Services);
        var approveSeed = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested, adminUserId);
        var rejectSeed = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested, adminUserId);
        var paidSeed = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Approved, adminUserId);
        var failedSeed = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Approved, adminUserId);
        using var client = CreateAdminClient(factory, adminUserId);

        using var approve = await client.PutAsync(AdminToolsRoutes.ApproveWithdrawal(approveSeed.WithdrawalId), Phase5ContractTestHelpers.CreateJsonContent(new { note = "ok" }));
        using var reject = await client.PutAsync(AdminToolsRoutes.RejectWithdrawal(rejectSeed.WithdrawalId), Phase5ContractTestHelpers.CreateJsonContent(new { reason = "no" }));
        using var paid = await client.PutAsync(AdminToolsRoutes.MarkWithdrawalPaid(paidSeed.WithdrawalId), Phase5ContractTestHelpers.CreateJsonContent(new { payoutReference = "ops-ref" }));
        using var failed = await client.PutAsync(AdminToolsRoutes.MarkWithdrawalFailed(failedSeed.WithdrawalId), Phase5ContractTestHelpers.CreateJsonContent(new { reason = "bank rejected" }));
        using var badRequest = await client.PutAsync(AdminToolsRoutes.MarkWithdrawalPaid(failedSeed.WithdrawalId), Phase5ContractTestHelpers.CreateJsonContent(new { payoutReference = "" }));
        using var notFound = await client.PutAsync(AdminToolsRoutes.ApproveWithdrawal("missing-withdrawal"), Phase5ContractTestHelpers.CreateJsonContent(new { note = "missing" }));
        using var conflict = await client.PutAsync(AdminToolsRoutes.ApproveWithdrawal(approveSeed.WithdrawalId), Phase5ContractTestHelpers.CreateJsonContent(new { note = "again" }));

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRequest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var approveDocument = JsonDocument.Parse(await approve.Content.ReadAsStringAsync());
        Assert.Equal("Approved", Phase5ContractTestHelpers.AssertDataEnvelope(approveDocument, 200).GetProperty("status").GetString());
        using var badRequestDocument = JsonDocument.Parse(await badRequest.Content.ReadAsStringAsync());
        using var notFoundDocument = JsonDocument.Parse(await notFound.Content.ReadAsStringAsync());
        using var conflictDocument = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Phase5ContractTestHelpers.AssertEnvelope(badRequestDocument.RootElement, 400);
        Phase5ContractTestHelpers.AssertEnvelope(notFoundDocument.RootElement, 404);
        Phase5ContractTestHelpers.AssertEnvelope(conflictDocument.RootElement, 409);
    }

    [Fact]
    public async Task AdminWithdrawalTransitions_RequireAdminAuthorization()
    {
        await using var factory = new ContractWebAppFactory();
        await factory.InitializeDatabaseAsync();
        var seed = await WithdrawalContractTestData.SeedWithdrawalAsync(factory.Services, WithdrawalRequestStatus.Requested);
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.PutAsync(AdminToolsRoutes.ApproveWithdrawal(seed.WithdrawalId), Phase5ContractTestHelpers.CreateJsonContent(new { note = "ok" }));
        using var doctor = factory.CreateClient();
        doctor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Doctor", seed.DoctorUserId));
        using var forbidden = await doctor.PutAsync(AdminToolsRoutes.ApproveWithdrawal(seed.WithdrawalId), Phase5ContractTestHelpers.CreateJsonContent(new { note = "ok" }));

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var unauthorizedDocument = JsonDocument.Parse(await unauthorized.Content.ReadAsStringAsync());
        using var forbiddenDocument = JsonDocument.Parse(await forbidden.Content.ReadAsStringAsync());
        Phase5ContractTestHelpers.AssertEnvelope(unauthorizedDocument.RootElement, 401);
        Phase5ContractTestHelpers.AssertEnvelope(forbiddenDocument.RootElement, 403);
    }

    private static HttpClient CreateAdminClient(ContractWebAppFactory factory, string adminUserId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Phase5ContractTestHelpers.CreateToken("Admin", adminUserId));
        return client;
    }
}
