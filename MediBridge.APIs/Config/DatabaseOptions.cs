using System.ComponentModel.DataAnnotations;

namespace MediBridge.APIs.Config;

public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    [Required]
    public string DefaultConnection { get; set; } = string.Empty;
}
