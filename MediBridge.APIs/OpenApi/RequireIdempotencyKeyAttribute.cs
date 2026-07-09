namespace MediBridge.APIs.OpenApi;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequireIdempotencyKeyAttribute : Attribute
{
}
