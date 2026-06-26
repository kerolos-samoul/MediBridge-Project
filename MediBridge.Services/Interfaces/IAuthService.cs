using MediBridge.Services.DTOs.Auth;

namespace MediBridge.Services.Interfaces;

public interface IAuthService
{
    Task<RegistrationResultDto> RegisterDoctorAsync(RegisterDoctorRequestDto request, CancellationToken cancellationToken = default);
    Task<RegistrationResultDto> RegisterCompanyAsync(RegisterCompanyRequestDto request, CancellationToken cancellationToken = default);
    Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<AuthResultDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default);
    Task LogoutAsync(string userId, RefreshRequestDto request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequestDto request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default);
    Task VerifyContactAsync(VerifyContactRequestDto request, CancellationToken cancellationToken = default);
    Task RequestContactVerificationAsync(RequestContactVerificationDto request, CancellationToken cancellationToken = default);
    Task<RegistrationResultDto> ResubmitRegistrationAsync(ResubmissionRequestDto request, CancellationToken cancellationToken = default);
}
