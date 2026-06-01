namespace MediBridge.Services.DTOs.Admin;

public sealed class PendingAccountPageDto
{
    public IReadOnlyList<PendingAccountDto> Items { get; set; } = Array.Empty<PendingAccountDto>();
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
