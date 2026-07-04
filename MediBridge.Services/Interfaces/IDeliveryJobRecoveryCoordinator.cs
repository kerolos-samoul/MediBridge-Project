namespace MediBridge.Services.Interfaces;

public interface IDeliveryJobRecoveryCoordinator
{
    Task RecoverAsync(CancellationToken cancellationToken = default);
}
