# Quickstart: Wallet and Campaign Workflow

## Prerequisites

- Apply existing Phase 2 identity/approval and Phase 3 domain migrations.
- Have one approved Admin account.
- Have one approved Pharmaceutical Company account.
- Have at least one approved Doctor account with no initial price, to verify pricing behavior.
- Run the API in an environment with JWT authentication enabled and the standard envelope/global exception middleware active.

## Smoke Workflow

1. Login as the approved company and admin.
2. Query the company wallet.
   - Expected: HTTP success envelope with one active company wallet, even if the wallet was missing before the request.
3. Start a mock top-up checkout for the company with a positive EGP amount and an idempotency key.
   - Expected: payment status `Succeeded`, internally generated transaction reference, wallet balance before/after, wallet transaction, ledger, and audit evidence.
   - Expected: no third-party redirect, webhook, callback endpoint, provider credential, or provider configuration.
4. Replay the same top-up request.
   - Expected: original payment/top-up result is returned and wallet balance is not credited again.
5. Replay the same idempotency key with a different amount.
   - Expected: conflict envelope and no wallet balance change.
6. As admin, try setting the approved doctor's price to null, zero, a negative value, and an over-precise amount.
   - Expected: validation failures.
7. As admin, set the approved doctor's price to a positive two-decimal EGP amount.
   - Expected: price history is recorded and the doctor becomes target-eligible.
8. As company, create an owned draft campaign.
   - Expected: campaign status `Draft`; no wallet reservation yet.
9. Upload a campaign media asset to the draft campaign.
   - Expected: asset is linked to the campaign with pending review status.
10. Request signed access for the stored asset.
    - Expected: response contains only `FileId`, a short-lived `Url`, and `ExpiresAtUtc`.
11. Upload a replacement for the pending asset.
    - Expected: replacement is accepted because the campaign is still draft and the old asset is retained for audit.
12. Delete a separate pending or rejected draft asset.
    - Expected: deletion succeeds only for pending/rejected draft assets; approved assets remain immutable.
13. Attempt to submit the campaign before asset approval.
    - Expected: validation failure requiring at least one approved campaign asset.
14. As admin, approve the campaign asset.
    - Expected: asset review history/status is updated.
15. As company, preview eligible targets and cost.
    - Expected: only approved, marketplace-eligible doctors with positive prices are included.
16. Submit the campaign for review.
    - Expected: target snapshots are captured, company funds are reserved, and campaign status becomes `PendingReview`.
17. As admin, approve the submitted campaign.
    - Expected: campaign status becomes `Approved` and exactly one queue row is created per eligible target doctor.
18. Retry the same admin campaign approval.
    - Expected: existing approval result is returned without duplicate review rows, wallet reservations, or queue rows.
19. As admin, inspect doctor-level queue rows for the campaign.
    - Expected: doctor-level queue rows are visible and ordered deterministically.
20. As company, view campaign queue summary.
    - Expected: aggregate queued/activated/cancelled/expired counts are visible for the owned campaign without doctor identifiers.

## Negative Validation Checks

- Company cannot access another company's wallet, campaigns, assets, submissions, or queue summary.
- Company cannot inspect doctor-level queue rows.
- Doctor cannot manage company wallets or campaigns.
- Admin campaign rejection releases uncharged reserved funds and creates no queue rows.
- Campaign submission with insufficient wallet funds fails before admin review.
- Campaign submission with no eligible priced doctors fails with an actionable message.
- Campaign asset upload to a non-owned campaign is denied.
- Campaign asset replacement or deletion is denied for approved assets and for non-draft campaigns.
- Top-up amount with more than two decimals is rejected rather than rounded.

## Verification Commands

```powershell
dotnet test tests\contract\MediBridge.ContractTests\MediBridge.ContractTests.csproj
dotnet test tests\integration\MediBridge.IntegrationTests\MediBridge.IntegrationTests.csproj
dotnet test tests\unit\MediBridge.UnitTests\MediBridge.UnitTests.csproj
```

## Done Criteria

- Full HTTP smoke workflow completes with no manual data seeding beyond approved account fixtures.
- All success, validation, unauthorized, not-found, and conflict responses use the standard envelope.
- No raw stack traces or payment-provider details are exposed.
- Wallet top-up produces mock payment, wallet transaction, wallet ledger, and audit evidence atomically.
- Campaign approval creates queue rows once and exposes queue verification according to role visibility rules.
