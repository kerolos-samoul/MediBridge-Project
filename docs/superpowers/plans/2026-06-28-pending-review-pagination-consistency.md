# Pending Review Pagination Consistency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make pending-review pagination count and page items use one eligibility predicate while exercising the full projection in performance coverage.

**Architecture:** Keep the public API and Core contract unchanged. Correct the SQL Server EF Core query inside `CampaignRepository`, then prove behavior through the existing endpoint-level integration suite.

**Tech Stack:** C# 12, .NET 8, ASP.NET Core, EF Core SQL Server, xUnit

---

### Task 1: Reproduce the eligibility mismatch

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminPendingCampaignListTests.cs`

- [x] **Step 1: Add a regression test**

Add `using Microsoft.EntityFrameworkCore;` and this test:

```csharp
[Fact]
public async Task AdminPendingCampaignList_UsesSameEligibilityForTotalAndItems()
{
    await using var factory = new WebAppFactory();
    await factory.InitializeDatabaseAsync();
    var activeActors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
    var deletedActors = await WalletCampaignWorkflowTestHelpers.CreateApprovedWorkflowActorsAsync(factory);
    var submittedAtUtc = new DateTime(2026, 6, 26, 9, 0, 0, DateTimeKind.Utc);
    var visibleCampaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
        factory, activeActors.CompanyProfileId, submittedAtUtc);
    var hiddenCampaign = await WalletCampaignWorkflowTestHelpers.CreatePendingReviewCampaignAsync(
        factory, deletedActors.CompanyProfileId, submittedAtUtc.AddMinutes(1));

    using (var scope = factory.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        var deletedCompany = await context.CompanyProfiles
            .IgnoreQueryFilters()
            .SingleAsync(company => company.Id == deletedActors.CompanyProfileId);
        deletedCompany.IsDeleted = true;
        deletedCompany.DeletedAtUtc = submittedAtUtc.AddMinutes(2);
        await context.SaveChangesAsync();
    }

    using var client = WalletCampaignWorkflowTestHelpers.CreateAdminClient(factory, activeActors.AdminUserId);
    using var response = await client.GetAsync("/api/admin/campaigns/pending-review?PageNumber=1&PageSize=20");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var data = document.RootElement.GetProperty("Data");
    Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
    var item = Assert.Single(data.GetProperty("items").EnumerateArray());
    Assert.Equal(visibleCampaign.CampaignId, item.GetProperty("campaignId").GetString());
    Assert.DoesNotContain(hiddenCampaign.CampaignId, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
}
```

- [x] **Step 2: Run the regression test and verify RED**

Run:

```powershell
dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "AdminPendingCampaignList_UsesSameEligibilityForTotalAndItems"
```

Expected: FAIL because the current count includes the soft-deleted-company campaign while the item join excludes it.

### Task 2: Canonicalize the repository eligibility query

**Files:**
- Modify: `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`
- Test: `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminPendingCampaignListTests.cs`

- [x] **Step 1: Build one joined query**

Replace the campaign-only `pendingCampaigns` query with `eligiblePendingCampaigns`, joining `context.Campaigns.AsNoTracking()` to `context.CompanyProfiles.AsNoTracking()` after applying active `PendingReview` and non-null `SubmittedAtUtc` filters.

```csharp
var eligiblePendingCampaigns = context.Campaigns
    .AsNoTracking()
    .Where(campaign => campaign.Status == CampaignStatus.PendingReview
        && campaign.SubmittedAtUtc != null
        && !campaign.IsDeleted)
    .Join(
        context.CompanyProfiles.AsNoTracking(),
        campaign => campaign.CompanyId,
        company => company.Id,
        (campaign, company) => new { Campaign = campaign, Company = company });
