using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MediBridge.APIs.OpenApi;

public sealed class BearerSecurityDocumentFilter : IDocumentFilter
{
    public const string SchemeName = "Bearer";

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Description = "JWT Authorization header using the Bearer scheme.",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };
    }
}
