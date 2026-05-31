using MediBridge.Core.Interfaces;

namespace MediBridge.APIs.Security;

public sealed class OwnershipAuthorizationService : IOwnershipAuthorizationService
{
    private const string AdminRole = "Admin";

    public bool IsOwnerOrAdmin(OwnershipRequirement requirement, ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated)
        {
            return false;
        }

        if (string.Equals(currentUser.Role, AdminRole, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(requirement.RequiredRole) &&
            !string.Equals(currentUser.Role, requirement.RequiredRole, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(currentUser.Role, requirement.OwnerType, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(currentUser.UserId, requirement.OwnerId, StringComparison.Ordinal);
    }
}
