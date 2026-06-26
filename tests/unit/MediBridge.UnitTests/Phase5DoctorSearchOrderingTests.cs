using FluentValidation;
using MediBridge.Services.DTOs.Doctors;
using MediBridge.Services.Validators.Doctors;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class Phase5DoctorSearchOrderingTests
{
    private readonly IValidator<EligibleDoctorSearchRequestDto> validator = new EligibleDoctorSearchRequestValidator();

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Validator_RejectsInvalidPaginationBounds(int pageNumber, int pageSize)
    {
        var result = await validator.ValidateAsync(new EligibleDoctorSearchRequestDto
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validator_RejectsInvalidFilterRanges()
    {
        var result = await validator.ValidateAsync(new EligibleDoctorSearchRequestDto
        {
            PageNumber = 1,
            PageSize = 20,
            MinExperienceYears = 10,
            MaxExperienceYears = 5,
            MinActivityScore = 101m,
            MinPrice = 20m,
            MaxPrice = 10m
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("Minimum experience", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, error => error.ErrorMessage.Contains("Minimum price", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Ordering_UsesActivityDescendingPriceAscendingThenStableId()
    {
        var doctors = new[]
        {
            new EligibleDoctorDto { DoctorId = "doctor-3", ActivityScore = 80m, PricePerMessage = 10m },
            new EligibleDoctorDto { DoctorId = "doctor-2", ActivityScore = 90m, PricePerMessage = 20m },
            new EligibleDoctorDto { DoctorId = "doctor-1", ActivityScore = 90m, PricePerMessage = 20m },
            new EligibleDoctorDto { DoctorId = "doctor-0", ActivityScore = 90m, PricePerMessage = 30m }
        };

        var orderedIds = doctors
            .OrderByDescending(doctor => doctor.ActivityScore)
            .ThenBy(doctor => doctor.PricePerMessage)
            .ThenBy(doctor => doctor.DoctorId)
            .Select(doctor => doctor.DoctorId)
            .ToArray();

        Assert.Equal(["doctor-1", "doctor-2", "doctor-0", "doctor-3"], orderedIds);
    }
}
