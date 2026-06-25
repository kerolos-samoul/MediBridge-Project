namespace MediBridge.Core.Interfaces.Notifications;

public interface IEmailDelivery
{
    Task SendContactVerificationAsync(
        string destination,
        string oneTimeCode,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(
        string destination,
        string resetToken,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);
}
