using MediBridge.Core.Entities.Policies;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.DTOs.Wallets;
using MediBridge.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase5AuditSafetyTests : IClassFixture<WebAppFactory>
{
    private static readonly string[] ForbiddenMetadataMarkers =
    [
        "rawGatewayPayload",
        "gatewayPayload",
        "secret",
        "privateFileAccessToken",
        "storageKey",
        "requestBody",
        "responseBody",
        "stackTrace"
    ];

    private readonly WebAppFactory factory;

    public Phase5AuditSafetyTests(WebAppFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("""{"rawGatewayPayload":{"id":"provider-payload"}}""")]
    [InlineData("""{"gatewayPayload":{"id":"provider-payload"}}""")]
    [InlineData("""{"secret":"hidden"}""")]
    [InlineData("""{"privateFileAccessToken":"grant-token"}""")]
    [InlineData("""{"storageKey":"campaigns/private/key.png"}""")]
    [InlineData("""{"requestBody":{"amount":100}}""")]
    [InlineData("""{"responseBody":{"status":"ok"}}""")]
    [InlineData("""{"stackTrace":"System.Exception: boom"}""")]
    public void AuditMetadataRules_RejectPhase5ForbiddenMetadataMarkers(string unsafeMetadata)
    {
        Assert.Throws<ArgumentException>(() => AuditMetadataRules.EnsureSafe(unsafeMetadata, nameof(unsafeMetadata)));
    }

    [Fact]
    public async Task Phase5CampaignAndWalletAuditMetadata_ExcludeSensitivePayloads()
    {
        await factory.InitializeDatabaseAsync();
        var company = await Phase5CampaignQueueTestHelpers.SeedApprovedCompanyAsync(factory.Services);
        var doctor = await Phase5CampaignQueueTestHelpers.SeedApprovedDoctorAsync(factory.Services, pricePerMessage: 50m, activityScore: 90m);
        var assetId = await Phase5CampaignQueueTestHelpers.SeedApprovedCampaignAssetAsync(factory.Services, company.CompanyId);
        await Phase5CampaignQueueTestHelpers.SeedCompanyWalletAsync(factory.Services, company.CompanyId, company.UserId, availableBalance: 1000m);

        using (var scope = factory.Services.CreateScope())
        {
            var campaignWorkflow = scope.ServiceProvider.GetRequiredService<ICampaignWorkflowService>();
            var walletService = scope.ServiceProvider.GetRequiredService<ICompanyWalletService>();

            await campaignWorkflow.SubmitCampaignAsync(company.UserId, $"idem-{Guid.NewGuid():N}", new CreateCampaignRequestDto
            {
                Title = "Phase 5 audit campaign",
                Description = "Phase 5 audit campaign description",
                ClinicalResearchInfo = "Phase 5 audit research context",
                AssetIds = [assetId],
                TargetDoctorIds = [doctor.DoctorId]
            });
            await walletService.TopUpCompanyWalletAsync(company.UserId, $"idem-{Guid.NewGuid():N}", new TopUpCompanyWalletRequestDto
            {
                Amount = 150m,
                Description = "Safe top-up reference"
            });
        }

        using var verificationScope = factory.Services.CreateScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var phase5AuditMetadata = await context.AuditEvents
            .Where(audit => audit.EventType.StartsWith("Phase5"))
            .Select(audit => audit.Metadata ?? string.Empty)
            .ToArrayAsync();
        var topUpMetadata = await context.WalletTransactions
            .Where(transaction => transaction.OperationType == WalletTransactionType.TopUp)
            .Select(transaction => transaction.Metadata ?? string.Empty)
            .ToArrayAsync();
        var serializedEvidence = string.Join(" ", phase5AuditMetadata.Concat(topUpMetadata));

        Assert.NotEmpty(phase5AuditMetadata);
        foreach (var marker in ForbiddenMetadataMarkers)
        {
            Assert.DoesNotContain(marker, serializedEvidence, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Phase5AuditRepository_RejectsUnsafeMetadataBeforePersistence()
    {
        await factory.InitializeDatabaseAsync();
        using var scope = factory.Services.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IDomainUnitOfWork>();

        await Assert.ThrowsAsync<ArgumentException>(() => unitOfWork.AuditEvents.AddPhase5AuditEventAsync(
            $"audit-{Guid.NewGuid():N}",
            "Phase5UnsafeProbe",
            actorUserId: "actor",
            actorRole: UserRole.Company.ToString(),
            targetType: AuditTargetType.Wallet,
            targetId: "wallet",
            outcome: AuditOutcome.Denied,
            reason: "Unsafe metadata probe.",
            correlationId: null,
            metadata: """{"storageKey":"private/key"}""",
            createdAtUtc: DateTime.UtcNow));
    }
}
