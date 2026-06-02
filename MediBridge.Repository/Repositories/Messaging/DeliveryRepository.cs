using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Enums;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;

namespace MediBridge.Repository.Repositories.Messaging;

public sealed class DeliveryRepository : IDeliveryRepository
{
    private readonly MediBridgeDbContext context;

    public DeliveryRepository(MediBridgeDbContext context)
    {
        this.context = context;
    }

    public async Task AddDeliveryAsync(
        string deliveryId,
        string doctorId,
        string campaignId,
        string companyId,
        DateOnly deliveryDateEgypt,
        decimal pricePerMessageSnapshot,
        decimal platformFeePercentSnapshot,
        decimal platformFeeAmount,
        decimal doctorEarnings,
        decimal reservedAmount,
        CancellationToken cancellationToken = default)
    {
        var delivery = new DoctorAdDelivery
        {
            Id = deliveryId,
            DoctorId = doctorId,
            CampaignId = campaignId,
            CompanyId = companyId,
            DeliveryDateEgypt = deliveryDateEgypt,
            Status = DeliveryStatus.Active
        };
        delivery.ApplySettlementSnapshot(pricePerMessageSnapshot, platformFeePercentSnapshot, platformFeeAmount, doctorEarnings, reservedAmount);

        await context.DoctorAdDeliveries.AddAsync(delivery, cancellationToken);
    }

    public Task<string?> FindDeliveryIdAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries
            .Where(delivery => delivery.DoctorId == doctorId && delivery.DeliveryDateEgypt == deliveryDateEgypt && delivery.CampaignId == campaignId)
            .Select(delivery => delivery.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> DeliveryExistsAsync(string doctorId, DateOnly deliveryDateEgypt, string campaignId, CancellationToken cancellationToken = default)
    {
        return context.DoctorAdDeliveries.AnyAsync(delivery => delivery.DoctorId == doctorId && delivery.DeliveryDateEgypt == deliveryDateEgypt && delivery.CampaignId == campaignId, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListDeliveryHistoryIdsAsync(string doctorId, DateOnly? fromDateEgypt = null, DateOnly? toDateEgypt = null, DeliveryStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = context.DoctorAdDeliveries.Where(delivery => delivery.DoctorId == doctorId);
        if (fromDateEgypt is not null)
        {
            query = query.Where(delivery => delivery.DeliveryDateEgypt >= fromDateEgypt);
        }

        if (toDateEgypt is not null)
        {
            query = query.Where(delivery => delivery.DeliveryDateEgypt <= toDateEgypt);
        }

        if (status is not null)
        {
            query = query.Where(delivery => delivery.Status == status);
        }

        return await query
            .OrderBy(delivery => delivery.DeliveryDateEgypt)
            .ThenBy(delivery => delivery.CreatedAtUtc)
            .Select(delivery => delivery.Id)
            .ToListAsync(cancellationToken);
    }
}
