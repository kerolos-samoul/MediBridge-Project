using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MediBridge.APIs.OpenApi;

public sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Identify mutation endpoints (POST, PUT, PATCH, DELETE)
        var isMutation = context.ApiDescription.HttpMethod?.ToUpper()
            is "POST" or "PUT" or "PATCH" or "DELETE";

        if (!isMutation)
        {
            return;
        }

        var requiresIdempotencyKey = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<RequireIdempotencyKeyAttribute>()
            .Any();
        if (!requiresIdempotencyKey && IsPhase8ReadTrackingEndpoint(context))
        {
            return;
        }

        operation.Parameters ??= [];

        // Check if Idempotency-Key parameter already exists
        if (operation.Parameters.Any(parameter =>
                string.Equals(parameter.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)
                && parameter.In == ParameterLocation.Header))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Description = "Unique identifier for idempotent request handling",
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = "string"
            }
        });
    }

    private static bool IsPhase8ReadTrackingEndpoint(OperationFilterContext context)
    {
        return string.Equals(context.ApiDescription.HttpMethod, "PUT", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                context.ApiDescription.RelativePath?.Trim('/'),
                "api/doctor/messages/{deliveryId}/read",
                StringComparison.OrdinalIgnoreCase);
    }
}
