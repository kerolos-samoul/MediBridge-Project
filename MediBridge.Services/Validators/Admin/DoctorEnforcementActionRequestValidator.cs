using FluentValidation;
using MediBridge.Core.Enums;
using MediBridge.Services.Config;
using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.Validators.Admin;

public sealed class DoctorEnforcementActionRequestValidator : AbstractValidator<DoctorEnforcementActionRequestDto>
{
    public DoctorEnforcementActionRequestValidator(DoctorDeliverySettingsOptions options, TimeProvider timeProvider)
    {
        RuleFor(request => request.ActionType).NotNull();
        RuleFor(request => request.Reason)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .Must(reason => reason is not null && reason.Trim().Length is >= 10 and <= 1000)
            .WithMessage("Reason must be between 10 and 1000 characters after trimming.");

        When(request => request.ActionType == DoctorEnforcementActionType.ReduceDailyLimit, () =>
        {
            RuleFor(request => request.NewDailyMessageLimit)
                .NotNull()
                .GreaterThanOrEqualTo(options.MinimumDailyMessageLimit)
                .LessThanOrEqualTo(options.MaximumDailyMessageLimit);
            RuleFor(request => request.SuspendedUntilUtc).Null();
        });

        When(request => request.ActionType == DoctorEnforcementActionType.Suspend, () =>
        {
            RuleFor(request => request.SuspendedUntilUtc)
                .NotNull()
                .Must(value => value!.Value.Kind == DateTimeKind.Utc)
                .WithMessage("SuspendedUntilUtc must be UTC.")
                .Must(value => value!.Value > timeProvider.GetUtcNow().UtcDateTime)
                .WithMessage("SuspendedUntilUtc must be in the future.");
            RuleFor(request => request.NewDailyMessageLimit).Null();
        });

        When(request => request.ActionType is DoctorEnforcementActionType.Warn or DoctorEnforcementActionType.Reactivate, () =>
        {
            RuleFor(request => request.NewDailyMessageLimit).Null();
            RuleFor(request => request.SuspendedUntilUtc).Null();
        });

        When(request => request.ActionType == DoctorEnforcementActionType.AutomaticReactivate, () =>
        {
            RuleFor(request => request.ActionType).NotEqual(DoctorEnforcementActionType.AutomaticReactivate);
        });
    }
}
