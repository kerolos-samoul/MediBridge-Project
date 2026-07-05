using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Entities.Files;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Enums;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Repository.Data.Identity;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

/// <summary>
/// Bug Condition Exploration Tests for Post-Merge Blockers
/// 
/// **CRITICAL**: These tests are EXPECTED TO FAIL on unfixed code.
/// Failure confirms the bugs exist. After fixes are applied, these tests will PASS.
/// 
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 2.1, 2.2, 2.3, 3.1, 3.2, 3.3**
/// </summary>
public sealed class PostMergeBlockersExplorationTests
{
    #region P1: Database Schema Drift - Phase 7 Migration Missing

    /// <summary>
    /// Exploration Test: Verify Phase 7 migration is missing from database
    /// 
    /// EXPECTED ON UNFIXED CODE: Migration 20260702174103_AddPhase7DeliveryExpiryJobs NOT present
    /// EXPECTED ON FIXED CODE: Migration present in __EFMigrationsHistory
    /// </summary>
    [Fact]
    public async Task P1_DatabaseMissingPhase7Migration_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var connection = context.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM __EFMigrationsHistory WHERE MigrationId = '20260702174103_AddPhase7DeliveryExpiryJobs'";
        
        var migrationExists = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;

        // On UNFIXED code: This should be FALSE (migration missing)
        // On FIXED code: This should be TRUE (migration applied)
        Assert.True(migrationExists, 
            "COUNTEREXAMPLE: Phase 7 migration 20260702174103_AddPhase7DeliveryExpiryJobs is NOT present in __EFMigrationsHistory. " +
            "This confirms the database schema drift bug exists.");
    }

    /// <summary>
    /// Exploration Test: Verify DeliveryJobRuns table is missing
    /// 
    /// EXPECTED ON UNFIXED CODE: SQL exception (table does not exist)
    /// EXPECTED ON FIXED CODE: Query succeeds
    /// </summary>
    [Fact]
    public async Task P1_DeliveryJobRunsTable_Missing_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        
        try
        {
            // Attempt to query the DeliveryJobRuns table
            await context.Database.ExecuteSqlRawAsync("SELECT COUNT(1) FROM DeliveryJobRuns");
            
            // If we reach here, table exists (fixed code)
            Assert.True(true, "DeliveryJobRuns table exists - fix has been applied");
        }
        catch (SqlException ex)
        {
            // On UNFIXED code: This exception is expected
            Assert.Contains("Invalid object name 'DeliveryJobRuns'", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Fail($"COUNTEREXAMPLE: DeliveryJobRuns table does not exist. SQL Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Exploration Test: Verify DeliveryRecoveryDispatches table is missing
    /// 
    /// EXPECTED ON UNFIXED CODE: SQL exception (table does not exist)
    /// EXPECTED ON FIXED CODE: Query succeeds
    /// </summary>
    [Fact]
    public async Task P1_DeliveryRecoveryDispatchesTable_Missing_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        
        try
        {
            // Attempt to query the DeliveryRecoveryDispatches table
            await context.Database.ExecuteSqlRawAsync("SELECT COUNT(1) FROM DeliveryRecoveryDispatches");
            
            // If we reach here, table exists (fixed code)
            Assert.True(true, "DeliveryRecoveryDispatches table exists - fix has been applied");
        }
        catch (SqlException ex)
        {
            // On UNFIXED code: This exception is expected
            Assert.Contains("Invalid object name 'DeliveryRecoveryDispatches'", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Fail($"COUNTEREXAMPLE: DeliveryRecoveryDispatches table does not exist. SQL Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Exploration Test: Verify DoctorMessageQueues.RowVersion column is missing
    /// 
    /// EXPECTED ON UNFIXED CODE: SQL exception (column does not exist)
    /// EXPECTED ON FIXED CODE: Query succeeds
    /// </summary>
    [Fact]
    public async Task P1_DoctorMessageQueues_RowVersionColumn_Missing_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        
        try
        {
            // Attempt to query the RowVersion column (renamed to ConcurrencyToken in Phase 7)
            await context.Database.ExecuteSqlRawAsync(
                "SELECT COUNT(1) FROM sys.columns WHERE [object_id] = OBJECT_ID('DoctorMessageQueues') AND [name] = 'ConcurrencyToken' AND [system_type_id] = 189");
            
            var connection = context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM sys.columns WHERE [object_id] = OBJECT_ID('DoctorMessageQueues') AND [name] = 'ConcurrencyToken' AND [system_type_id] = 189";
            var columnExists = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
            
            // On FIXED code: Column should exist
            Assert.True(columnExists, "ConcurrencyToken (RowVersion) column exists - fix has been applied");
        }
        catch (SqlException ex)
        {
            // On UNFIXED code: This exception is expected
            Assert.Fail($"COUNTEREXAMPLE: DoctorMessageQueues.ConcurrencyToken column does not exist or has wrong type. SQL Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Exploration Test: Verify DoctorAdDeliveries.ExpiredAtUtc column is missing
    /// 
    /// EXPECTED ON UNFIXED CODE: SQL exception (column does not exist)
    /// EXPECTED ON FIXED CODE: Query succeeds
    /// </summary>
    [Fact]
    public async Task P1_DoctorAdDeliveries_ExpiredAtUtcColumn_Missing_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sys.columns WHERE [object_id] = OBJECT_ID('DoctorAdDeliveries') AND [name] = 'ExpiredAtUtc'";
        var columnExists = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        
        // On UNFIXED code: This should be FALSE
        // On FIXED code: This should be TRUE
        Assert.True(columnExists, 
            "COUNTEREXAMPLE: DoctorAdDeliveries.ExpiredAtUtc column does not exist. " +
            "This confirms the Phase 7 migration is missing.");
    }

    /// <summary>
    /// Exploration Test: Campaign approval fails with HTTP 500 due to missing Phase 7 tables
    /// 
    /// EXPECTED ON UNFIXED CODE: SQL exception when trying to create DeliveryJobRuns
    /// EXPECTED ON FIXED CODE: Campaign approved successfully with queue rows created
    /// </summary>
    [Fact]
    public async Task P1_CampaignApproval_FailsWithSqlException_DueToMissingTables()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();

        // Seed reviewable campaign
        var seed = await SeedReviewableCampaignAsync(factory, StoredFileReviewStatus.Approved);

        try
        {
            // Attempt to approve campaign using the service directly
            using var scope = factory.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IAdminCampaignReviewService>();
            var result = await service.ReviewCampaignAsync(
                seed.AdminUserId,
                seed.CampaignId,
                "approve-exploration-test",
                new ReviewDecisionRequestDto("Approved", null, "Ready for delivery."));

            // If we reach here, the fix has been applied
            Assert.Equal("Approved", result.Decision);
        }
        catch (SqlException ex)
        {
            // On UNFIXED code: This exception is expected
            Assert.Contains("Invalid object name", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Fail($"COUNTEREXAMPLE: Campaign approval failed with SQL exception. " +
                       $"This confirms the bug exists. SQL Error: {ex.Message}");
        }
        catch (DbUpdateException ex)
        {
            // On UNFIXED code: This exception is also expected
            Assert.Fail($"COUNTEREXAMPLE: Campaign approval failed with database update exception. " +
                       $"This confirms the bug exists. Error: {ex.Message}");
        }
    }

    #endregion

    #region P2: Password-Reset Token Discarded - Email Not Sent

    /// <summary>
    /// Exploration Test: Password reset token generated but NOT sent via email
    /// 
    /// EXPECTED ON UNFIXED CODE: HTTP 200 but IEmailSender NOT called (no email sent)
    /// EXPECTED ON FIXED CODE: HTTP 200 AND IEmailSender called with reset token
    /// </summary>
    [Fact]
    public async Task P2_PasswordResetToken_GeneratedButNotEmailed_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        // Register and approve a doctor
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);

        // Get the TestEmailSink to verify emails
        using var scope = factory.Services.CreateScope();
        var emailSink = scope.ServiceProvider.GetRequiredService<TestEmailSink>();
        var initialEmailCount = emailSink.Messages.Count;

        // Request password reset
        var response = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Contact = email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        // Verify token was hashed and saved in database
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var user = await context.Users.SingleAsync(u => u.Email == email);
        var resetFlow = await context.PasswordResetFlows
            .Where(f => f.UserId == user.Id)
            .OrderByDescending(f => f.CreatedAtUtc)
            .FirstOrDefaultAsync();

        Assert.NotNull(resetFlow);
        Assert.NotNull(resetFlow.TokenHash);
        Assert.NotEmpty(resetFlow.TokenHash);

        // Check if email was sent
        var emailsSent = emailSink.Messages.Count - initialEmailCount;

        // On UNFIXED code: emailsSent should be 0 (no email sent)
        // On FIXED code: emailsSent should be > 0 (email with reset token sent)
        Assert.True(emailsSent > 0, 
            $"COUNTEREXAMPLE: Password reset token was generated and hashed (TokenHash: {resetFlow.TokenHash.Substring(0, 20)}...) " +
            $"but NO email was sent. Emails sent: {emailsSent}. " +
            $"This confirms the password reset email bug exists - token is discarded before sending.");
    }

    /// <summary>
    /// Exploration Test: Verify reset token never sent means user cannot complete reset flow
    /// 
    /// EXPECTED ON UNFIXED CODE: User cannot reset password (no token received)
    /// EXPECTED ON FIXED CODE: User receives token and can reset password
    /// </summary>
    [Fact]
    public async Task P2_PasswordReset_CannotCompleteFlow_BecauseTokenNeverReceived()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        // Register and approve a doctor
        var email = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        await Phase6IdentityTestHelpers.SetStatusAsync(factory.Services, email, AccountStatus.Approved);

        // Request password reset
        var forgotResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Contact = email });
        Assert.Equal(HttpStatusCode.Accepted, forgotResponse.StatusCode);

        // Get the TestEmailSink to check for emails
        using var scope = factory.Services.CreateScope();
        var emailSink = scope.ServiceProvider.GetRequiredService<TestEmailSink>();
        
        // On UNFIXED code: No password reset email in the sink
        var passwordResetEmails = emailSink.Messages
            .Where(m => m.Subject?.Contains("password", StringComparison.OrdinalIgnoreCase) == true ||
                       m.Subject?.Contains("reset", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (passwordResetEmails.Count == 0)
        {
            Assert.Fail($"COUNTEREXAMPLE: User requested password reset but received NO email. " +
                       $"Total emails in sink: {emailSink.Messages.Count}. " +
                       $"User cannot complete the password reset flow without the token. " +
                       $"This confirms the bug: plaintext token is discarded before sending.");
        }

        // If we reach here, email was sent (fixed code)
        Assert.True(passwordResetEmails.Count > 0, "Password reset email was sent - fix has been applied");
    }

    #endregion

    #region P3: OpenAPI Documentation Gaps

    /// <summary>
    /// Exploration Test: Bearer security scheme is missing from OpenAPI document
    /// 
    /// EXPECTED ON UNFIXED CODE: components.securitySchemes.Bearer NOT present
    /// EXPECTED ON FIXED CODE: Bearer security scheme declared
    /// </summary>
    [Fact]
    public async Task P3_OpenApiDoc_MissingBearerSecurityScheme_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;

        // Check for components.securitySchemes.Bearer
        bool hasBearerScheme = false;
        if (root.TryGetProperty("components", out var components))
        {
            if (components.TryGetProperty("securitySchemes", out var securitySchemes))
            {
                hasBearerScheme = securitySchemes.TryGetProperty("Bearer", out var bearerScheme) &&
                                 bearerScheme.GetProperty("type").GetString() == "http" &&
                                 bearerScheme.GetProperty("scheme").GetString() == "bearer";
            }
        }

        // On UNFIXED code: hasBearerScheme should be FALSE
        // On FIXED code: hasBearerScheme should be TRUE
        Assert.True(hasBearerScheme, 
            "COUNTEREXAMPLE: Bearer security scheme is NOT declared in OpenAPI document (components.securitySchemes.Bearer missing). " +
            "This confirms the OpenAPI documentation gap bug exists.");
    }

    /// <summary>
    /// Exploration Test: Idempotency-Key parameter NOT marked as required on mutation endpoints
    /// 
    /// EXPECTED ON UNFIXED CODE: POST /campaigns missing Idempotency-Key or not marked as required
    /// EXPECTED ON FIXED CODE: Idempotency-Key present and marked as required
    /// </summary>
    [Fact]
    public async Task P3_OpenApiDoc_IdempotencyKeyNotRequired_OnMutationEndpoints_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");

        // Check POST /api/company/campaigns/drafts endpoint
        bool hasRequiredIdempotencyKey = false;
        if (paths.TryGetProperty("/api/company/campaigns/drafts", out var campaignPath))
        {
            if (campaignPath.TryGetProperty("post", out var postOperation))
            {
                if (postOperation.TryGetProperty("parameters", out var parameters))
                {
                    foreach (var param in parameters.EnumerateArray())
                    {
                        if (param.GetProperty("name").GetString() == "Idempotency-Key" &&
                            param.GetProperty("in").GetString() == "header")
                        {
                            hasRequiredIdempotencyKey = param.TryGetProperty("required", out var required) && 
                                                       required.GetBoolean();
                            break;
                        }
                    }
                }
            }
        }

        // On UNFIXED code: hasRequiredIdempotencyKey should be FALSE
        // On FIXED code: hasRequiredIdempotencyKey should be TRUE
        Assert.True(hasRequiredIdempotencyKey, 
            "COUNTEREXAMPLE: Idempotency-Key header is NOT documented as required on POST /api/company/campaigns/drafts. " +
            "This confirms the OpenAPI documentation gap bug exists.");
    }

    /// <summary>
    /// Exploration Test: Admin pricing endpoint DoctorProfile ID parameter lacks clarifying description
    /// 
    /// EXPECTED ON UNFIXED CODE: Parameter description is missing or unclear
    /// EXPECTED ON FIXED CODE: Parameter has clarifying description mentioning DoctorProfile entity
    /// </summary>
    [Fact]
    public async Task P3_OpenApiDoc_AdminPricingParameter_LacksClarifyingDescription_CounterexampleFound()
    {
        await using var factory = new WebAppFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var paths = document.RootElement.GetProperty("paths");

        // Check PUT /api/admin/doctors/{doctorId}/price endpoint
        bool hasGoodDescription = false;
        if (paths.TryGetProperty("/api/admin/doctors/{doctorId}/price", out var pricingPath))
        {
            if (pricingPath.TryGetProperty("put", out var putOperation))
            {
                if (putOperation.TryGetProperty("parameters", out var parameters))
                {
                    foreach (var param in parameters.EnumerateArray())
                    {
                        if (param.GetProperty("name").GetString() == "doctorId")
                        {
                            if (param.TryGetProperty("description", out var desc))
                            {
                                var description = desc.GetString() ?? "";
                                // Good description should mention DoctorProfile entity distinction
                                hasGoodDescription = description.Contains("DoctorProfile", StringComparison.OrdinalIgnoreCase) &&
                                                    (description.Contains("entity", StringComparison.OrdinalIgnoreCase) ||
                                                     description.Contains("unique identifier", StringComparison.OrdinalIgnoreCase));
                            }
                            break;
                        }
                    }
                }
            }
        }

        // On UNFIXED code: hasGoodDescription should be FALSE
        // On FIXED code: hasGoodDescription should be TRUE
        Assert.True(hasGoodDescription, 
            "COUNTEREXAMPLE: DoctorProfile ID parameter on PUT /api/admin/doctors/{doctorId}/price lacks clarifying description. " +
            "This confirms the OpenAPI documentation gap bug exists.");
    }

    #endregion

    #region Helper Methods

    private static async Task<ReviewableCampaignSeed> SeedReviewableCampaignAsync(
        WebAppFactory factory, 
        StoredFileReviewStatus mediaStatus)
    {
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services);
        var walletId = await Phase5CampaignQueueTestHelpers.SeedCompanyWalletAsync(
            factory.Services,
            company.CompanyId,
            company.UserId,
            1000m);
        var submittedAtUtc = DateTime.UtcNow.AddMinutes(-5);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var suffix = Guid.NewGuid().ToString("N");
        
        var adminEmail = $"admin-{suffix}@medibridge.local";
        var admin = new MediBridgeIdentityUser
        {
            Id = $"admin-{suffix}",
            UserName = adminEmail,
            NormalizedUserName = adminEmail.ToUpperInvariant(),
            Email = adminEmail,
            NormalizedEmail = adminEmail.ToUpperInvariant(),
            Role = UserRole.Admin,
            AccountStatus = AccountStatus.Approved,
            EmailVerified = true,
            EmailConfirmed = true,
            CreatedAtUtc = DateTime.UtcNow,
            PasswordHash = "AQAAAAIAAYagAAAAEDummyHashForTestingPurposesOnly=", // Dummy hash
            SecurityStamp = Guid.NewGuid().ToString()
        };
        
        var campaign = new Campaign
        {
            Id = $"campaign-{suffix}",
            CompanyId = company.CompanyId,
            Title = "Post-merge blocker test campaign",
            Description = "Ready for admin approval to trigger Phase 7 delivery job creation.",
            ClinicalResearchInfo = null,
            Status = CampaignStatus.PendingReview,
            SubmittedAtUtc = submittedAtUtc,
            CreatedAtUtc = submittedAtUtc
        };
        
        var target = new CampaignTarget
        {
            Id = $"target-{suffix}",
            CampaignId = campaign.Id,
            DoctorId = doctor.DoctorId,
            SpecializationSnapshot = "Cardiology",
            ExperienceYearsSnapshot = 7,
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 95m,
            PricePerMessageSnapshot = 50m,
            CreatedAtUtc = submittedAtUtc
        };
        
        var file = new StoredFile
        {
            Id = $"file-{suffix}",
            OwnerType = StoredFileOwnerType.Company,
            OwnerId = company.CompanyId,
            RelatedCampaignId = campaign.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = "campaign.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"campaigns/{suffix}/campaign.png",
            StorageProvider = "TestStorage",
            StorageResourceType = StoredFileStorageResourceType.Image,
            StorageDeliveryType = StoredFileStorageDeliveryType.Private,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = mediaStatus,
            UploadStatus = StoredFileUploadStatus.Stored,
            SafetyScanStatus = StoredFileSafetyScanStatus.Deferred,
            ReviewedAtUtc = mediaStatus == StoredFileReviewStatus.Approved ? DateTime.UtcNow : null,
            CreatedAtUtc = submittedAtUtc
        };

        await context.Users.AddAsync(admin);
        await context.Campaigns.AddAsync(campaign);
        await context.CampaignTargets.AddAsync(target);
        await context.StoredFiles.AddAsync(file);
        await context.SaveChangesAsync();
        
        return new ReviewableCampaignSeed(admin.Id, adminEmail, campaign.Id, walletId, submittedAtUtc);
    }

    private sealed record ReviewableCampaignSeed(
        string AdminUserId,
        string AdminEmail,
        string CampaignId,
        string WalletId,
        DateTime SubmittedAtUtc);

    #endregion
}
