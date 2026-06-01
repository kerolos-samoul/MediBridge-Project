using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Interfaces.Identity;

public interface IAccountResubmissionTokenRepository
{
    Task AddAsync(AccountResubmissionToken token, CancellationToken cancellationToken = default);
    Task<AccountResubmissionToken?> FindUnconsumedByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<AccountResubmissionToken?> FindUnconsumedByTokenHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task MarkConsumedAsync(AccountResubmissionToken token, DateTime consumedAtUtc, CancellationToken cancellationToken = default);
}
