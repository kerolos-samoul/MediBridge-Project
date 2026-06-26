using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace MediBridge.APIs.Config;

public static class RateLimitPolicyNames
{
    public const string Login = "login";
    public const string Registration = "registration";
    public const string Refresh = "refresh";
    public const string CompanyTopUp = "company-top-up";
    public const string DoctorWithdrawal = "doctor-withdrawal";
    public const string DoctorInteraction = "doctor-interaction";
    public const string FileUpload = "file-upload";
    public const string Phase5CampaignSubmission = "phase5-campaign-submission";
    public const string Phase5WalletTopUp = "phase5-wallet-top-up";
    public const string Envelope = "rate-limit-envelope";

    public static readonly string[] All =
    [
        Login,
        Registration,
        Refresh,
        CompanyTopUp,
        DoctorWithdrawal,
        DoctorInteraction,
        FileUpload,
        Phase5CampaignSubmission,
        Phase5WalletTopUp,
        Envelope
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
        [RateLimitPolicyNames.DoctorInteraction] = new(),
        [RateLimitPolicyNames.FileUpload] = new()
        {
            PermitLimit = 20,
            WindowSeconds = 3600,
            QueueLimit = 0
        },
        [RateLimitPolicyNames.Phase5CampaignSubmission] = new(),
        [RateLimitPolicyNames.Phase5WalletTopUp] = new(),
        [RateLimitPolicyNames.Envelope] = new()
    };

    public RateLimitPolicyOptions GetPolicy(string policyName)
    {
        return Policies.TryGetValue(policyName, out var policy)
            ? policy
            : throw new InvalidOperationException($"Missing rate limit policy '{policyName}'.");
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

public sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        var missingPolicies = RateLimitPolicyNames.All
            .Where(policyName => options.Policies.ContainsKey(policyName) is false)
            .ToArray();

        return missingPolicies.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Missing rate limit policy configuration for: {string.Join(", ", missingPolicies)}.");
    }
}
