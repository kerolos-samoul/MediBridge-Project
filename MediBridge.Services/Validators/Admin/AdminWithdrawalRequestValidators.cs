using FluentValidation;
using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.Validators.Admin;

public sealed class AdminWithdrawalDecisionRequestValidator : AbstractValidator<AdminWithdrawalDecisionRequestDto>
{
    public AdminWithdrawalDecisionRequestValidator()
    {
        RuleFor(request => request.Note).MaximumLength(1000);
        RuleFor(request => request.Reason).MaximumLength(1000);
    }
}

public sealed class MarkWithdrawalPaidRequestValidator : AbstractValidator<MarkWithdrawalPaidRequestDto>
{
    public MarkWithdrawalPaidRequestValidator()
    {
        RuleFor(request => request.PayoutReference)
            .NotEmpty()
            .MaximumLength(200);
    }
}

public sealed class MarkWithdrawalFailedRequestValidator : AbstractValidator<MarkWithdrawalFailedRequestDto>
{
    public MarkWithdrawalFailedRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty()
            .MaximumLength(1000);
    }
}
