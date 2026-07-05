using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MediBridge.APIs.OpenApi;

public sealed class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var authAttributes = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<AuthorizeAttribute>()
            .ToList();
        var anonymous = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<AllowAnonymousAttribute>()
            .Any();

        if (anonymous || authAttributes.Count == 0)
        {
            return;
        }

        operation.Security ??= [];
        operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized" });
        operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden" });

        var requirements = authAttributes
            .SelectMany(auth => BuildRequirement(auth))
            .ToList();

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "bearerAuth"
                }
            }] = requirements
        });
    }

    private static string[] BuildRequirement(AuthorizeAttribute authorize)
    {
        var roles = SplitRoles(authorize.Roles);
        var policy = authorize.Policy;

        if (roles.Count > 0 && !string.IsNullOrWhiteSpace(policy))
        {
            return [$"Policy: {policy}", $"Roles: {string.Join(", ", roles)}"];
        }

        if (roles.Count > 0)
        {
            return [$"Roles: {string.Join(", ", roles)}"];
        }

        if (!string.IsNullOrWhiteSpace(policy))
        {
            return [$"Policy: {policy}"];
        }

        return [];
    }

    private static IReadOnlyList<string> SplitRoles(string? roles)
    {
        if (string.IsNullOrWhiteSpace(roles))
        {
            return [];
        }

        return roles.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(role => role.Trim())
            .Where(role => role.Length > 0)
            .ToList();
    }
}
