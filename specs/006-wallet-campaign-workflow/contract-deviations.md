# 006 Contract Deviations

## Campaign creation routes

The original 006 API specification assigned `POST /api/company/campaigns` to draft creation. A later legacy implementation reassigned that route to complete campaign creation and submission, but its required unbound `assetIds` could not be produced by any public upload workflow. That direct-submission operation is removed from the public API.

The supported campaign workflow is explicit:

| Route | Behavior |
| --- | --- |
| `POST /api/company/campaigns/drafts` | Creates an owned campaign with `Draft` status. |
| `POST /api/campaigns/{campaignId}/files` | Uploads and binds campaign media to the owned draft. |
| `POST /api/company/campaigns/{campaignId}/submit` | Submits the populated draft for review. |

`POST /api/company/campaigns` is no longer registered and is absent from generated OpenAPI. Clients must use the three-step workflow above.
