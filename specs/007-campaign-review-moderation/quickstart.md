# Quickstart: Phase 6 Campaign Review & Moderation

## Prerequisites

- Apply existing identity, campaign, file, target, and queue migrations.
- Have one approved Admin account.
- Have one approved Pharmaceutical Company account.
- Have one submitted `PendingReview` campaign owned by the company.
- The submitted campaign has campaign text, at least one target snapshot, an explicit `SubmittedAtUtc`, and at least one active Pending or Approved campaign media asset.
- The company wallet has enough available balance for submission affordability validation, but submission has not reserved or moved any funds.
- Run the API with JWT authentication, role authorization, standard envelope responses, and global exception middleware enabled.

## Implemented Endpoints

- `PUT /api/company/campaigns/{campaignId}` returns the updated `Campaign` for an owned `Draft` or `RevisionRequired` campaign.
- `POST /api/company/campaigns/{campaignId}/submit` validates affordability without reserving funds and returns `CampaignSubmission` with `estimatedCost`, `currency`, and `submittedAtUtc`.
- `GET /api/admin/campaigns/pending-review` returns a bounded `PendingCampaignPage` ordered by `submittedAtUtc`, then campaign identifier.
- `GET /api/admin/campaigns/{campaignId}/review-detail` returns `CampaignReviewDetail` with short-lived signed file access and a storage-unavailable `503` response.
- `POST /api/admin/campaigns/{campaignId}/review` records an idempotent canonical `Approved`, `Rejected`, or `RevisionRequired` decision.
- `GET /api/admin/campaigns/{campaignId}/queue` returns doctor-level queue rows with distinct campaign-submission and queue timestamps.
- `GET /api/company/campaigns/{campaignId}/review-outcome` returns the owning company's public `CompanyReviewOutcome` without internal notes.

## Smoke Workflow

1. Login as the approved admin and the owning company.
2. As admin, list pending campaigns.
   - Expected: the submitted campaign appears in `SubmittedAtUtc`, then campaign-id order; draft, approved, rejected, revision-required, cancelled, completed, and deleted campaigns are excluded.
   - Expected: after one warm-up request, a normal 20-item integration-fixture page returns within 1 second.
3. As admin, open the campaign review detail.
   - Expected: response includes company identity, campaign text, active Pending/Approved media, target summary, submitted time, readiness issues, and optional submitted materials when present.
   - Expected: every reviewable file contains a short-lived `accessUrl` and `accessExpiresAtUtc`; opening the URL as admin succeeds before expiry.
   - Expected: raw storage keys, raw storage locations, provider credentials, and internal-only company data are not returned.
4. If all campaign media is still Pending, try approving the campaign.
   - Expected: approval fails with an actionable message requiring at least one separately Approved campaign media asset; campaign and wallet state remain unchanged.
5. Approve at least one media asset through the existing admin asset-review endpoint.
6. As admin, approve the campaign using an idempotency key.
   - Expected: campaign status becomes `Approved`, one review-history row is appended, and one pending queue row is created per submitted target doctor.
   - Expected: each queue row copies the campaign `SubmittedAtUtc` into `CampaignSubmittedAtUtc` and records approval time separately as `QueuedAtUtc`.
   - Expected: no wallet balance, wallet transaction, wallet ledger, delivery, payout, or settlement record changes.
7. Replay the same approval with the same idempotency key and identical decision payload.
   - Expected: original approval result is returned without duplicate review history or queue rows.
8. As admin, inspect queue rows for the approved campaign.
   - Expected: rows are ordered by submitted campaign time, queue creation time, then stable row identifier.
9. As company, view the campaign review outcome.
   - Expected: status, public decision, decision time, resubmission eligibility, and queue count are visible only for the owned campaign.

## Rejection Workflow

1. Prepare a second submitted `PendingReview` campaign with an approved media asset and target snapshot.
2. As admin, reject the campaign with a public-facing reason.
   - Expected: campaign status becomes `Rejected`, review history is appended, no queue rows are created, no wallet reservation/release occurs, and the reason is visible to the owning company.
3. As company, attempt to edit or resubmit the rejected campaign.
   - Expected: request is denied; status and review history remain unchanged.

## Revision Workflow

1. Prepare a third submitted `PendingReview` campaign with an approved media asset and target snapshot.
2. As admin, return the campaign for revision with a public-facing reason.
   - Expected: campaign status becomes `RevisionRequired`, review history is appended, no queue rows or wallet effects are created, and the reason is visible to the owning company.
3. As the owning company, call `PUT /api/company/campaigns/{campaignId}` with corrected title, description, and optional clinical research information.
   - Expected: update succeeds only in `RevisionRequired`; `SubmittedAtUtc` remains unchanged until resubmission.
4. As the owning company, upload, replace, or delete Pending/Rejected campaign assets if the revision requires it.
   - Expected: asset operations allow `RevisionRequired`; Approved assets remain immutable.
5. Resubmit the revised campaign with a new idempotency key.
   - Expected: active target snapshots are replaced, wallet affordability is validated, one append-only submission-attempt row is stored, a new `SubmittedAtUtc` is recorded, and campaign returns to `PendingReview` while prior review history remains available.
   - Expected: available/reserved balances and wallet transaction/ledger counts remain unchanged.
6. Replay the same resubmission key while the campaign remains PendingReview.
   - Expected: the stored submission result is returned without replacing targets or adding submission/audit rows.
7. Submit a different key while the campaign remains PendingReview, or reuse a key from an older review attempt.
   - Expected: conflict envelope and no mutation.

## Negative Validation Checks

- Approval fails when the campaign has no campaign text.
- Approval fails when the campaign has no target snapshot.
- Initial submission and revision resubmission fail when there is no active Pending/Approved campaign media asset.
- Submission with only an active Pending media asset succeeds, but campaign approval fails until at least one active media asset is separately Approved.
- Campaign approval does not approve pending or rejected media assets.
- Rejection and revision-required decisions fail without a reason.
- Non-admin users cannot list pending campaigns, open admin review details, or make review decisions.
- A company cannot view another company's review outcome.
- A stale or concurrent conflicting decision preserves a single final campaign outcome and returns the current state or conflict envelope.
- A replay with the same idempotency key but different decision or reason is rejected without changing campaign status or queue rows.
- Company content updates and asset operations fail for PendingReview, Rejected, Approved, Active, Paused, Completed, Cancelled, deleted, and non-owned campaigns.
- Submission, revision resubmission, and moderation decisions do not create wallet transactions, wallet ledger entries, reservations, releases, charges, doctor earnings, deliveries, payouts, or expiry events.

## Verification Commands

```powershell
dotnet test tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj
dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj
dotnet test tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj
```

## Done Criteria

- Admin pending list is deterministic and meets the warmed 20-item page performance goal.
- Admin review detail exposes the complete review package with working short-lived signed access and without leaking protected storage details.
- Approved campaigns create exactly one pending queue row per submitted target doctor and no duplicates on replay.
- Rejected campaigns are final and cannot be edited or resubmitted.
- Revision-required campaigns can be edited and resubmitted while preserving prior review history.
- All success, validation, unauthorized, not-found, and conflict responses use the standard envelope.
- No wallet, delivery, expiry, settlement, or payout side effects occur during submission, revision resubmission, or moderation.
