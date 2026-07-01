using FluentValidation;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Pricing;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class AdminPricingService : IAdminPricingService
{
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IValidator<SetDoctorPriceRequestDto> validator;

    public AdminPricingService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IValidator<SetDoctorPriceRequestDto> validator)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.validator = validator;
    }

    public async Task<DoctorPriceDto> SetDoctorPriceAsync(string adminUserId, string doctorId, SetDoctorPriceRequestDto request, CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new WorkflowValidationException("Validation failed.");
        }

        return await domainUnitOfWork.ExecuteInTransactionAsync<DoctorPriceDto>(async transactionCancellationToken =>
        {
            var admin = await identityUnitOfWork.Users.FindByIdForUpdateAsync(adminUserId, transactionCancellationToken)
                ?? throw new WorkflowUnauthorizedException("Authentication denied.");
            if (admin.Role != UserRole.Admin || admin.AccountStatus != AccountStatus.Approved || admin.IsDeleted)
            {
                throw new WorkflowForbiddenException("Forbidden.");
            }

            if (string.IsNullOrWhiteSpace(doctorId))
            {
                throw new WorkflowNotFoundException("Not found.");
            }

            var doctor = await identityUnitOfWork.Profiles.FindDoctorProfileByIdAsync(doctorId, transactionCancellationToken)
                ?? throw new WorkflowNotFoundException("Not found.");
            var doctorUser = await identityUnitOfWork.Users.FindByIdForUpdateAsync(doctor.UserId, transactionCancellationToken)
                ?? throw new WorkflowNotFoundException("Not found.");
            if (doctorUser.Role != UserRole.Doctor
                || doctorUser.AccountStatus != AccountStatus.Approved
                || doctorUser.IsDeleted
                || doctor.IsDeleted)
            {
                throw new WorkflowNotFoundException("Not found.");
            }

            var previousPrice = doctor.PricePerMessage;
            var newPrice = request.PricePerMessage!.Value;
            doctor.PricePerMessage = newPrice;
            doctor.UpdatedAtUtc = DateTime.UtcNow;
            await domainUnitOfWork.PolicyHistory.AddDoctorPriceHistoryAsync(
                Guid.NewGuid().ToString("N"),
                doctor.Id,
                previousPrice,
                newPrice,
                admin.Id,
                request.Reason,
                transactionCancellationToken);

            return new DoctorPriceDto(doctor.Id, newPrice, "EGP");
        }, cancellationToken);
    }
}
