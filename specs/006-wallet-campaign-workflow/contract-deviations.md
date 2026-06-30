# 006 Contract Deviations

## Campaign creation routes

The original 006 API specification assigned `POST /api/company/campaigns` to draft creation. Current development already assigns that route to complete campaign creation and submission, including asset, target, wallet, and idempotency validation.

To preserve backward compatibility, 006 draft creation is exposed additively:

| Route | Behavior |
| --- | --- |
| `POST /api/company/campaigns` | Creates and submits a complete campaign with `PendingReview` status. |
| `POST /api/company/campaigns/drafts` | Creates an owned campaign with `Draft` status. |

Both routes remain company-authorized and return the standard API envelope. Each route retains validation appropriate to its distinct request contract.
