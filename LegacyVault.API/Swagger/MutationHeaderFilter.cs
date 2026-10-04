using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LegacyVault.API.Swagger;

public sealed class MutationHeaderFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.HttpMethod is "GET" or "HEAD" or "OPTIONS") return;
        operation.Parameters ??= new List<OpenApiParameter>();
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Vault-Request",
            In = ParameterLocation.Header,
            Required = true,
            Description = "Required mutation header. Swagger UI sets this automatically.",
            Schema = new OpenApiSchema { Type = "string", Default = new OpenApiString("1"), Enum = new List<IOpenApiAny> { new OpenApiString("1") } }
        });
    }
}
