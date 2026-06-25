using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Identity;

public interface IContactVerificationFlowRepository
{
    Task AddAsync(ContactVerificationFlow flow, CancellationToken cancellationToken = default);
    Task<ContactVerificationFlow?> FindUnconsumedByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<ContactVerificationFlow?> FindLatestActiveForUpdateAsync(string userId, ContactVerificationChannel channel, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task SupersedeActiveAsync(string userId, ContactVerificationChannel channel, DateTime supersededAtUtc, CancellationToken cancellationToken = default);
    Task RecordFailedAttemptAsync(ContactVerificationFlow flow, DateTime attemptedAtUtc, CancellationToken cancellationToken = default);
    Task MarkSentAsync(ContactVerificationFlow flow, DateTime sentAtUtc, CancellationToken cancellationToken = default);
    Task MarkConsumedAsync(ContactVerificationFlow flow, DateTime consumedAtUtc, CancellationToken cancellationToken = default);
}
