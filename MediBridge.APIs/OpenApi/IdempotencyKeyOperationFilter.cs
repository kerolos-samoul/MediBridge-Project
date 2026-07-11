using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MediBridge.APIs.OpenApi;

public sealed class IdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiresIdempotencyKey = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<RequireIdempotencyKeyAttribute>()
            .Any();
        if (!requiresIdempotencyKey)
        {
            return;
        }

        operation.Parameters ??= [];
        var existing = operation.Parameters.FirstOrDefault(parameter =>
                string.Equals(parameter.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)
                && parameter.In == ParameterLocation.Header);
        if (existing is not null)
        {
            existing.Required = true;
            existing.Description ??= "Required idempotency key for replay-safe request handling. Must be 8 to 128 characters.";
            existing.Schema ??= new OpenApiSchema { Type = "string" };
            existing.Schema.MinLength = 8;
            existing.Schema.MaxLength = 128;
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "Idempotency-Key",
            In = ParameterLocation.Header,
            Description = "Required idempotency key for replay-safe request handling. Must be 8 to 128 characters.",
            Required = true,
            Schema = new OpenApiSchema
            {
                Type = "string",
                MinLength = 8,
                MaxLength = 128
            }
        });
    }
}
