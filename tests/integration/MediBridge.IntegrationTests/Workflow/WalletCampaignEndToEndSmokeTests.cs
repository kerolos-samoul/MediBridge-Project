using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignEndToEndSmokeTests
{
    [Fact]
    public async Task FullWalletCampaignWorkflow_CompletesThroughPublicHttp()
    {
        var stopwatch = Stopwatch.StartNew();
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);
        using var company = WalletCampaignWorkflowTestHelpers.CreateCompanyClient(factory, actors.CompanyUserId);

        using var wallet = await company.GetAsync("/api/company/wallet");
        await AssertEnvelopeAsync(wallet, HttpStatusCode.OK);

        const string topUpKey = "workflow-smoke-topup-001";
        using var topUp = await SendTopUpAsync(company, topUpKey, 500m);
        var topUpData = await AssertEnvelopeAsync(topUp, HttpStatusCode.OK);
        var paymentId = topUpData.GetProperty("paymentId").GetString();
        var transactionReference = topUpData.GetProperty("transactionReference").GetString();
        Assert.Equal("Succeeded", topUpData.GetProperty("status").GetString());

        using var replay = await SendTopUpAsync(company, topUpKey, 500m);
        var replayData = await AssertEnvelopeAsync(replay, HttpStatusCode.OK);
        Assert.Equal(paymentId, replayData.GetProperty("paymentId").GetString());
        Assert.Equal(transactionReference, replayData.GetProperty("transactionReference").GetString());

        using var conflict = await SendTopUpAsync(company, topUpKey, 501m);
        await AssertEnvelopeAsync(conflict, HttpStatusCode.Conflict);

        foreach (var invalidPrice in new decimal?[] { null, 0m, -1m, 1.001m })
        {
            using var invalidPriceResponse = await admin.PutAsJsonAsync(
                $"/api/admin/doctors/{actors.DoctorProfileId}/price",
                new { PricePerMessage = invalidPrice });
            await AssertEnvelopeAsync(invalidPriceResponse, HttpStatusCode.BadRequest);
        }

        using var validPrice = await admin.PutAsJsonAsync(
            $"/api/admin/doctors/{actors.DoctorProfileId}/price",
            new { PricePerMessage = 50m, Reason = "End-to-end smoke workflow" });
        await AssertEnvelopeAsync(validPrice, HttpStatusCode.OK);

        using var draft = await company.PostAsJsonAsync(
            "/api/company/campaigns",
            new { Title = "HTTP smoke campaign", Description = "Complete public workflow validation." });
        var draftData = await AssertEnvelopeAsync(draft, HttpStatusCode.Created);
        var campaignId = draftData.GetProperty("campaignId").GetString()!;

        using var upload = await UploadAssetAsync(company, campaignId);
        var uploadData = await AssertEnvelopeAsync(upload, HttpStatusCode.Created);
        var assetId = uploadData.GetProperty("assetId").GetString()!;
        Assert.Equal("Pending", uploadData.GetProperty("reviewStatus").GetString());

        using var submit = await SendWithoutBodyAsync(
            company,
            HttpMethod.Post,
            $"/api/company/campaigns/{campaignId}/submit",
            "workflow-smoke-submit-001");
        var submitData = await AssertEnvelopeAsync(submit, HttpStatusCode.OK);
        Assert.Equal("PendingReview", submitData.GetProperty("status").GetString());

        using var prematureApproval = await ReviewCampaignAsync(admin, campaignId, "workflow-smoke-review-premature");
        await AssertEnvelopeAsync(prematureApproval, HttpStatusCode.BadRequest);

        using var assetReview = await admin.PostAsJsonAsync(
            $"/api/admin/campaign-assets/{assetId}/review",
            new { Decision = "Approved", Reason = (string?)null });
        await AssertEnvelopeAsync(assetReview, HttpStatusCode.OK);

        using var preview = await company.GetAsync($"/api/company/campaigns/{campaignId}/target-preview");
        var previewData = await AssertEnvelopeAsync(preview, HttpStatusCode.OK);
        Assert.Equal(1, previewData.GetProperty("eligibleDoctorCount").GetInt32());

        using var submitReplay = await SendWithoutBodyAsync(
            company,
            HttpMethod.Post,
            $"/api/company/campaigns/{campaignId}/submit",
            "workflow-smoke-submit-001");
        var submitReplayData = await AssertEnvelopeAsync(submitReplay, HttpStatusCode.OK);
        Assert.Equal(submitData.GetProperty("submittedAtUtc").GetDateTime(), submitReplayData.GetProperty("submittedAtUtc").GetDateTime());

        using var approval = await ReviewCampaignAsync(admin, campaignId, "workflow-smoke-review-001");
        var approvalData = await AssertEnvelopeAsync(approval, HttpStatusCode.OK);
        Assert.Equal("Approved", approvalData.GetProperty("status").GetString());
        Assert.Equal(1, approvalData.GetProperty("queuedCount").GetInt32());

        using var approvalReplay = await ReviewCampaignAsync(admin, campaignId, "workflow-smoke-review-001");
        var replayApprovalData = await AssertEnvelopeAsync(approvalReplay, HttpStatusCode.OK);
        Assert.Equal(1, replayApprovalData.GetProperty("queuedCount").GetInt32());

        using var adminQueue = await admin.GetAsync($"/api/admin/campaigns/{campaignId}/queue");
        var queueData = await AssertEnvelopeAsync(adminQueue, HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Array, queueData.ValueKind);
        Assert.Single(queueData.EnumerateArray());

        using var companySummary = await company.GetAsync($"/api/company/campaigns/{campaignId}/queue-summary");
        var summaryData = await AssertEnvelopeAsync(companySummary, HttpStatusCode.OK);
        Assert.Equal(campaignId, summaryData.GetProperty("campaignId").GetString());
        Assert.Equal(1, summaryData.GetProperty("queuedCount").GetInt32());
        Assert.False(summaryData.TryGetProperty("doctorId", out _));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(5), $"Workflow took {stopwatch.Elapsed}.");
    }

    private static Task<HttpResponseMessage> SendTopUpAsync(HttpClient client, string idempotencyKey, decimal amount)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/company/wallet/mock-checkout")
        {
            Content = JsonContent.Create(new { Amount = amount, Currency = "EGP" })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> UploadAssetAsync(HttpClient client, string campaignId)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3, 4]);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "smoke-campaign.png");
        return client.PostAsync($"/api/company/campaigns/{campaignId}/assets", form);
    }

    private static Task<HttpResponseMessage> ReviewCampaignAsync(HttpClient client, string campaignId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = "Approved", Reason = (string?)null })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendWithoutBodyAsync(
        HttpClient client,
        HttpMethod method,
        string route,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> AssertEnvelopeAsync(HttpResponseMessage response, HttpStatusCode statusCode)
    {
        Assert.Equal(statusCode, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal((int)statusCode, root.GetProperty("Code").GetInt32());
        Assert.Equal(JsonValueKind.String, root.GetProperty("Message").ValueKind);
        Assert.True(root.TryGetProperty("Data", out var data));
        return data.Clone();
    }
}
