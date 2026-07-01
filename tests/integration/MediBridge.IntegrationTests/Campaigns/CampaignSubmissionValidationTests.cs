using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Wallets;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class CampaignSubmissionValidationTests
{
    [Fact]
    public async Task SubmitCampaign_WithPendingAsset_SucceedsWithoutWalletMutation()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var ids = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using (var seedScope = factory.Services.CreateScope())
        {
            var seedContext = seedScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
            await seedContext.Wallets.AddAsync(new Wallet
            {
                OwnerType = WalletOwnerType.Company,
                OwnerId = ids.CompanyProfileId,
                OwnerUserId = ids.CompanyUserId,
                AvailableBalance = 500m,
                ReservedBalance = 0m
            });
            await seedContext.SaveChangesAsync();
        }
        using var client = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, ids.CompanyUserId);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, ids.AdminUserId);
        using var price = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{ids.DoctorProfileId}/price",
            new { PricePerMessage = 50m, Reason = "Submission validation price" });
        Assert.Equal(HttpStatusCode.OK, price.StatusCode);

        using var draftResponse = await client.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "Reviewable draft", Description = "Pending asset can enter campaign moderation." });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        using var draftDocument = JsonDocument.Parse(await draftResponse.Content.ReadAsStringAsync());
        var campaignId = draftDocument.RootElement.GetProperty("Data").GetProperty("campaignId").GetString()!;

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(new byte[] { 1, 2, 3 });
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "pending.png");
        using var assetResponse = await client.PostAsync($"/api/company/campaigns/{campaignId}/assets", form);
        Assert.Equal(HttpStatusCode.Created, assetResponse.StatusCode);

        using var submitRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/company/campaigns/{campaignId}/submit");
        submitRequest.Headers.Add("Idempotency-Key", $"submit-{Guid.NewGuid():N}");
        using var submitResponse = await client.SendAsync(submitRequest);

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        using var submitDocument = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync());
        Assert.Equal(200, submitDocument.RootElement.GetProperty("Code").GetInt32());
        var data = submitDocument.RootElement.GetProperty("Data");
        Assert.Equal("PendingReview", data.GetProperty("status").GetString());
        Assert.Equal("EGP", data.GetProperty("currency").GetString());
        Assert.True(data.GetProperty("estimatedCost").GetDecimal() > 0m);
        Assert.NotEqual(default, data.GetProperty("submittedAtUtc").GetDateTime());
        Assert.NotEmpty(await WalletCampaignWorkflowTestHelpers.ListTargetsAsync(factory, campaignId));

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var wallet = await context.Wallets.AsNoTracking().SingleAsync(item => item.OwnerId == ids.CompanyProfileId);
        Assert.Equal(500m, wallet.AvailableBalance);
        Assert.Equal(0m, wallet.ReservedBalance);
        Assert.Empty(await context.WalletTransactions.Where(item => item.WalletId == wallet.Id).ToListAsync());
        Assert.Empty(await context.WalletLedgerEntries.Where(item => item.WalletId == wallet.Id).ToListAsync());
        Assert.Equal(1, await context.CampaignSubmissionAttempts.CountAsync(item => item.CampaignId == campaignId));
    }
}
