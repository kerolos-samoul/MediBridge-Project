using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Identity;

public interface IContactVerificationFlowRepository
{
    Task AddAsync(ContactVerificationFlow flow, CancellationToken cancellationToken = default);
    Task<ContactVerificationFlow?> FindUnconsumedByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContactVerificationFlow>> ListUnconsumedByUserDestinationAsync(
        string userId,
        ContactVerificationChannel channel,
        string destinationHash,
        CancellationToken cancellationToken = default);
    Task MarkConsumedAsync(ContactVerificationFlow flow, DateTime consumedAtUtc, CancellationToken cancellationToken = default);
}
