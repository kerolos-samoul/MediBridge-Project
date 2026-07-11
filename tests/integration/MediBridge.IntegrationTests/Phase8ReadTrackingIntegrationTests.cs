using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase8ReadTrackingIntegrationTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly TodayEgypt = new(2026, 7, 10);

    [Fact]
    public async Task MarkRead_FirstReadSetsTimestamp_SecondReadReplays_AndFinancialStateIsUnchanged()
    {
        await using var factory = new FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadScenarioAsync(factory, "idempotent-read");

        using var client = CreateClient(factory, "Doctor", seed.DoctorUserId);
        var before = await SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);

        using var firstResponse = await client.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
        using var secondResponse = await client.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var firstData = await ReadDataAsync(firstResponse);
        var secondData = await ReadDataAsync(secondResponse);
        Assert.Equal(seed.DeliveryId, firstData.GetProperty("DeliveryId").GetString());
        Assert.Equal(seed.DeliveryId, secondData.GetProperty("DeliveryId").GetString());
        Assert.Equal("Created", firstData.GetProperty("ReadStatus").GetString());
        Assert.Equal("Replayed", secondData.GetProperty("ReadStatus").GetString());
        Assert.Equal(
            firstData.GetProperty("ReadAtUtc").GetDateTime(),
            secondData.GetProperty("ReadAtUtc").GetDateTime());

        var after = await SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Equal(firstData.GetProperty("ReadAtUtc").GetDateTime(), after.ReadAtUtc);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.ReservationStatus, after.ReservationStatus);
        Assert.Equal(before.CompanyAvailableBalance, after.CompanyAvailableBalance);
        Assert.Equal(before.CompanyReservedBalance, after.CompanyReservedBalance);
        Assert.Equal(before.DoctorAvailableBalance, after.DoctorAvailableBalance);
        Assert.Equal(before.DoctorReservedBalance, after.DoctorReservedBalance);
        Assert.Equal(before.WalletTransactionCount, after.WalletTransactionCount);
        Assert.Equal(before.WalletLedgerEntryCount, after.WalletLedgerEntryCount);
    }

    [Fact]
    public async Task MarkRead_DeniesUnauthenticatedWrongRolesAndOtherDoctors_WithSafeEnvelopes()
    {
        await using var factory = new FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var seed = await SeedReadScenarioAsync(factory, "authorization");

        using var unauthenticatedClient = factory.CreateClient();
        using var unauthenticated = await unauthenticatedClient.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var companyClient = CreateClient(factory, "Company", seed.CompanyUserId);
        using var company = await companyClient.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
        await AssertSafeEmptyEnvelopeAsync(company, HttpStatusCode.Forbidden);

        using var adminClient = CreateClient(factory, "Admin", $"admin-{seed.Suffix}");
        using var admin = await adminClient.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
        await AssertSafeEmptyEnvelopeAsync(admin, HttpStatusCode.Forbidden);

        var otherDoctor = await Phase8InteractionTestHelpers.SeedApprovedActiveDoctorAsync(
            factory.Services,
            $"other-doctor-user-{seed.Suffix}",
            $"other-doctor-{seed.Suffix}",
            $"other-doctor-{seed.Suffix}@example.test",
            50m,
            10,
            UtcNow.AddDays(-10));
        using var otherDoctorClient = CreateClient(factory, "Doctor", otherDoctor.UserId);
        using var otherDoctorResponse = await otherDoctorClient.PutAsync($"/api/doctor/messages/{seed.DeliveryId}/read", content: null);
        await AssertSafeEmptyEnvelopeAsync(otherDoctorResponse, HttpStatusCode.NotFound);

        var after = await SnapshotAsync(factory, seed.DeliveryId, seed.CompanyWalletId, seed.DoctorWalletId);
        Assert.Null(after.ReadAtUtc);
        Assert.Equal(0, after.WalletTransactionCount);
        Assert.Equal(0, after.WalletLedgerEntryCount);
    }

    private static async Task<ReadScenarioSeed> SeedReadScenarioAsync(ConfiguredWebAppFactory factory, string label)
    {
        var suffix = $"{label}-{Guid.NewGuid():N}";
        var doctor = await Phase8InteractionTestHelpers.SeedApprovedActiveDoctorAsync(
            factory.Services,
            $"doctor-user-{suffix}",
            $"doctor-{suffix}",
            $"doctor-{suffix}@example.test",
            50m,
            10,
            UtcNow.AddDays(-10));
        var company = await Phase8InteractionTestHelpers.SeedApprovedCompanyAsync(
            factory.Services,
            $"company-user-{suffix}",
            $"company-{suffix}",
            $"company-{suffix}@example.test",
            UtcNow.AddDays(-10));
        var campaignId = $"campaign-{suffix}";
        var deliveryId = $"delivery-{suffix}";
        var companyWalletId = $"company-wallet-{suffix}";
        var doctorWalletId = $"doctor-wallet-{suffix}";

        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            factory.Services,
            campaignId,
            company.CompanyId,
            CampaignStatus.Approved,
            UtcNow.AddDays(-2),
            UtcNow.AddDays(-3),
            false,
            null);
        await Phase8InteractionTestHelpers.SeedCompanyWalletAsync(
            factory.Services,
            companyWalletId,
            company.CompanyId,
            company.UserId,
            availableBalance: 200m,
            reservedBalance: 50m,
            UtcNow.AddDays(-2));
        await Phase8InteractionTestHelpers.SeedDoctorWalletAsync(
            factory.Services,
            doctorWalletId,
            doctor.DoctorId,
            doctor.UserId,
            availableBalance: 25m,
            reservedBalance: 0m,
            UtcNow.AddDays(-2));
        await Phase8InteractionTestHelpers.SeedCurrentEgyptDateDeliveryAsync(
            factory.Services,
            deliveryId,
            doctor.DoctorId,
            campaignId,
            company.CompanyId,
            TodayEgypt,
            UtcNow.AddMinutes(-30),
            pricePerMessageSnapshot: 50m,
            platformFeePercentSnapshot: 20m,
            platformFeeAmount: 10m,
            doctorEarnings: 40m,
            reservedAmount: 50m,
            UtcNow.AddMinutes(-30));

        return new ReadScenarioSeed(
            suffix,
            doctor.UserId,
            company.UserId,
            deliveryId,
            companyWalletId,
            doctorWalletId);
    }

    private static HttpClient CreateClient(ConfiguredWebAppFactory factory, string role, string userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken(role, userId));
        return client;
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(200, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal("Message read recorded.", document.RootElement.GetProperty("Message").GetString());
        return document.RootElement.GetProperty("Data").Clone();
    }

    private static async Task AssertSafeEmptyEnvelopeAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expectedStatusCode, document.RootElement.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Data").ValueKind);
        var body = document.RootElement.ToString();
        Assert.DoesNotContain("company-wallet", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("doctor-wallet", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Campaign", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ReadStateSnapshot> SnapshotAsync(
        ConfiguredWebAppFactory factory,
        string deliveryId,
        string companyWalletId,
        string doctorWalletId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var delivery = await context.DoctorAdDeliveries.AsNoTracking().SingleAsync(item => item.Id == deliveryId);
        var companyWallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == companyWalletId);
        var doctorWallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.Id == doctorWalletId);

        return new ReadStateSnapshot(
            delivery.ReadAtUtc,
            delivery.Status,
            delivery.ReservationStatus,
            companyWallet.AvailableBalance,
            companyWallet.ReservedBalance,
            doctorWallet.AvailableBalance,
            doctorWallet.ReservedBalance,
            await context.WalletTransactions.CountAsync(),
            await context.WalletLedgerEntries.CountAsync());
    }

    private sealed record ReadScenarioSeed(
        string Suffix,
        string DoctorUserId,
        string CompanyUserId,
        string DeliveryId,
        string CompanyWalletId,
        string DoctorWalletId);

    private sealed record ReadStateSnapshot(
        DateTime? ReadAtUtc,
        DeliveryStatus Status,
        ReservationStatus ReservationStatus,
        decimal CompanyAvailableBalance,
        decimal CompanyReservedBalance,
        decimal DoctorAvailableBalance,
        decimal DoctorReservedBalance,
        int WalletTransactionCount,
        int WalletLedgerEntryCount);

    private sealed class FixedClockFactory(DateTime utcNow) : ConfiguredWebAppFactory
    {
        private readonly AdjustableTimeProvider clock = new(utcNow);

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        }
    }

    private sealed class AdjustableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTime utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
