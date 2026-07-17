using MediBridge.Core.Interfaces.Admin;
using MediBridge.Services.Interfaces;

namespace MediBridge.Services.Validators.Admin;

public sealed class AdminToolsPaginationValidator
{
    public AdminPagination Resolve(int? pageNumber, int? pageSize)
    {
        var resolvedPageNumber = pageNumber ?? AdminPagination.DefaultPageNumber;
        var resolvedPageSize = pageSize ?? AdminPagination.DefaultPageSize;
        if (resolvedPageNumber < 1)
        {
            throw new Phase5ValidationException("Validation failed.", ["PageNumber must be at least 1."]);
        }

        if (resolvedPageSize is < 1 or > AdminPagination.MaximumPageSize)
        {
            throw new Phase5ValidationException("Validation failed.", ["PageSize must be between 1 and 100."]);
        }

        return new AdminPagination(resolvedPageNumber, resolvedPageSize);
    }
}
