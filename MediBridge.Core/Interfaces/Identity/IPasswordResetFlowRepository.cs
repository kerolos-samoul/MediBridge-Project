using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Interfaces.Identity;

public interface IPasswordResetFlowRepository
{
    Task AddAsync(PasswordResetFlow flow, CancellationToken cancellationToken = default);
    Task<PasswordResetFlow?> FindUnconsumedByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task MarkConsumedAsync(PasswordResetFlow flow, DateTime consumedAtUtc, CancellationToken cancellationToken = default);
}
