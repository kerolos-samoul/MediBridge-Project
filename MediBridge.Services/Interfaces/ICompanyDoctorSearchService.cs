using MediBridge.Services.DTOs.Doctors;

namespace MediBridge.Services.Interfaces;

public interface ICompanyDoctorSearchService
{
    Task<EligibleDoctorPageDto> SearchEligibleDoctorsAsync(string actorUserId, EligibleDoctorSearchRequestDto request, CancellationToken cancellationToken = default);
}
