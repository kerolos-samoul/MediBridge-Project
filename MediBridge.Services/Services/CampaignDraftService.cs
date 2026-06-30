using FluentValidation;
using MediBridge.Core.Entities.Campaigns;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Identity;
using MediBridge.Services.DTOs.Campaigns;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Services;

public sealed class CampaignDraftService : ICampaignDraftService
{
    private readonly IDomainUnitOfWork domainUnitOfWork;
    private readonly IIdentityUnitOfWork identityUnitOfWork;
    private readonly IValidator<CreateCampaignDraftRequestDto> validator;

    public CampaignDraftService(
        IDomainUnitOfWork domainUnitOfWork,
        IIdentityUnitOfWork identityUnitOfWork,
        IValidator<CreateCampaignDraftRequestDto> validator)
    {
        this.domainUnitOfWork = domainUnitOfWork;
        this.identityUnitOfWork = identityUnitOfWork;
        this.validator = validator;
    }

    public async Task<CampaignDraftDto> CreateDraftAsync(
        string actorUserId,
        CreateCampaignDraftRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            throw new Phase5ValidationException(
                "Validation failed.",
                validation.Errors.Select(error => error.ErrorMessage).ToArray());
        }

        var companyId = await ResolveApprovedCompanyIdAsync(actorUserId, cancellationToken);
        var campaign = new Campaign
        {
            Id = Guid.NewGuid().ToString("N"),
            CompanyId = companyId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            ClinicalResearchInfo = NormalizeOptional(request.ClinicalResearchInfo),
            Status = CampaignStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow
        };

        await domainUnitOfWork.ExecuteInTransactionAsync(
            async transactionCancellationToken =>
            {
                await domainUnitOfWork.Campaigns.AddCampaignAsync(campaign, transactionCancellationToken);
            },
            cancellationToken);

        return new CampaignDraftDto
        {
            CampaignId = campaign.Id,
            CompanyId = campaign.CompanyId,
            Title = campaign.Title,
            Description = campaign.Description,
            ClinicalResearchInfo = campaign.ClinicalResearchInfo,
            Status = campaign.Status
        };
    }

    private async Task<string> ResolveApprovedCompanyIdAsync(string actorUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new Phase5ForbiddenException("Forbidden.");
        }

        var user = await identityUnitOfWork.Users.FindByIdAsync(actorUserId, cancellationToken);
        var profile = await identityUnitOfWork.Profiles.FindCompanyProfileByUserIdAsync(actorUserId, cancellationToken);
        if (user is { Role: UserRole.Company, AccountStatus: AccountStatus.Approved, IsDeleted: false }
            && profile is { IsDeleted: false })
        {
            return profile.Id;
        }

        throw new Phase5ForbiddenException("Forbidden.");
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
