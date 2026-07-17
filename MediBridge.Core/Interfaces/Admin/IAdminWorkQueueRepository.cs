namespace MediBridge.Core.Interfaces.Admin;

public interface IAdminWorkQueueRepository
{
    Task<IReadOnlyDictionary<AdminWorkQueueCategory, int>> CountByCategoryAsync(
        AdminWorkQueueFilters filters,
        CancellationToken cancellationToken = default);

    Task<AdminPageReadModel<AdminWorkQueueItemReadModel>> ListQueueItemsAsync(
        AdminWorkQueueFilters filters,
        CancellationToken cancellationToken = default);
}
