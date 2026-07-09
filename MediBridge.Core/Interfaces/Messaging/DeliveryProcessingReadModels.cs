using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Messaging;

public sealed record QueuedDoctorCursor(string DoctorId);

public sealed record QueuedDoctorReadModel(string DoctorId);

public sealed record QueuedCandidateCursor(DateTime? CampaignSubmittedAtUtc, string Id);

public sealed record DeliveryQueueCandidateReadModel(
    string Id,
    string DoctorId,
    string CampaignId,
    DateTime? CampaignSubmittedAtUtc);

public sealed record LockedDoctorDeliveryEligibilityReadModel(
    string DoctorId,
    string UserId,
    UserRole UserRole,
    AccountStatus AccountStatus,
    bool UserIsDeleted,
    bool ProfileIsDeleted,
    DoctorMarketplaceStatus MarketplaceStatus,
    decimal? PricePerMessage,
    int DailyMessageLimit);

public sealed record LockedCampaignCompanyEligibilityReadModel(
    string CampaignId,
    string CompanyId,
    CampaignStatus CampaignStatus,
    bool CampaignIsDeleted,
    bool CompanyProfileIsDeleted,
    UserRole CompanyUserRole,
    AccountStatus CompanyAccountStatus,
    bool CompanyUserIsDeleted);

public sealed record OverdueDeliveryCursor(DateOnly DeliveryDateEgypt, DateTime CreatedAtUtc, string Id);

public sealed record OverdueDeliveryReadModel(string Id, DateOnly DeliveryDateEgypt, DateTime CreatedAtUtc);

public sealed record EffectivePlatformFeePolicyReadModel(string Id, decimal FeePercent);

public sealed record DeliveryReservationReplayReadModel(
    string Id,
    string DoctorId,
    string CampaignId,
    string CompanyId,
    DateOnly DeliveryDateEgypt,
    DeliveryStatus Status,
    ReservationStatus ReservationStatus,
    decimal ReservedAmount);

public sealed record TodayDeliveryCursor(DateTime DeliveredAtUtc, string Id);

public sealed record TodayDeliveryReadModel(
    string DeliveryId,
    string DoctorId,
    string CampaignId,
    DeliveryStatus Status,
    DateOnly DeliveryDateEgypt,
    DateTime DeliveredAtUtc,
    string Title,
    string Description,
    string? ClinicalResearchInfo);

public sealed record ApprovedDeliveryAssetReadModel(
    string FileId,
    string CampaignId,
    StoredFilePurpose Purpose,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    StoredFileReviewStatus ReviewStatus);

public sealed record DeliveryAssetAuthorizationReadModel(
    string DeliveryId,
    string DoctorId,
    DateOnly DeliveryDateEgypt,
    string CampaignId,
    string FileId,
    string StorageKey,
    string StorageProvider,
    StoredFileStorageResourceType StorageResourceType,
    StoredFileStorageDeliveryType StorageDeliveryType);
