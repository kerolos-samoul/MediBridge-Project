using System.Text.Json;
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

    public async Task<DoctorPriceDto> SetDoctorPriceAsync(
        string adminUserId,
        string doctorId,
        SetDoctorPriceRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Pricing request body is required."]);
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase5ValidationException(
                "Validation failed.",
                validation.Errors.Select(error => error.ErrorMessage).ToArray());
        }

        return await domainUnitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var admin = await identityUnitOfWork.Users.FindByIdForUpdateAsync(adminUserId, transactionCancellationToken);
            if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
            {
                throw new Phase5ForbiddenException("Forbidden.");
            }

            if (string.IsNullOrWhiteSpace(doctorId))
            {
                throw new Phase5NotFoundException("Doctor was not found.");
            }

            var doctor = await identityUnitOfWork.Profiles.FindDoctorProfileByIdForUpdateAsync(
                doctorId.Trim(),
                transactionCancellationToken);
            if (doctor is null)
            {
                throw new Phase5NotFoundException("Doctor was not found.");
            }

            var doctorUser = await identityUnitOfWork.Users.FindByIdForUpdateAsync(
                doctor.UserId,
                transactionCancellationToken);
            if (doctorUser is not { Role: UserRole.Doctor, AccountStatus: AccountStatus.Approved, IsDeleted: false })
            {
                throw new Phase5NotFoundException("Doctor was not found.");
            }

            var previousPrice = doctor.PricePerMessage;
            var newPrice = request.PricePerMessage!.Value;
            var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            var now = DateTime.UtcNow;
            doctor.PricePerMessage = newPrice;
            doctor.UpdatedAtUtc = now;
            await domainUnitOfWork.PolicyHistory.AddDoctorPriceHistoryAsync(
                Guid.NewGuid().ToString("N"),
                doctor.Id,
                previousPrice,
                newPrice,
                admin.Id,
                reason,
                transactionCancellationToken);
            await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "DoctorPriceUpdated",
                admin.Id,
                admin.Role.ToString(),
                AuditTargetType.Doctor,
                doctor.Id,
                AuditOutcome.Success,
                reason ?? "Doctor price updated.",
                correlationId: null,
                JsonSerializer.Serialize(new { PreviousPrice = previousPrice, NewPrice = newPrice, Currency = "EGP" }),
                now,
                transactionCancellationToken);

            return new DoctorPriceDto(doctor.Id, newPrice, "EGP");
        }, cancellationToken);
    }
}
