namespace MediBridge.Core.Entities;

public interface ISoftDeleteRecord
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
}
