using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LegacyVault.API.Swagger;

public sealed class MutationHeaderFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.HttpMethod is "GET" or "HEAD" or "OPTIONS") return;
        if (!context.ApiDescription.RelativePath?.StartsWith("api/auth/google", StringComparison.OrdinalIgnoreCase) ?? true) return;
        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Vault-Request",
            In = ParameterLocation.Header,
            Required = true,
            Description = "Required only for Google temporary-cookie mutations. Bearer API mutations do not require this header.",
            Schema = new OpenApiSchema { Type = "string", Default = new OpenApiString("1"), Enum = new List<IOpenApiAny> { new OpenApiString("1") } }
        });
    }
}
