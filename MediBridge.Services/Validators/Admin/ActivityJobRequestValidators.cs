using FluentValidation;
using MediBridge.Core.Interfaces.Time;
using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.Validators.Admin;

public sealed class RunDailyActivityScoreRequestDtoValidator : AbstractValidator<RunDailyActivityScoreRequestDto>
{
    public RunDailyActivityScoreRequestDtoValidator(IEgyptBusinessClock clock)
    {
        RuleFor(request => request.ScoreDateEgypt)
            .NotNull()
            .Must(scoreDate => scoreDate!.Value < clock.Capture().BusinessDateEgypt)
            .WithMessage("ScoreDateEgypt must be a completed Cairo date.");
    }
}

public sealed class RunWeeklyEnforcementRequestDtoValidator : AbstractValidator<RunWeeklyEnforcementRequestDto>
{
    public RunWeeklyEnforcementRequestDtoValidator(IEgyptBusinessClock clock)
    {
        RuleFor(request => request.WeekStartDateEgypt)
            .NotNull()
            .Must(weekStart => weekStart!.Value.DayOfWeek == DayOfWeek.Monday)
            .WithMessage("WeekStartDateEgypt must be a Monday.")
            .Must(weekStart => weekStart!.Value.AddDays(7) <= clock.Capture().BusinessDateEgypt)
            .WithMessage("WeekStartDateEgypt must identify a completed Cairo week.");
    }
}