```

- [x] **Step 2: Use the canonical query for count and page**

Call `CountAsync` on `eligiblePendingCampaigns`, then order, page, and project from the same query. Keep the existing target/media correlated projections and stable ordering.

```csharp
var totalCount = await eligiblePendingCampaigns.CountAsync(cancellationToken);
var items = await eligiblePendingCampaigns
    .OrderBy(row => row.Campaign.SubmittedAtUtc)
    .ThenBy(row => row.Campaign.Id)
    .Skip(boundedSkip)
    .Take(boundedTake)
    .Select(row => new PendingCampaignReviewReadModel(
        row.Campaign.Id,
        row.Campaign.CompanyId,
        row.Company.CompanyName,
        row.Campaign.Title,
        row.Campaign.Description,
        row.Campaign.Status,
        row.Campaign.SubmittedAtUtc!.Value,
        context.CampaignTargets.Count(target => target.CampaignId == row.Campaign.Id),
        context.StoredFiles.Any(file => file.OwnerType == StoredFileOwnerType.Campaign
            && file.OwnerId == row.Campaign.Id
            && file.Purpose == StoredFilePurpose.CampaignMedia
            && file.StorageState == StorageObjectState.Active
            && file.DeletedAtUtc == null
            && file.SupersededByFileId == null
            && (file.ReviewStatus == StoredFileReviewStatus.Pending
                || file.ReviewStatus == StoredFileReviewStatus.Approved)),
        context.StoredFiles.Any(file => file.OwnerType == StoredFileOwnerType.Campaign
            && file.OwnerId == row.Campaign.Id
            && file.Purpose == StoredFilePurpose.CampaignMedia
            && file.StorageState == StorageObjectState.Active
            && file.DeletedAtUtc == null
            && file.SupersededByFileId == null
            && file.ReviewStatus == StoredFileReviewStatus.Approved)))
    .ToListAsync(cancellationToken);
```

- [x] **Step 3: Run the regression test and verify GREEN**

Run the Task 1 command.

Expected: PASS with one visible item and `totalCount = 1`.

### Task 3: Strengthen the performance fixture

**Files:**
- Modify: `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminPendingCampaignListTests.cs`

- [x] **Step 1: Seed realistic related rows**

Update `SeedPendingPageAsync` to accept a doctor id and add one `CampaignTarget` plus one active approved `StoredFile` with `Purpose = CampaignMedia` for every pending campaign before the single `SaveChangesAsync` call.

```csharp
private static async Task SeedPendingPageAsync(WebAppFactory factory, string companyId, string doctorId, int count)
{
    using var scope = factory.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
    var submittedAtUtc = new DateTime(2026, 6, 26, 10, 0, 0, DateTimeKind.Utc);
    for (var index = 0; index < count; index++)
    {
        var campaign = CreateCampaign(companyId, CampaignStatus.PendingReview, submittedAtUtc.AddMinutes(index));
        await context.Campaigns.AddAsync(campaign);
        await context.CampaignTargets.AddAsync(new CampaignTarget
        {
            CampaignId = campaign.Id,
            DoctorId = doctorId,
            SpecializationSnapshot = "Cardiology",
            ExperienceYearsSnapshot = 8,
            LocationSnapshot = "Cairo",
            ActivityScoreSnapshot = 90m,
            PricePerMessageSnapshot = 50m,
            CreatedAtUtc = campaign.SubmittedAtUtc!.Value
        });
        await context.StoredFiles.AddAsync(new StoredFile
        {
            OwnerType = StoredFileOwnerType.Campaign,
            OwnerId = campaign.Id,
            Purpose = StoredFilePurpose.CampaignMedia,
            OriginalFileName = $"performance-{index}.png",
            ContentType = "image/png",
            SizeBytes = 1024,
            StorageKey = $"integration/performance/{campaign.Id}.png",
            StorageState = StorageObjectState.Active,
            Visibility = StoredFileVisibility.Private,
            ReviewStatus = StoredFileReviewStatus.Approved,
            CreatedAtUtc = campaign.SubmittedAtUtc.Value
        });
    }

    await context.SaveChangesAsync();
}
```

Update the caller:

```csharp
await SeedPendingPageAsync(factory, actors.CompanyProfileId, actors.DoctorProfileId, count: 20);
```

- [x] **Step 2: Run all pending-list tests**

```powershell
dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "AdminPendingCampaignList"
```

Expected: all pending-list tests pass, including the warmed one-second assertion.

### Task 4: Verify scope and regressions

**Files:**
- Verify: `MediBridge.Repository/Repositories/Campaigns/CampaignRepository.cs`
- Verify: `tests/integration/MediBridge.IntegrationTests/Campaigns/AdminPendingCampaignListTests.cs`

- [x] **Step 1: Build the solution**

```powershell
dotnet build MediBridge.slnx --no-restore
```

Expected: 0 warnings and 0 errors.

- [x] **Step 2: Run Phase 3 contract tests**

```powershell
dotnet test tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj --filter "AdminPendingCampaignReview|AdminCampaignReviewDetail" --no-build
```

Expected: all focused contract tests pass.

- [x] **Step 3: Run Phase 3 integration tests**

```powershell
dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj --filter "AdminPendingCampaignList|AdminCampaignReviewDetail|AdminCampaignReviewReadiness" --no-build
```

Expected: all focused integration tests pass.

- [x] **Step 4: Run broader regression tests**

```powershell
dotnet test MediBridge.slnx --no-build
```

Expected: identify whether any failures are caused by this change; report pre-existing or out-of-scope failures separately with evidence.
