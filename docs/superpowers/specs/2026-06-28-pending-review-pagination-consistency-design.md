# Pending Review Pagination Consistency Design

## Problem

The admin pending-review repository counts active `PendingReview` campaigns before joining active company profiles, but it pages results after that join. Because `CompanyProfile` has a global soft-delete filter, a pending campaign owned by a soft-deleted company contributes to `totalCount` but cannot appear in `items`. This can produce inflated totals, premature empty pages, and inconsistent pagination metadata.

The existing performance test also seeds campaigns without the target and media rows evaluated by the page projection, so it does not exercise a representative review-ready dataset.

## Required Behavior

- A campaign is eligible for the admin pending-review list only when the campaign is active, has `PendingReview` status, has a non-null submission time, and has an active company profile available for identity projection.
- Soft-deleted company profiles and their data must not be exposed.
- `totalCount` and paged `items` must derive from the same eligibility query.
- Ordering remains `SubmittedAtUtc ASC, CampaignId ASC`.
- Paging remains bounded to 100 records.
- Target and media readiness facts remain SQL projections without per-campaign repository queries.
- Normal two-query pagination retains read-committed semantics; concurrent submissions or moderation decisions may change between requests, but the count and items use an identical predicate.

## Architecture

The fix remains entirely in the repository persistence layer. `CampaignRepository.ListPendingReviewCampaignsAsync` will construct one provider-side joined `IQueryable` representing eligible pending reviews. Both `CountAsync` and the ordered `Skip`/`Take` projection will consume that query. Core interfaces, service DTOs, service orchestration, controller contracts, and public response shapes remain unchanged.

## Data Flow

1. `AdminCampaignsController` authenticates the request and delegates paging inputs.
2. `AdminCampaignReviewService` validates the approved admin and bounds paging.
3. `CampaignRepository` filters active submitted pending campaigns.
4. The repository joins active company profiles; EF Core applies the profile soft-delete query filter.
5. The joined query supplies both `totalCount` and the ordered page projection.
6. The service maps the provider-neutral read model to the public DTO.

## Testing

- Add an endpoint-level integration regression test containing one eligible campaign and one pending campaign owned by a soft-deleted company. Before the fix, the test must fail because `totalCount` is larger than `items.Count`; after the fix, it must pass and must not expose the deleted company or campaign.
- Strengthen the warmed 20-item performance fixture by adding one target snapshot and one active approved campaign-media row per campaign in the same seed operation.
- Run the pending-list integration tests, all Phase 3 contract/integration tests, the solution build, and broader regression tests.

## Security and Performance

The canonical inner join preserves soft-delete confidentiality. It adds the same indexed company-profile primary-key lookup to the count query that already exists in the item query, trading a small and bounded count-query cost for correct pagination. The campaign status/submission index and company-profile primary key continue to support the access path. No new database schema or migration is required.

## Non-Goals

- Exposing soft-deleted company identities.
- Creating placeholder company names.
- Changing the public API contract or pagination model.
- Introducing keyset pagination or snapshot transactions.
- Refactoring unrelated campaign moderation behavior.
