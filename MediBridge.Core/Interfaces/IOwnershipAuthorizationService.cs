namespace MediBridge.Core.Interfaces;

public interface IOwnershipAuthorizationService
{
    bool IsOwnerOrAdmin(OwnershipRequirement requirement, ICurrentUserContext currentUser);
}

public sealed record OwnershipRequirement(
    string ResourceType,
    string OwnerType,
    string OwnerId,
    string? RequiredRole = null);