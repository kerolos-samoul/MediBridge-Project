using MediBridge.Core.Interfaces.Admin;
using MediBridge.Services.DTOs.Admin;
using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Admin;

namespace MediBridge.Services.Services;

public sealed class AdminWorkQueueService : IAdminWorkQueueService
{
    private readonly IAdminWorkQueueRepository repository;
    private readonly AdminToolsPaginationValidator paginationValidator;

    public AdminWorkQueueService(
        IAdminWorkQueueRepository repository,
        AdminToolsPaginationValidator paginationValidator)
    {
        this.repository = repository;
        this.paginationValidator = paginationValidator;
    }

    public async Task<AdminWorkQueuePageDto> ListAsync(
        int? pageNumber,
        int? pageSize,
        string? category,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var pagination = paginationValidator.Resolve(pageNumber, pageSize);
        AdminWorkQueueCategory? parsedCategory = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            if (!Enum.TryParse<AdminWorkQueueCategory>(category, ignoreCase: true, out var categoryValue))
            {
                throw new Phase5ValidationException("Validation failed.", ["category is invalid."]);
            }

            parsedCategory = categoryValue;
        }

        var filters = new AdminWorkQueueFilters(parsedCategory, status, pagination);
        var page = await repository.ListQueueItemsAsync(filters, cancellationToken);
        var counts = await repository.CountByCategoryAsync(filters, cancellationToken);
        return AdminToolsDtoMapper.ToWorkQueuePage(new AdminWorkQueuePageReadModel(page, counts));
    }
}
