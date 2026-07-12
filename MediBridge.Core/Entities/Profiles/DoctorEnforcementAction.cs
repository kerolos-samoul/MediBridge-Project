using MediBridge.Core.Enums;

namespace MediBridge.Core.Entities.Profiles;

public sealed class DoctorEnforcementAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DoctorId { get; set; } = string.Empty;
    public string? ActorAdminUserId { get; set; }
    public DoctorEnforcementActionType ActionType { get; set; }
    public string? Reason { get; set; }
    public DoctorMarketplaceStatus PreviousStatus { get; set; }
    public DoctorMarketplaceStatus NewStatus { get; set; }
    public int PreviousDailyMessageLimit { get; set; }
    public int? NewDailyMessageLimit { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }
    public DateTime? SuspendedUntilUtc { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public string? CorrelationId { get; set; }
    public string? AuditEventId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DoctorId))
        {
            throw new InvalidOperationException("Enforcement actions require a doctor id.");
        }

        if (EffectiveAtUtc.Kind != DateTimeKind.Utc || CreatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException("Enforcement action timestamps must be UTC.");
        }

        if (PreviousDailyMessageLimit < 0 || NewDailyMessageLimit < 0)
        {
            throw new InvalidOperationException("Daily message limits cannot be negative.");
        }

        if (ActionType != DoctorEnforcementActionType.AutomaticReactivate)
        {
            if (string.IsNullOrWhiteSpace(ActorAdminUserId))
            {
                throw new InvalidOperationException("Admin enforcement actions require an actor.");
            }

            if (string.IsNullOrWhiteSpace(Reason))
            {
                throw new InvalidOperationException("Admin enforcement actions require a reason.");
            }
        }

        if (ActionType == DoctorEnforcementActionType.Suspend)
        {
            if (NewStatus != DoctorMarketplaceStatus.Suspended || SuspendedAtUtc is null || SuspendedUntilUtc is null)
            {
                throw new InvalidOperationException("Suspend actions require suspended state and suspension timestamps.");
            }

            if (SuspendedAtUtc.Value.Kind != DateTimeKind.Utc || SuspendedUntilUtc.Value.Kind != DateTimeKind.Utc || SuspendedUntilUtc.Value <= EffectiveAtUtc)
            {
                throw new InvalidOperationException("Suspend actions require a future UTC expiry.");
            }
        }

        if (ActionType == DoctorEnforcementActionType.ReduceDailyLimit && NewDailyMessageLimit is null)
        {
            throw new InvalidOperationException("Reduce-limit actions require a new daily message limit.");
        }

        if (ActionType == DoctorEnforcementActionType.Reactivate
            && (PreviousStatus != DoctorMarketplaceStatus.Suspended || NewStatus != DoctorMarketplaceStatus.Active))
        {
            throw new InvalidOperationException("Manual reactivation actions require suspended-to-active transition evidence.");
        }

        if (ActionType == DoctorEnforcementActionType.AutomaticReactivate)
        {
            if (PreviousStatus != DoctorMarketplaceStatus.Suspended || NewStatus != DoctorMarketplaceStatus.Active || SuspendedUntilUtc is null)
            {
                throw new InvalidOperationException("Automatic reactivation actions require suspended-to-active transition and suspension expiry evidence.");
            }

            if (SuspendedUntilUtc.Value.Kind != DateTimeKind.Utc || EffectiveAtUtc < SuspendedUntilUtc.Value)
            {
                throw new InvalidOperationException("Automatic reactivation cannot be effective before the suspension expiry.");
            }
        }
    }
}
