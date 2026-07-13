using MediBridge.Core.Interfaces.Campaigns;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Campaigns;

public sealed class CompanyReportingPaginationValidator
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 100;

    public CompanyReportingPagination Resolve(int? pageNumber, int? pageSize)
    {
        var resolvedPageNumber = pageNumber ?? DefaultPageNumber;
        var resolvedPageSize = pageSize ?? DefaultPageSize;
        if (resolvedPageNumber < 1)
        {
            throw new Phase5ValidationException("Validation failed.", ["PageNumber must be at least 1."]);
        }

        if (resolvedPageSize is < 1 or > MaximumPageSize)
        {
            throw new Phase5ValidationException("Validation failed.", ["PageSize must be between 1 and 100."]);
        }

        return new CompanyReportingPagination(resolvedPageNumber, resolvedPageSize);
    }
}
