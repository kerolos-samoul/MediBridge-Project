namespace MediBridge.Core.Interfaces;

public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    string? UserId { get; }

    string? Role { get; }

    bool? IsApproved { get; }

    string? CorrelationId { get; }
}
