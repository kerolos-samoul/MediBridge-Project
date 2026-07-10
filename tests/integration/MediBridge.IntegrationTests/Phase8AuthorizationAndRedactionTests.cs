using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8AuthorizationAndRedactionTests
{
    private const string RawIdempotencyKey = "secret-idempotency-key-0001";
    private const string FullFeedbackText = "full feedback text should never appear in envelopes or audit metadata";

    [Fact]
    public async Task ReadAndInteract_DenyUnauthorizedRolesAndInactiveDoctors_WithSafeEnvelopes()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "authorization-redaction");
        var company = await Phase8InteractionTestHelpers.SeedApprovedCompanyAsync(
            factory.Services,
            $"denied-company-user-{seed.Suffix}",
            $"denied-company-{seed.Suffix}",
            $"denied-company-{seed.Suffix}@example.test",
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-10));
        var unapproved = await Phase8InteractionTestHelpers.SeedApprovedActiveDoctorAsync(
            factory.Services,
            $"pending-doctor-user-{seed.Suffix}",
            $"pending-doctor-{seed.Suffix}",
            $"pending-doctor-{seed.Suffix}@example.test",
            50m,
            10,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-10));
        await SetDoctorAccountStatusAsync(factory, unapproved.UserId, AccountStatus.Pending);
        var suspended = await Phase7DeliveryTestHelpers.SeedSuspendedDoctorAsync(
            factory.Services,
            $"suspended-doctor-user-{seed.Suffix}",
            $"suspended-doctor-{seed.Suffix}",
            $"suspended-doctor-{seed.Suffix}@example.test",
            50m,
            10,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-10));
        var deleted = await Phase7DeliveryTestHelpers.SeedDeletedDoctorAsync(
            factory.Services,
            $"deleted-doctor-user-{seed.Suffix}",
            $"deleted-doctor-{seed.Suffix}",
            $"deleted-doctor-{seed.Suffix}@example.test",
            50m,
            10,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-10),
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-1));
        var otherDoctor = await Phase8InteractionTestHelpers.SeedApprovedActiveDoctorAsync(
            factory.Services,
            $"other-doctor-user-{seed.Suffix}",
            $"other-doctor-{seed.Suffix}",
            $"other-doctor-{seed.Suffix}@example.test",
            50m,
            10,
            Phase8InteractionTestHelpers.DefaultUtcNow.AddDays(-10));

        using var unauthenticatedClient = factory.CreateClient();
        using var unauthenticatedRead = await unauthenticatedClient.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedRead.StatusCode);
        using var unauthenticatedInteract = Phase8InteractionTestHelpers.CreateInteractRequest(seed.DeliveryId, RawIdempotencyKey, "Accept", FullFeedbackText);
        using var unauthenticatedInteractResponse = await unauthenticatedClient.SendAsync(unauthenticatedInteract);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedInteractResponse.StatusCode);

        foreach (var denied in new[]
        {
            CreateClient(factory, "Company", company.UserId),
            CreateClient(factory, "Admin", $"admin-{seed.Suffix}"),
            Phase8InteractionTestHelpers.CreateDoctorClient(factory, unapproved.UserId),
            Phase8InteractionTestHelpers.CreateDoctorClient(factory, suspended.UserId),
            Phase8InteractionTestHelpers.CreateDoctorClient(factory, deleted.UserId)
        })
        {
            using (denied)
            {
                using var read = await denied.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
                await AssertEnvelopeDoesNotLeakAsync(read, HttpStatusCode.Forbidden);

                using var interactRequest = Phase8InteractionTestHelpers.CreateInteractRequest(seed.DeliveryId, RawIdempotencyKey, "Accept", FullFeedbackText);
                using var interact = await denied.SendAsync(interactRequest);
                await AssertEnvelopeDoesNotLeakAsync(interact, HttpStatusCode.Forbidden);
            }
        }

        using var otherDoctorClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, otherDoctor.UserId);
        using var otherRead = await otherDoctorClient.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
        await AssertEnvelopeDoesNotLeakAsync(otherRead, HttpStatusCode.NotFound);
        using var otherInteractRequest = Phase8InteractionTestHelpers.CreateInteractRequest(seed.DeliveryId, RawIdempotencyKey, "Reject", FullFeedbackText);
        using var otherInteract = await otherDoctorClient.SendAsync(otherInteractRequest);
        await AssertEnvelopeDoesNotLeakAsync(otherInteract, HttpStatusCode.NotFound);

        var after = await Phase8InteractionTestHelpers.SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Null(after.ReadAtUtc);
        Assert.Null(after.InteractedAtUtc);
        Assert.Equal(0, after.WalletTransactionCount);
        Assert.Equal(0, after.WalletLedgerEntryCount);
    }

    [Fact]
    public async Task ValidationConflictConsistencyAndUnexpectedFailures_DoNotExposeSensitiveMaterial()
    {
        await using var factory = new Phase8InteractionTestHelpers.FixedClockFactory(Phase8InteractionTestHelpers.DefaultUtcNow);
        await factory.InitializeDatabaseAsync();
        var conflictSeed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "redaction-conflict");
        var consistencySeed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "redaction-consistency", companyReservedBalance: 10m);
        var unexpectedSeed = await Phase8InteractionTestHelpers.SeedSettlementScenarioAsync(factory, "redaction-unexpected");

        using var client = Phase8InteractionTestHelpers.CreateDoctorClient(factory, conflictSeed.DoctorUserId);
        using var validationRequest = Phase8InteractionTestHelpers.CreateInteractRequest(
            conflictSeed.DeliveryId,
            "short",
            "Accept",
            FullFeedbackText);
        using var validation = await client.SendAsync(validationRequest);
        await AssertEnvelopeDoesNotLeakAsync(validation, HttpStatusCode.BadRequest);

        using var first = Phase8InteractionTestHelpers.CreateInteractRequest(conflictSeed.DeliveryId, RawIdempotencyKey, "Accept", "safe");
        using var firstResponse = await client.SendAsync(first);
        await Phase8InteractionTestHelpers.ReadSuccessDataAsync(firstResponse);
        using var conflict = Phase8InteractionTestHelpers.CreateInteractRequest(conflictSeed.DeliveryId, RawIdempotencyKey, "Reject", FullFeedbackText);
        using var conflictResponse = await client.SendAsync(conflict);
        await AssertEnvelopeDoesNotLeakAsync(conflictResponse, HttpStatusCode.Conflict);

        using var consistencyClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, consistencySeed.DoctorUserId);
        using var consistency = Phase8InteractionTestHelpers.CreateInteractRequest(consistencySeed.DeliveryId, "consistency-key-0001", "Accept", FullFeedbackText);
        using var consistencyResponse = await consistencyClient.SendAsync(consistency);
        await AssertEnvelopeDoesNotLeakAsync(consistencyResponse, HttpStatusCode.InternalServerError);

        await ExecuteSqlAsync(factory, """
            CREATE TRIGGER [TR_Phase8_RedactionUnexpected] ON [WalletTransactions] AFTER INSERT AS
            BEGIN THROW 52099, 'secret-idempotency-key-0001 full feedback wallet balance stack trace', 1; END
            """);
        try
        {
            using var unexpectedClient = Phase8InteractionTestHelpers.CreateDoctorClient(factory, unexpectedSeed.DoctorUserId);
            using var unexpected = Phase8InteractionTestHelpers.CreateInteractRequest(unexpectedSeed.DeliveryId, "unexpected-key-0001", "Accept", FullFeedbackText);
            using var unexpectedResponse = await unexpectedClient.SendAsync(unexpected);
            await AssertEnvelopeDoesNotLeakAsync(unexpectedResponse, HttpStatusCode.InternalServerError);
        }
        finally
        {
            await ExecuteSqlAsync(factory, "DROP TRIGGER [TR_Phase8_RedactionUnexpected]");
        }

        var auditPayloads = await ReadPhase8AuditPayloadsAsync(factory);
        Assert.NotEmpty(auditPayloads);
        foreach (var payload in auditPayloads)
        {
            AssertDoesNotContainSensitiveMaterial(payload);
        }
    }

    private static HttpClient CreateClient(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        string role,
        string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(role, userId));
        return client;
    }

    private static async Task SetDoctorAccountStatusAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory,
        string doctorUserId,
        AccountStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await context.Users.SingleAsync(item => item.Id == doctorUserId);
        user.AccountStatus = status;
        user.ApprovedAtUtc = status == AccountStatus.Approved ? Phase8InteractionTestHelpers.DefaultUtcNow : null;
        user.LastStatusChangedAtUtc = Phase8InteractionTestHelpers.DefaultUtcNow;
        await context.SaveChangesAsync();
    }

    private static async Task AssertEnvelopeDoesNotLeakAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal((int)expectedStatusCode, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
        AssertDoesNotContainSensitiveMaterial(body);
    }

    private static async Task<IReadOnlyList<string>> ReadPhase8AuditPayloadsAsync(
        Phase8InteractionTestHelpers.FixedClockFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        return await context.AuditEvents
            .AsNoTracking()
            .Where(item => item.EventType.StartsWith("Phase8"))
            .Select(item => (item.Metadata ?? string.Empty) + " " + (item.Reason ?? string.Empty))
            .ToArrayAsync();
    }

    private static async Task ExecuteSqlAsync(Phase8InteractionTestHelpers.FixedClockFactory factory, string sql)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static void AssertDoesNotContainSensitiveMaterial(string text)
    {
        Assert.DoesNotContain(RawIdempotencyKey, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(FullFeedbackText, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReservedBalance", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AvailableBalance", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("company-wallet", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("doctor-wallet", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StorageKey", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at MediBridge.", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request body", text, StringComparison.OrdinalIgnoreCase);
    }
}
