namespace MediBridge.Services.DTOs.Doctors;

public sealed class EligibleDoctorSearchRequestDto
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Specialization { get; set; }
    public int? MinExperienceYears { get; set; }
    public int? MaxExperienceYears { get; set; }
    public string? Location { get; set; }
    public decimal? MinActivityScore { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
}

public sealed class EligibleDoctorDto
{
    public string DoctorId { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public int ExperienceYears { get; set; }
    public string Location { get; set; } = string.Empty;
    public decimal ActivityScore { get; set; }
    public decimal PricePerMessage { get; set; }
}

public sealed class EligibleDoctorPageDto
{
    public PageMetadataDto Page { get; set; } = new();
    public IReadOnlyList<EligibleDoctorDto> Items { get; set; } = Array.Empty<EligibleDoctorDto>();
}

public sealed class PageMetadataDto
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
}
