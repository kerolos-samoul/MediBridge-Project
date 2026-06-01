using MediBridge.Core.Entities.Identity;

namespace MediBridge.Core.Interfaces.Identity;

public interface IAuthenticationAuditEventRepository
{
    Task AddAsync(AuthenticationAuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuthenticationAuditEvent>> ListByTargetUserIdAsync(string targetUserId, CancellationToken cancellationToken = default);
}
