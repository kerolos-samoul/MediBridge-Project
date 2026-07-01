namespace MediBridge.ContractTests;

public static class WalletCampaignWorkflowRoutes
{
    public const string CompanyWallet = "/api/company/wallet";
    public const string CompanyWalletMockCheckout = "/api/company/wallet/mock-checkout";
    public const string CompanyCampaigns = "/api/company/campaigns";
    public const string CompanyCampaignUpdate = "/api/company/campaigns/{campaignId}";
    public const string CompanyCampaignAssets = "/api/company/campaigns/{campaignId}/assets";
    public const string CompanyCampaignTargetPreview = "/api/company/campaigns/{campaignId}/target-preview";
    public const string CompanyCampaignSubmit = "/api/company/campaigns/{campaignId}/submit";
    public const string CompanyCampaignQueueSummary = "/api/company/campaigns/{campaignId}/queue-summary";
    public const string CompanyCampaignReviewOutcome = "/api/company/campaigns/{campaignId}/review-outcome";
    public const string AdminDoctorPrice = "/api/admin/doctors/{doctorId}/price";
    public const string AdminCampaignAssetReview = "/api/admin/campaign-assets/{assetId}/review";
    public const string AdminPendingCampaignReviews = "/api/admin/campaigns/pending-review";
    public const string AdminCampaignReviewDetail = "/api/admin/campaigns/{campaignId}/review-detail";
    public const string AdminCampaignReview = "/api/admin/campaigns/{campaignId}/review";
    public const string AdminCampaignQueue = "/api/admin/campaigns/{campaignId}/queue";
}
