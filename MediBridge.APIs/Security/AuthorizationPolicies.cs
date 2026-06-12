using MediBridge.Core.Enums;

namespace MediBridge.APIs.Security;

public static class AuthorizationPolicies
{
    public const string Admin = nameof(UserRole.Admin);
    public const string Doctor = nameof(UserRole.Doctor);
    public const string Company = nameof(UserRole.Company);

    public const string AdminOnly = "AdminOnly";
    public const string DoctorOnly = "DoctorOnly";
    public const string CompanyOnly = "CompanyOnly";
    public const string AdminFileReview = "AdminFileReview";
    public const string CompanyCampaignFileUpload = "CompanyCampaignFileUpload";
    public const string DoctorVerificationUpload = "DoctorVerificationUpload";
    public const string AuthenticatedFileAccess = "AuthenticatedFileAccess";
}
