using FluentValidation;
using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Admin;

namespace MediBridge.Services.Validators.Admin;

internal sealed class AdminAccountDecisionRequestValidator : AbstractValidator<AdminAccountDecisionRequestDto>
{
    public AdminAccountDecisionRequestValidator()
    {
        RuleFor(request => request.Decision).IsInEnum();
        RuleFor(request => request.Reason)
            .NotEmpty()
            .When(request => request.Decision is AdminAccountDecisionType.Reject or AdminAccountDecisionType.Suspend);
    }
}
