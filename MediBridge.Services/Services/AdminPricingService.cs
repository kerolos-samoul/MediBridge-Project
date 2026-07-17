using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Entities.Identity;
using MediBridge.Core.Entities.Profiles;
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
    private readonly IValidator<SetDoctorPriceRequestDto> priceValidator;
    private readonly IValidator<DeactivateDoctorPricingRequestDto> deactivatePricingValidator;
    private readonly IValidator<SetDoctorDeliverySettingsRequestDto> deliverySettingsValidator;

    public AdminPricingService(
        IIdentityUnitOfWork identityUnitOfWork,
        IDomainUnitOfWork domainUnitOfWork,
        IValidator<SetDoctorPriceRequestDto> priceValidator,
        IValidator<DeactivateDoctorPricingRequestDto> deactivatePricingValidator,
        IValidator<SetDoctorDeliverySettingsRequestDto> deliverySettingsValidator)
    {
        this.identityUnitOfWork = identityUnitOfWork;
        this.domainUnitOfWork = domainUnitOfWork;
        this.priceValidator = priceValidator;
        this.deactivatePricingValidator = deactivatePricingValidator;
        this.deliverySettingsValidator = deliverySettingsValidator;
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

        var validation = await priceValidator.ValidateAsync(request, cancellationToken);
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
            doctor.PricingIsActive = true;
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
                JsonSerializer.Serialize(new { PreviousPrice = previousPrice, NewPrice = newPrice, PricingIsActive = true, Currency = "EGP" }),
                now,
                transactionCancellationToken);

            return new DoctorPriceDto(doctor.Id, newPrice, "EGP", PricingIsActive: true);
        }, cancellationToken);
    }

    public async Task<DoctorPriceDto> DeactivateDoctorPricingAsync(
        string adminUserId,
        string doctorId,
        DeactivateDoctorPricingRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Deactivate pricing request body is required."]);
        }

        var validation = await deactivatePricingValidator.ValidateAsync(request, cancellationToken);
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

            if (!doctor.PricingIsActive)
            {
                throw new Phase5ConflictException("Doctor pricing is already inactive.");
            }

            var previousPrice = doctor.PricePerMessage;
            var reason = request.Reason!.Trim();
            var now = DateTime.UtcNow;
            doctor.PricingIsActive = false;
            doctor.UpdatedAtUtc = now;

            await domainUnitOfWork.PolicyHistory.AddDoctorPricingDeactivationHistoryAsync(
                Guid.NewGuid().ToString("N"),
                doctor.Id,
                previousPrice,
                admin.Id,
                reason,
                transactionCancellationToken);
            await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "DoctorPricingDeactivated",
                admin.Id,
                admin.Role.ToString(),
                AuditTargetType.Doctor,
                doctor.Id,
                AuditOutcome.Success,
                reason,
                correlationId: null,
                JsonSerializer.Serialize(new { PreviousPrice = previousPrice, PricingIsActive = false, Currency = "EGP" }),
                now,
                transactionCancellationToken);

            return new DoctorPriceDto(doctor.Id, null, "EGP", PricingIsActive: false);
        }, cancellationToken);
    }

    public async Task<DoctorDeliverySettingsDto> GetDoctorDeliverySettingsAsync(
        string adminUserId,
        string doctorId,
        CancellationToken cancellationToken = default)
    {
        var admin = await identityUnitOfWork.Users.FindByIdAsync(adminUserId, cancellationToken);
        if (admin is not { Role: UserRole.Admin, AccountStatus: AccountStatus.Approved, IsDeleted: false })
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        if (string.IsNullOrWhiteSpace(doctorId))
        {
            throw new Phase5NotFoundException("Doctor was not found.");
        }

        var doctor = await identityUnitOfWork.Profiles.FindDoctorProfileByIdAsync(
            doctorId.Trim(),
            cancellationToken);
        if (doctor is null)
        {
            throw new Phase5NotFoundException("Doctor was not found.");
        }

        var doctorUser = await identityUnitOfWork.Users.FindByIdAsync(doctor.UserId, cancellationToken);
        if (doctorUser is not { Role: UserRole.Doctor, IsDeleted: false })
        {
            throw new Phase5NotFoundException("Doctor was not found.");
        }

        return ToDeliverySettingsDto(doctor, doctorUser);
    }

    public async Task<DoctorDeliverySettingsDto> SetDoctorDeliverySettingsAsync(
        string adminUserId,
        string doctorId,
        SetDoctorDeliverySettingsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new Phase5ValidationException("Validation failed.", ["Delivery settings request body is required."]);
        }

        var validation = await deliverySettingsValidator.ValidateAsync(request, cancellationToken);
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

            if (doctor.Status != DoctorMarketplaceStatus.Active)
            {
                throw new Phase5ConflictException("Doctor is not active in the marketplace.");
            }

            var previousDailyLimit = doctor.DailyMessageLimit;
            var previousWeeklyRequirement = doctor.MinimumWeeklyRequirement;
            var newDailyLimit = request.DailyMessageLimit!.Value;
            var newWeeklyRequirement = request.MinimumWeeklyRequirement!.Value;
            var reason = request.Reason!.Trim();
            var now = DateTime.UtcNow;

            doctor.DailyMessageLimit = newDailyLimit;
            doctor.MinimumWeeklyRequirement = newWeeklyRequirement;
            doctor.UpdatedAtUtc = now;
            var doctorWallet = await domainUnitOfWork.Wallets.GetOrCreateActiveWalletForUpdateAsync(
                Guid.NewGuid().ToString("N"),
                WalletOwnerType.Doctor,
                doctor.Id,
                doctor.UserId,
                now,
                transactionCancellationToken);

            await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                Guid.NewGuid().ToString("N"),
                "DoctorDeliverySettingsUpdated",
                admin.Id,
                admin.Role.ToString(),
                AuditTargetType.Doctor,
                doctor.Id,
                AuditOutcome.Success,
                reason,
                correlationId: null,
                JsonSerializer.Serialize(new
                {
                    PreviousDailyMessageLimit = previousDailyLimit,
                    NewDailyMessageLimit = newDailyLimit,
                    PreviousMinimumWeeklyRequirement = previousWeeklyRequirement,
                    NewMinimumWeeklyRequirement = newWeeklyRequirement,
                    DoctorWalletId = doctorWallet.Id
                }),
                now,
                transactionCancellationToken);

            return ToDeliverySettingsDto(doctor, doctorUser);
        }, cancellationToken);
    }

    private static DoctorDeliverySettingsDto ToDeliverySettingsDto(DoctorProfile doctor, ApplicationUser doctorUser)
    {
        var isDeliveryEligible =
            doctorUser is { Role: UserRole.Doctor, AccountStatus: AccountStatus.Approved, IsDeleted: false }
            && !doctor.IsDeleted
            && doctor.Status == DoctorMarketplaceStatus.Active
            && doctor.PricingIsActive
            && doctor.PricePerMessage > 0
            && doctor.DailyMessageLimit > 0;

        var eligibilityState = isDeliveryEligible
            ? "DeliveryEligible"
            : ResolveDeliverySettingsState(doctor, doctorUser);

        return new DoctorDeliverySettingsDto(
            doctor.Id,
            doctor.DailyMessageLimit,
            doctor.MinimumWeeklyRequirement,
            isDeliveryEligible,
            eligibilityState);
    }

    private static string ResolveDeliverySettingsState(DoctorProfile doctor, ApplicationUser doctorUser)
    {
        if (doctorUser is not { Role: UserRole.Doctor, AccountStatus: AccountStatus.Approved, IsDeleted: false })
        {
            return "DoctorAccountNotApproved";
        }

        if (doctor.IsDeleted)
        {
            return "DoctorProfileDeleted";
        }

        if (doctor.Status != DoctorMarketplaceStatus.Active)
        {
            return "DoctorMarketplaceInactive";
        }

        if (!doctor.PricingIsActive)
        {
            return "DoctorPricingInactive";
        }

        if (doctor.PricePerMessage is null or <= 0)
        {
            return "DoctorPriceMissing";
        }

        if (doctor.DailyMessageLimit <= 0)
        {
            return "DailyMessageLimitNotConfigured";
        }

        return "DeliveryIneligible";
    }
}
