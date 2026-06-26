using System.Text.Json;

namespace MediBridge.Core.Entities.Policies;

public static class AuditMetadataRules
{
    private static readonly string[] ForbiddenTerms =
    [
        "password",
        "plaintexttoken",
        "plain_text_token",
        "token",
        "requestbody",
        "request_body",
        "responsebody",
        "response_body",
        "secret",
        "payload",
        "storagekey",
        "storage_key",
        "stacktrace",
        "stack_trace"
    ];

    public static string? EnsureSafe(string? metadata, string paramName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return metadata;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            ValidateElement(document.RootElement, paramName);
        }
        catch (JsonException)
        {
            RejectIfForbidden(metadata, paramName);
        }

        return metadata;
    }

    private static void ValidateElement(JsonElement element, string paramName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    RejectIfForbidden(property.Name, paramName);
                    ValidateElement(property.Value, paramName);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ValidateElement(item, paramName);
                }

                break;
            case JsonValueKind.String:
                RejectIfForbidden(element.GetString(), paramName);
                break;
        }
    }

    private static void RejectIfForbidden(string? value, string paramName)
    {
        if (value is null)
        {
            return;
        }

        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        if (ForbiddenTerms.Any(term => normalized.Contains(term.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal)))
        {
            throw new ArgumentException("Audit metadata cannot contain passwords, plaintext tokens, request bodies, response bodies, or secrets.", paramName);
        }
    }
}
