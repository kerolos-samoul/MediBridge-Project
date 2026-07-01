using System.Net;
using System.Net.Http.Json;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests.Campaigns;

public sealed class AdminCampaignApprovalCompanyStatusConcurrencyTests
{
    [Fact]
    public async Task Approval_HoldsCompanyStatusStableUntilTheDecisionTransactionCompletes()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(
            factory,
            campaign.CampaignId,
            actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(
            factory,
            campaign.CampaignId);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var blockerScope = factory.Services.CreateScope();
        var blockerContext = blockerScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await using var blockerTransaction = await blockerContext.Database.BeginTransactionAsync();
        var blockerSessionId = await blockerContext.Database
            .SqlQueryRaw<int>("SELECT CONVERT(int, @@SPID) AS [Value]")
            .SingleAsync();
        await blockerContext.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT COUNT(*)
            FROM [DoctorMessageQueues] WITH (TABLOCKX, HOLDLOCK);
            """);

        var approvalTask = ReviewAsync(admin, campaign.CampaignId, "company-status-lock", "Approved");
        var companyStatusMutationWasBlocked = false;
        var companyProfileDeletionWasBlocked = false;

        try
        {
            await WaitUntilApprovalIsBlockedOnQueuePersistenceAsync(factory, blockerSessionId, approvalTask);
            companyStatusMutationWasBlocked = await IsConcurrentCompanyStatusMutationBlockedAsync(
                factory,
                actors.CompanyUserId);
            companyProfileDeletionWasBlocked = await IsConcurrentCompanyProfileDeletionBlockedAsync(
                factory,
                actors.CompanyProfileId);
        }
        finally
        {
            await blockerTransaction.RollbackAsync();
        }

        using var approvalResponse = await approvalTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);
        Assert.True(
            companyStatusMutationWasBlocked,
            "Campaign approval must retain a lock on the owning company's account row until its transaction completes.");
        Assert.True(
            companyProfileDeletionWasBlocked,
            "Campaign approval must retain a lock on the owning company profile until its transaction completes.");
    }

    [Fact]
    public async Task IdenticalApprovalReplay_ReturnsStoredResultAfterCompanyIsSuspended()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        var actors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
        var campaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
            factory,
            actors.CompanyProfileId,
            DateTime.UtcNow.AddMinutes(-5));
        await WalletCampaignWorkflowTestHelpers.AddTargetSnapshotAsync(
            factory,
            campaign.CampaignId,
            actors.DoctorProfileId);
        await WalletCampaignWorkflowTestHelpers.AddApprovedCampaignMediaAsync(factory, campaign.CampaignId);
        using var admin = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, actors.AdminUserId);

        using var first = await ReviewAsync(admin, campaign.CampaignId, "replay-after-suspension", "Approved");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await SetCompanyStatusAsync(factory, actors.CompanyUserId, AccountStatus.Suspended);

        using var replay = await ReviewAsync(admin, campaign.CampaignId, "replay-after-suspension", "Approved");

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await context.CampaignReviewHistories.CountAsync(item => item.CampaignId == campaign.CampaignId));
        Assert.Equal(1, await context.DoctorMessageQueues.CountAsync(item => item.CampaignId == campaign.CampaignId));
    }

    private static async Task WaitUntilApprovalIsBlockedOnQueuePersistenceAsync(
        WebAppFactory factory,
        int blockerSessionId,
        Task<HttpResponseMessage> approvalTask)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();

        while (!timeout.IsCancellationRequested)
        {
            if (approvalTask.IsCompleted)
            {
                using var response = await approvalTask;
                throw new Xunit.Sdk.XunitException(
                    $"Approval completed with HTTP {(int)response.StatusCode} before reaching queue persistence.");
            }

            var blockedRequestCount = await context.Database
                .SqlQuery<int>($"""
                    SELECT COUNT(*) AS [Value]
                    FROM sys.dm_exec_requests
                    WHERE [blocking_session_id] = {blockerSessionId}
                      AND [database_id] = DB_ID()
                    """)
                .SingleAsync(timeout.Token);
            if (blockedRequestCount > 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
        }

        throw new TimeoutException("Approval did not reach queue persistence within the test timeout.");
    }

    private static async Task<bool> IsConcurrentCompanyStatusMutationBlockedAsync(
        WebAppFactory factory,
        string companyUserId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                SET LOCK_TIMEOUT 500;
                UPDATE [Users]
                SET [AccountStatus] = {(int)AccountStatus.Suspended}
                WHERE [Id] = {companyUserId};
                """);
            return false;
        }
        catch (SqlException exception) when (exception.Number == 1222)
        {
            return true;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<bool> IsConcurrentCompanyProfileDeletionBlockedAsync(
        WebAppFactory factory,
        string companyProfileId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                SET LOCK_TIMEOUT 500;
                UPDATE [CompanyProfiles]
                SET [IsDeleted] = CAST(1 AS bit),
                    [DeletedAtUtc] = SYSUTCDATETIME()
                WHERE [Id] = {companyProfileId};
                """);
            return false;
        }
        catch (SqlException exception) when (exception.Number == 1222)
        {
            return true;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task SetCompanyStatusAsync(
        WebAppFactory factory,
        string companyUserId,
        AccountStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var companyUser = await context.Users.SingleAsync(user => user.Id == companyUserId);
        companyUser.AccountStatus = status;
        await context.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> ReviewAsync(
        HttpClient client,
        string campaignId,
        string idempotencyKey,
        string decision)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/campaigns/{campaignId}/review")
        {
            Content = JsonContent.Create(new { Decision = decision, Reason = (string?)null })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }
}
