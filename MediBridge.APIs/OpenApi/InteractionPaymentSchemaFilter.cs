using MediBridge.Services.DTOs.Messaging;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MediBridge.APIs.OpenApi;

public sealed class InteractionPaymentSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == typeof(InteractDeliveryRequestDto))
        {
            schema.Required.Add(nameof(InteractDeliveryRequestDto.Decision));
            if (TryGetProperty(schema, nameof(InteractDeliveryRequestDto.FeedbackText), out var feedback))
            {
                ApplyFeedbackContract(feedback);
            }
        }
        else if (context.Type == typeof(InteractDeliveryResultDto)
            && TryGetProperty(schema, nameof(InteractDeliveryResultDto.FeedbackText), out var resultFeedback))
        {
            ApplyFeedbackContract(resultFeedback);
        }
    }

    private static void ApplyFeedbackContract(OpenApiSchema feedback)
    {
        feedback.Nullable = true;
        feedback.MaxLength = 1000;
        feedback.Description ??= "Trimmed before storage; empty after trimming is treated as absent.";
    }

    private static bool TryGetProperty(OpenApiSchema schema, string propertyName, out OpenApiSchema property)
    {
        if (schema.Properties.TryGetValue(propertyName, out property!))
        {
            return true;
        }

        var match = schema.Properties
            .FirstOrDefault(item => string.Equals(item.Key, propertyName, StringComparison.OrdinalIgnoreCase));
        property = match.Value;
        return property is not null;
    }
}
