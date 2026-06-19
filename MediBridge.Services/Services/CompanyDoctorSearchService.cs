using System.Text.Json;
using FluentValidation;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Doctors;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class CompanyDoctorSearchService : ICompanyDoctorSearchService
{
    private const string DeniedAuditEventType = "Phase5CompanyDoctorSearchDenied";
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IValidator<EligibleDoctorSearchRequestDto> validator;

    public CompanyDoctorSearchService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IValidator<EligibleDoctorSearchRequestDto> validator)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.validator = validator;
    }

    public async Task<EligibleDoctorPageDto> SearchEligibleDoctorsAsync(string actorUserId, EligibleDoctorSearchRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (validationResult.IsValid is false)
        {
            throw new Phase5ValidationException(
                "Validation failed.",
                validationResult.Errors.Select(error => error.ErrorMessage).ToArray());
        }

        await EnsureApprovedCompanyActorAsync(actorUserId, cancellationToken);

        var criteria = new EligibleDoctorSearchCriteria(
            request.Specialization,
            request.MinExperienceYears,
            request.MaxExperienceYears,
            request.Location,
            request.MinActivityScore,
            request.MinPrice,
            request.MaxPrice);
        var skip = (request.PageNumber - 1) * request.PageSize;
        var totalCount = await domainUnitOfWork.Profiles.CountEligibleDoctorsAsync(criteria, cancellationToken);
        var doctors = await domainUnitOfWork.Profiles.SearchEligibleDoctorsAsync(criteria, skip, request.PageSize, cancellationToken);

        return new EligibleDoctorPageDto
        {
            Page = new PageMetadataDto
            {
                PageNumber = request.PageNumber,
                PageSize = request.PageSize,
                TotalCount = totalCount
            },
            Items = doctors.Select(doctor => new EligibleDoctorDto
            {
                DoctorId = doctor.Id,
                Specialization = doctor.Specialization,
                ExperienceYears = doctor.ExperienceYears,
                Location = doctor.Location,
                ActivityScore = doctor.ActivityScore,
                PricePerMessage = doctor.PricePerMessage ?? 0m
            }).ToArray()
        };
    }

    private async Task EnsureApprovedCompanyActorAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var user = await identityUnitOfWork.Users.FindByIdAsync(actorUserId, cancellationToken);
        var profile = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(actorUserId, cancellationToken);

        if (user is not { Role: UserRole.Company, AccountStatus: AccountStatus.Approved, IsDeleted: false } || profile is not { IsDeleted: false })
        {
            await domainUnitOfWork.AuditEvents.AddPhase5AuditEventAsync(
                Guid.NewGuid().ToString("N"),
                DeniedAuditEventType,
                actorUserId,
                user?.Role.ToString(),
                AuditTargetType.User,
                actorUserId,
                AuditOutcome.Denied,
                "Actor is not an approved active company user.",
                correlationId: null,
                JsonSerializer.Serialize(new { ActorUserId = actorUserId }),
                DateTime.UtcNow,
                cancellationToken);
            await domainUnitOfWork.SaveChangesAsync(cancellationToken);
            throw new Phase5ForbiddenException("Forbidden.");
        }
    }
}
