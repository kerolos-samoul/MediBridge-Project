using MediBridge.Core.Enums;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.DTOs.Wallets;

namespace MediBridge.Services.Interfaces;

public interface IAdminWorkQueueService
{
    Task<AdminWorkQueuePageDto> ListAsync(int? pageNumber, int? pageSize, string? category, string? status, CancellationToken cancellationToken = default);
}

public interface IAdminStatisticsService
{
    Task<AdminStatisticsDto> GetAsync(string? fromDateEgypt, string? toDateEgypt, CancellationToken cancellationToken = default);
}

public interface IWithdrawalService
{
    Task<DoctorWithdrawalDto> CreateWithdrawalAsync(string actorUserId, CreateWithdrawalRequestDto request, CancellationToken cancellationToken = default);
    Task<DoctorWithdrawalPageDto> ListDoctorWithdrawalsAsync(string actorUserId, int? pageNumber, int? pageSize, WithdrawalRequestStatus? status, CancellationToken cancellationToken = default);
    Task<AdminWithdrawalPageDto> ListAdminWithdrawalsAsync(string adminUserId, int? pageNumber, int? pageSize, WithdrawalRequestStatus? status, string? doctorId, DateTime? requestedFromUtc, DateTime? requestedToUtc, DateTime? reviewedFromUtc, DateTime? reviewedToUtc, decimal? minimumAmount, decimal? maximumAmount, string? payoutReference, CancellationToken cancellationToken = default);
    Task<AdminWithdrawalDto> ApproveAsync(string adminUserId, string withdrawalId, AdminWithdrawalDecisionRequestDto? request, CancellationToken cancellationToken = default);
    Task<AdminWithdrawalDto> RejectAsync(string adminUserId, string withdrawalId, AdminWithdrawalDecisionRequestDto request, CancellationToken cancellationToken = default);
    Task<AdminWithdrawalDto> MarkPaidAsync(string adminUserId, string withdrawalId, MarkWithdrawalPaidRequestDto request, CancellationToken cancellationToken = default);
    Task<AdminWithdrawalDto> MarkFailedAsync(string adminUserId, string withdrawalId, MarkWithdrawalFailedRequestDto request, CancellationToken cancellationToken = default);
}
