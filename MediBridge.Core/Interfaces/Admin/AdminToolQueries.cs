using MediBridge.Core.Enums;

namespace MediBridge.Core.Interfaces.Admin;

public sealed record AdminPagination(int PageNumber = 1, int PageSize = 20)
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public int Skip => (PageNumber - 1) * PageSize;
}

public sealed record AdminWorkQueueFilters(
    AdminWorkQueueCategory? Category = null,
    string? Status = null,
    AdminPagination? Pagination = null);

public sealed record AdminWithdrawalFilters(
    WithdrawalRequestStatus? Status = null,
    string? DoctorId = null,
    DateTime? RequestedFromUtc = null,
    DateTime? RequestedToUtc = null,
    DateTime? ReviewedFromUtc = null,
    DateTime? ReviewedToUtc = null,
    decimal? MinimumAmount = null,
    decimal? MaximumAmount = null,
    string? PayoutReference = null,
    AdminPagination? Pagination = null);

public sealed record AdminStatisticsDateRange(DateOnly FromDateEgypt, DateOnly ToDateEgypt)
{
    public int InclusiveDayCount => ToDateEgypt.DayNumber - FromDateEgypt.DayNumber + 1;
}
