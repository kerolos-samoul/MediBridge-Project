using MediBridge.Core.Entities.Profiles;

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
    Task<DoctorProfile?> FindDoctorProfileByIdForUpdateAsync(string doctorId, CancellationToken cancellationToken = default);
    Task<CompanyProfile?> FindCompanyProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<CompanyProfile?> FindCompanyProfileByIdAsync(string companyId, CancellationToken cancellationToken = default);
    Task<CompanyProfile?> FindCompanyProfileByIdForUpdateAsync(string companyId, CancellationToken cancellationToken = default);
    Task<bool> CompanyLicenseExistsAsync(string licenseNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorProfile>> SearchEligibleDoctorsAsync(EligibleDoctorSearchCriteria criteria, int skip, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DoctorProfile>> ListEligibleDoctorsByIdsAsync(IReadOnlyCollection<string> doctorIds, CancellationToken cancellationToken = default);
    Task<int> CountEligibleDoctorsAsync(EligibleDoctorSearchCriteria criteria, CancellationToken cancellationToken = default);
}
