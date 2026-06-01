using MediBridge.Core.Entities.Profiles;

namespace MediBridge.Core.Interfaces.Identity;

public interface IProfileRepository
{
    Task AddDoctorProfileAsync(DoctorProfile profile, CancellationToken cancellationToken = default);
    Task AddCompanyProfileAsync(CompanyProfile profile, CancellationToken cancellationToken = default);
    Task<DoctorProfile?> FindDoctorProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<CompanyProfile?> FindCompanyProfileByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> CompanyLicenseExistsAsync(string licenseNumber, CancellationToken cancellationToken = default);
}
