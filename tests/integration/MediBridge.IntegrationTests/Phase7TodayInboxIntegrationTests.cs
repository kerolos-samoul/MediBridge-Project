using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7TodayInboxIntegrationTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 3, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TodayInbox_RequiresAuthentication()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/doctor/messages/today");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TodayInbox_FiltersByDoctorAndCairoDate_OrdersByActivation_AndPagesWithoutDuplicates()
    {
        await using var factory = new FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var doctor = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            factory.Services, $"user-{suffix}", $"doctor-{suffix}", $"doctor-{suffix}@example.test", 50m, 10, UtcNow.AddDays(-10));
        var otherDoctor = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            factory.Services, $"other-user-{suffix}", $"other-doctor-{suffix}", $"other-{suffix}@example.test", 50m, 10, UtcNow.AddDays(-10));
        var company = await Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(
            factory.Services, $"company-user-{suffix}", $"company-{suffix}", $"company-{suffix}@example.test", UtcNow.AddDays(-10));
        var today = new DateOnly(2026, 7, 3);
        var campaignIds = Enumerable.Range(1, 5).Select(index => $"campaign-{index}-{suffix}").ToArray();
        foreach (var campaignId in campaignIds)
        {
            await Phase7DeliveryTestHelpers.SeedCampaignAsync(
                factory.Services, campaignId, company.CompanyId, CampaignStatus.Approved, UtcNow.AddDays(-2), UtcNow.AddDays(-3), false, null);
        }

        await SeedDeliveryAsync(factory.Services, $"today-b-{suffix}", doctor.DoctorId, campaignIds[1], company.CompanyId, today, UtcNow.AddMinutes(-10), DeliveryStatus.Accepted, UtcNow.AddHours(-2));
        await SeedDeliveryAsync(factory.Services, $"today-a-{suffix}", doctor.DoctorId, campaignIds[0], company.CompanyId, today, UtcNow.AddMinutes(-10), DeliveryStatus.Active, UtcNow.AddMinutes(-1));
        await SeedDeliveryAsync(factory.Services, $"today-c-{suffix}", doctor.DoctorId, campaignIds[2], company.CompanyId, today, UtcNow.AddMinutes(-5), DeliveryStatus.Rejected, UtcNow.AddDays(-1));
        await SeedDeliveryAsync(factory.Services, $"yesterday-{suffix}", doctor.DoctorId, campaignIds[3], company.CompanyId, today.AddDays(-1), UtcNow.AddDays(-1), DeliveryStatus.Active, UtcNow.AddDays(-1));
        await SeedDeliveryAsync(factory.Services, $"tomorrow-{suffix}", doctor.DoctorId, campaignIds[4], company.CompanyId, today.AddDays(1), UtcNow.AddDays(1), DeliveryStatus.Active, UtcNow.AddDays(1));
        await SeedDeliveryAsync(factory.Services, $"other-{suffix}", otherDoctor.DoctorId, campaignIds[0], company.CompanyId, today, UtcNow.AddMinutes(-20), DeliveryStatus.Active, UtcNow.AddMinutes(-20));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));
        var ids = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        do
        {
            var path = "/api/doctor/messages/today?PageSize=1" + (cursor is null ? string.Empty : $"&Cursor={Uri.EscapeDataString(cursor)}");
            using var response = await client.GetAsync(path);
            var responseText = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Expected 200 after [{string.Join(',', ids)}], got {(int)response.StatusCode}: {responseText}");
            using var document = JsonDocument.Parse(responseText);
            var data = document.RootElement.GetProperty("Data");
            Assert.Equal("2026-07-03", data.GetProperty("BusinessDateEgypt").GetString());
            ids.AddRange(data.GetProperty("Items").EnumerateArray().Select(item => item.GetProperty("DeliveryId").GetString()!));
            cursor = data.GetProperty("NextCursor").ValueKind == JsonValueKind.Null
                ? null
                : data.GetProperty("NextCursor").GetString();
            firstCursor ??= cursor;
        }
        while (cursor is not null);

        Assert.Equal(new[] { $"today-a-{suffix}", $"today-b-{suffix}", $"today-c-{suffix}" }, ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        using var malformed = await client.GetAsync("/api/doctor/messages/today?Cursor=broken");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);

        using var fullPage = await client.GetAsync("/api/doctor/messages/today?PageSize=100");
        using var fullPageDocument = JsonDocument.Parse(await fullPage.Content.ReadAsStringAsync());
        Assert.Equal(3, fullPageDocument.RootElement.GetProperty("Data").GetProperty("Items").GetArrayLength());

        using var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", otherDoctor.UserId));
        using var crossDoctorCursor = await otherClient.GetAsync($"/api/doctor/messages/today?Cursor={Uri.EscapeDataString(firstCursor!)}");
        Assert.Equal(HttpStatusCode.BadRequest, crossDoctorCursor.StatusCode);

        using var companyClient = factory.CreateClient();
        companyClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Company", company.UserId));
        using var companyDenied = await companyClient.GetAsync("/api/doctor/messages/today");
        Assert.Equal(HttpStatusCode.Forbidden, companyDenied.StatusCode);

        using var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Admin", $"admin-{suffix}"));
        using var adminDenied = await adminClient.GetAsync("/api/doctor/messages/today");
        Assert.Equal(HttpStatusCode.Forbidden, adminDenied.StatusCode);

        factory.Clock.SetUtcNow(UtcNow.AddDays(1));
        using var staleCursor = await client.GetAsync($"/api/doctor/messages/today?Cursor={Uri.EscapeDataString(firstCursor!)}");
        Assert.Equal(HttpStatusCode.BadRequest, staleCursor.StatusCode);
        using var nextDay = await client.GetAsync("/api/doctor/messages/today");
        using var nextDayDocument = JsonDocument.Parse(await nextDay.Content.ReadAsStringAsync());
        var nextDayItems = nextDayDocument.RootElement.GetProperty("Data").GetProperty("Items").EnumerateArray().ToArray();
        Assert.Single(nextDayItems);
        Assert.Equal($"tomorrow-{suffix}", nextDayItems[0].GetProperty("DeliveryId").GetString());
    }

    [Fact]
    public async Task TodayInbox_DefaultAndMaximumPageSizesRemainReachable()
    {
        await using var factory = new FixedClockFactory(UtcNow);
        await factory.InitializeDatabaseAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var doctor = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            factory.Services, $"page-user-{suffix}", $"page-doctor-{suffix}", $"page-{suffix}@example.test", 50m, 101, UtcNow.AddDays(-10));
        var company = await Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(
            factory.Services, $"page-company-user-{suffix}", $"page-company-{suffix}", $"page-company-{suffix}@example.test", UtcNow.AddDays(-10));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            for (var index = 0; index < 101; index++)
            {
                var campaignId = $"page-campaign-{index:D3}-{suffix}";
                db.Campaigns.Add(new Campaign
                {
                    Id = campaignId,
                    CompanyId = company.CompanyId,
                    Title = $"Page campaign {index}",
                    Description = "Paging boundary",
                    Status = CampaignStatus.Approved,
                    SubmittedAtUtc = UtcNow.AddDays(-2),
                    CreatedAtUtc = UtcNow.AddDays(-3)
                });
                db.DoctorAdDeliveries.Add(new DoctorAdDelivery
                {
                    Id = $"page-delivery-{index:D3}-{suffix}",
                    DoctorId = doctor.DoctorId,
                    CampaignId = campaignId,
                    CompanyId = company.CompanyId,
                    DeliveryDateEgypt = new DateOnly(2026, 7, 3),
                    DeliveredAtUtc = UtcNow.AddMinutes(index),
                    Status = DeliveryStatus.Active,
                    ReservationStatus = ReservationStatus.Reserved,
                    PricePerMessageSnapshot = 50m,
                    PlatformFeePercentSnapshot = 20m,
                    PlatformFeeAmount = 10m,
                    DoctorEarnings = 40m,
                    ReservedAmount = 50m,
                    CreatedAtUtc = UtcNow.AddMinutes(index)
                });
            }

            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));
        using var defaultPage = await client.GetAsync("/api/doctor/messages/today");
        using var defaultDocument = JsonDocument.Parse(await defaultPage.Content.ReadAsStringAsync());
        Assert.Equal(50, defaultDocument.RootElement.GetProperty("Data").GetProperty("Items").GetArrayLength());
        Assert.Equal(JsonValueKind.String, defaultDocument.RootElement.GetProperty("Data").GetProperty("NextCursor").ValueKind);

        using var maximumPage = await client.GetAsync("/api/doctor/messages/today?PageSize=100");
        using var maximumDocument = JsonDocument.Parse(await maximumPage.Content.ReadAsStringAsync());
        Assert.Equal(100, maximumDocument.RootElement.GetProperty("Data").GetProperty("Items").GetArrayLength());
        Assert.Equal(JsonValueKind.String, maximumDocument.RootElement.GetProperty("Data").GetProperty("NextCursor").ValueKind);
    }

    [Fact]
    public async Task TodayInbox_ChangesBusinessDateAtCairoDstTransition()
    {
        var transitionUtc = FindCairoOffsetTransitionUtc(2026);
        var cairo = ResolveCairo();
        var beforeUtc = transitionUtc.AddMinutes(-1);
        var beforeDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(beforeUtc, cairo));
        var afterDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(transitionUtc, cairo));
        Assert.NotEqual(beforeDate, afterDate);

        await using var factory = new FixedClockFactory(beforeUtc);
        await factory.InitializeDatabaseAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var doctor = await Phase7DeliveryTestHelpers.SeedApprovedDoctorAsync(
            factory.Services, $"dst-user-{suffix}", $"dst-doctor-{suffix}", $"dst-{suffix}@example.test", 50m, 10, beforeUtc.AddDays(-10));
        var company = await Phase7DeliveryTestHelpers.SeedApprovedCompanyAsync(
            factory.Services, $"dst-company-user-{suffix}", $"dst-company-{suffix}", $"dst-company-{suffix}@example.test", beforeUtc.AddDays(-10));
        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            factory.Services, $"dst-before-campaign-{suffix}", company.CompanyId, CampaignStatus.Approved, beforeUtc.AddDays(-2), beforeUtc.AddDays(-3), false, null);
        await Phase7DeliveryTestHelpers.SeedCampaignAsync(
            factory.Services, $"dst-after-campaign-{suffix}", company.CompanyId, CampaignStatus.Approved, beforeUtc.AddDays(-2), beforeUtc.AddDays(-3), false, null);
        await SeedDeliveryAsync(factory.Services, $"dst-before-{suffix}", doctor.DoctorId, $"dst-before-campaign-{suffix}", company.CompanyId, beforeDate, beforeUtc, DeliveryStatus.Active, beforeUtc);
        await SeedDeliveryAsync(factory.Services, $"dst-after-{suffix}", doctor.DoctorId, $"dst-after-campaign-{suffix}", company.CompanyId, afterDate, transitionUtc, DeliveryStatus.Active, transitionUtc);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.CreateToken("Doctor", doctor.UserId));
        Assert.Equal($"dst-before-{suffix}", await ReadSingleDeliveryIdAsync(client));
        factory.Clock.SetUtcNow(transitionUtc);
        Assert.Equal($"dst-after-{suffix}", await ReadSingleDeliveryIdAsync(client));
    }

    private static Task SeedDeliveryAsync(
        IServiceProvider services,
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly date,
        DateTime deliveredAtUtc,
        DeliveryStatus status,
        DateTime createdAtUtc)
    {
        return Phase7DeliveryTestHelpers.SeedDeliveryAsync(
            services, deliveryId, doctorId, campaignId, companyId, date, deliveredAtUtc, status,
            status == DeliveryStatus.Active ? ReservationStatus.Reserved : ReservationStatus.Charged,
            50m, 20m, 10m, 40m, 50m, createdAtUtc);
    }

    private static async Task<string> ReadSingleDeliveryIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/doctor/messages/today");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("Data").GetProperty("Items").EnumerateArray().ToArray();
        Assert.Single(items);
        return items[0].GetProperty("DeliveryId").GetString()!;
    }

    private static DateTime FindCairoOffsetTransitionUtc(int year)
    {
        var cairo = ResolveCairo();
        var cursor = new DateTime(year, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var offset = cairo.GetUtcOffset(cursor);
        while (cursor < end)
        {
            cursor = cursor.AddMinutes(1);
            var nextOffset = cairo.GetUtcOffset(cursor);
            if (nextOffset != offset)
            {
                return cursor;
            }
        }

        throw new InvalidOperationException($"No Cairo DST transition was found in {year}.");
    }

    private static TimeZoneInfo ResolveCairo()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        }
    }

    private sealed class FixedClockFactory(DateTime utcNow) : ConfiguredWebAppFactory
    {
        public AdjustableTimeProvider Clock { get; } = new(utcNow);

        protected override void ConfigureWebHostCore(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }
    }

    private sealed class AdjustableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTime currentUtc = utcNow;

        public override DateTimeOffset GetUtcNow() => new(currentUtc);

        public void SetUtcNow(DateTime value) => currentUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}
