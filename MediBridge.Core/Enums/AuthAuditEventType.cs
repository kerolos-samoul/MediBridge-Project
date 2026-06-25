namespace MediBridge.Core.Enums;

public enum AuthAuditEventType
{
    Registration = 1,
    LoginSuccess = 2,
    LoginDenied = 3,
    Refresh = 4,
    Logout = 5,
    PasswordResetCompleted = 6,
    ContactVerificationCompleted = 7,
    RefreshReuseDetected = 8,
    AdminDecision = 9,
    AccountResubmission = 10,
    ContactVerificationIssued = 11,
    ContactVerificationDeliveryFailed = 12,
    ContactVerificationDenied = 13
}
