using MediBridge.Core.Entities.Profiles;
using MediBridge.Core.Interfaces.Messaging;

namespace MediBridge.Core.Interfaces.Identity;

public sealed record EligibleDoctorSearchCriteria(
    string? Specialization,
    int? MinExperienceYears,
    int? MaxExperienceYears,
    string? Location,
    decimal? MinActivityScore,
    decimal? MinPrice,
    decimal? MaxPrice);

public interface IProfileRepository
{
    Task AddDoctorProfileAsync(DoctorProfile profile, CancellationToken cancellationToken = default);
    Task AddCompanyProfileAsync(CompanyProfile profile, CancellationToken cancellationToken = default);
    Task<DoctorProfile?> FindDoctorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<DoctorProfile?> FindDoctorProfileByIdAsync(string doctorId, CancellationToken cancellationToken = default);
    Task<DoctorProfile?> FindDoctorProfileByIdForUpdateAsync(string doctorId, CancellationToken cancellationToken = default);
    Task<CompanyProfile?> FindCompanyProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<CompanyProfile?> FindCompanyProfileByIdAsync(string companyId, CancellationToken cancellationToken = default);
    Task<CompanyProfile?> FindCompanyProfileByIdForUpdateAsync(string companyId, CancellationToken cancellationToken = default);
    Task<bool> CompanyLicenseExistsAsync(string licenseNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorProfile>> SearchEligibleDoctorsAsync(EligibleDoctorSearchCriteria criteria, int skip, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorProfile>> ListEligibleDoctorsByIdsAsync(IReadOnlyCollection<string> doctorIds, CancellationToken cancellationToken = default);
    Task<int> CountEligibleDoctorsAsync(EligibleDoctorSearchCriteria criteria, CancellationToken cancellationToken = default);
    Task<LockedDoctorDeliveryEligibilityReadModel?> FindDoctorDeliveryEligibilityForUpdateAsync(string doctorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListApprovedNonDeletedDoctorIdsForActivityScoringAsync(string? afterDoctorId, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 activity scoring candidates are not available.");
    Task<IReadOnlyList<string>> ListApprovedNonDeletedDoctorIdsForWeeklyEnforcementAsync(string? afterDoctorId, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 weekly enforcement candidates are not available.");
    Task<DoctorProfile?> FindDoctorProfileForEnforcementUpdateByIdAsync(string doctorId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 enforcement locking is not available.");
    Task<IReadOnlyList<DoctorProfile>> ListExpiredSuspendedDoctorsForUpdateAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 suspension expiry candidates are not available.");
    Task<bool> ApplyCurrentActivityScoreAsync(string doctorId, decimal activityScore, DateTime calculatedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 score updates are not available.");
    Task ApplyEnforcementStateChangesAsync(DoctorProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 enforcement state changes are not available.");
    Task<bool> SuspensionOverlapsEgyptWeekAsync(string doctorId, DateOnly weekStartDateEgypt, DateOnly weekEndDateEgypt, CancellationToken cancellationToken = default) => throw new NotSupportedException("Phase 9 suspension overlap checks are not available.");
}
