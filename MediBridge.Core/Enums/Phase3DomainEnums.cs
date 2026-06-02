namespace MediBridge.Core.Enums;

public enum DoctorMarketplaceStatus
{
    Active = 1,
    Warned = 2,
    Suspended = 3
}

public enum CampaignStatus
{
    Draft = 1,
    PendingReview = 2,
    Approved = 3,
    Rejected = 4,
    Active = 5,
    Paused = 6,
    Completed = 7,
    Cancelled = 8
}

public enum CampaignReviewDecision
{
    Approved = 1,
    Rejected = 2,
    ChangesRequested = 3
}

public enum QueueItemStatus
{
    Queued = 1,
    Activated = 2,
    Cancelled = 3
}

public enum DeliveryStatus
{
    Active = 1,
    Accepted = 2,
    Rejected = 3,
    Expired = 4
}

public enum ReservationStatus
{
    Reserved = 1,
    Released = 2,
    Charged = 3
}

public enum FeedbackQualityStatus
{
    Pending = 1,
    Accepted = 2,
    Flagged = 3
}

public enum WalletOwnerType
{
    Doctor = 1,
    Company = 2,
    Platform = 3
}

public enum WalletTransactionType
{
    TopUp = 1,
    Reserve = 10,
    Release = 11,
    Charge = 20,
    Earn = 21,
    Refund = 22,
    WithdrawRequest = 30,
    WithdrawApproved = 31,
    WithdrawRejected = 32,
    WithdrawPayout = 33
}

public enum WalletLedgerEntryDirection
{
    Debit = 1,
    Credit = 2
}

public enum WalletBalanceType
{
    Available = 1,
    Reserved = 2
}

public enum WithdrawalRequestStatus
{
    Requested = 1,
    Approved = 2,
    Rejected = 3,
    Paid = 4,
    Failed = 5
}

public enum StoredFileOwnerType
{
    Doctor = 1,
    Company = 2,
    Admin = 3,
    Campaign = 4
}

public enum StoredFilePurpose
{
    VerificationDocument = 1,
    CampaignMedia = 2,
    VoiceNote = 3,
    ClinicalResearchAttachment = 4
}

public enum StoredFileVisibility
{
    Private = 1,
    Protected = 2,
    Public = 3
}

public enum StoredFileReviewStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public enum AuditOutcome
{
    Success = 1,
    Denied = 2,
    Info = 3
}

public enum AuditTargetType
{
    User = 1,
    Doctor = 2,
    Company = 3,
    Campaign = 4,
    Delivery = 5,
    Wallet = 6,
    WalletTransaction = 7,
    WithdrawalRequest = 8,
    StoredFile = 9,
    Policy = 10,
    AuditEvent = 11
}
