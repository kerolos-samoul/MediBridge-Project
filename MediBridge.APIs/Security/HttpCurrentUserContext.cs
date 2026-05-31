using System.Security.Claims;
using MediBridge.APIs.Middleware;
using MediBridge.Core.Interfaces;

namespace MediBridge.APIs.Security;

public sealed class HttpCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public string? UserId => IsAuthenticated
        ? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")
        : null;

    public string? Role => IsAuthenticated
        ? User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role")
        : null;

    public bool? IsApproved
    {
        get
        {
            if (!IsAuthenticated)
            {
                return null;
            }

            var value = User.FindFirstValue("IsApproved") ?? User.FindFirstValue("is_approved");
            return bool.TryParse(value, out var isApproved) ? isApproved : null;
        }
    }

    public string? CorrelationId => _httpContextAccessor.HttpContext is { } context
        ? CorrelationIdMiddleware.GetCorrelationId(context)
        : null;

    private ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
}
