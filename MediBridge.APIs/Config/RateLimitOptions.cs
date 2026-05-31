using System.ComponentModel.DataAnnotations;

namespace MediBridge.APIs.Config;

public static class RateLimitPolicyNames
{
    public const string Login = "login";
    public const string Registration = "registration";
    public const string Refresh = "refresh";
    public const string CompanyTopUp = "company-top-up";
    public const string DoctorWithdrawal = "doctor-withdrawal";
    public const string DoctorInteraction = "doctor-interaction";

    public static readonly string[] All =
    [
        Login,
        Registration,
        Refresh,
        CompanyTopUp,
        DoctorWithdrawal,
        DoctorInteraction
    ];
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public Dictionary<string, RateLimitPolicyOptions> Policies { get; set; } = new()
    {
        [RateLimitPolicyNames.Login] = new(),
        [RateLimitPolicyNames.Registration] = new(),
        [RateLimitPolicyNames.Refresh] = new(),
        [RateLimitPolicyNames.CompanyTopUp] = new(),
        [RateLimitPolicyNames.DoctorWithdrawal] = new(),
        [RateLimitPolicyNames.DoctorInteraction] = new()
    };

    public RateLimitPolicyOptions GetPolicy(string policyName)
    {
        return Policies.TryGetValue(policyName, out var policy)
            ? policy
            : new RateLimitPolicyOptions();
    }
}

public sealed class RateLimitPolicyOptions
{
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int WindowSeconds { get; set; } = 60;

    [Range(0, int.MaxValue)]
    public int QueueLimit { get; set; } = 0;
}
