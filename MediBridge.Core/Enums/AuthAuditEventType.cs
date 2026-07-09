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
    ContactVerificationRequested = 11,
    ContactVerificationSent = 12,
    ContactVerificationFailed = 13,
    ContactVerificationExpired = 14,
    ContactVerificationMaxAttemptsReached = 15,
    PasswordResetRequested = 18,
    PasswordResetSent = 19,
    PasswordResetSendFailed = 20
}
