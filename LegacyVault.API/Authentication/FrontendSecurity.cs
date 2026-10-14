using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Repositories;

namespace LegacyVault.API.Authentication;

public static class FrontendSecurity
{
    public static IServiceCollection AddVaultCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
        if (origins.Any(x => x == "*" || !Uri.TryCreate(x, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/" || uri.Scheme is not ("http" or "https")))
            throw new InvalidOperationException("Cors:Origins must be explicit HTTP(S) origins without paths or wildcard.");
        services.AddCors(options => options.AddPolicy("Frontend", policy =>
        {
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithExposedHeaders("Location", "X-Signature-Valid", "Content-Disposition");
        }));
        return services;
    }
    public static IApplicationBuilder UseVaultErrors(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        try
        {
            if (context.Request.Path.StartsWithSegments("/api/auth/google") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS") && context.Request.Headers["X-Vault-Request"] != "1")
                throw new WorkflowException(400, "Set X-Vault-Request: 1 for Google cookie-bound mutations.");
            await next(context);
        }
        catch (WorkflowException ex) { context.Response.StatusCode = ex.StatusCode; await context.Response.WriteAsJsonAsync(new { status = ex.StatusCode, detail = ex.Message }); }
        catch (RepositoryConflictException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { status = 409, detail = "Resource changed concurrently; reload and retry." }); }
        catch (RepositoryDuplicateException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { status = 409, detail = "Duplicate resource." }); }
        catch (Exception ex) when (ex is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("VaultApi").LogError("API failed ({ExceptionType})", ex.GetType().Name);
            context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { status = 500, detail = "Operation could not be completed." });
        }
    });
}
