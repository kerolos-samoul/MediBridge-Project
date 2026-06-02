namespace MediBridge.Core.Entities;

public interface IConcurrencyTrackedRecord
{
    byte[] ConcurrencyToken { get; set; }
}
