using MediBridge.Services.Interfaces;
using MediBridge.Services.Validators.Admin;
using Xunit;

namespace MediBridge.UnitTests.Admin;

public sealed class AdminToolsPaginationValidatorTests
{
    private readonly AdminToolsPaginationValidator validator = new();

    [Fact]
    public void Resolve_DefaultsToPageOneAndTwentyItems()
    {
        var pagination = validator.Resolve(null, null);

        Assert.Equal(1, pagination.PageNumber);
        Assert.Equal(20, pagination.PageSize);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 20)]
    [InlineData(3, 100)]
    public void Resolve_AcceptsValidBounds(int pageNumber, int pageSize)
    {
        var pagination = validator.Resolve(pageNumber, pageSize);

        Assert.Equal(pageNumber, pagination.PageNumber);
        Assert.Equal(pageSize, pagination.PageSize);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Resolve_RejectsInvalidBounds(int pageNumber, int pageSize)
    {
        Assert.Throws<Phase5ValidationException>(() => validator.Resolve(pageNumber, pageSize));
    }
}
