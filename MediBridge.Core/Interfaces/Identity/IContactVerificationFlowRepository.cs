using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Interfaces.Identity;

public interface IContactVerificationFlowRepository
{
    Task AddAsync(ContactVerificationFlow flow, CancellationToken cancellationToken = default);
    Task<ContactVerificationFlow?> FindUnconsumedByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task MarkConsumedAsync(ContactVerificationFlow flow, DateTime consumedAtUtc, CancellationToken cancellationToken = default);
}
