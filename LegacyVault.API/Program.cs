using LegacyVault.DAL.Context;
using Microsoft.EntityFrameworkCore;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LegacyVault API",
        Version = "v1",
        Description = "First call POST /api/auth/login over HTTPS. The browser stores the session cookie and sends it on subsequent requests. Beneficiaries must verify OTP before accessing inherited assets. Upload endpoints accept multipart/form-data; signatures are detached RSA-SHA256 binary files."
    });
    options.OperationFilter<LegacyVault.API.Swagger.MutationHeaderFilter>();
});
var cookieKeys = builder.Services.AddDataProtection()
    .SetApplicationName("LegacyVault")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "auth-keys")));
if (OperatingSystem.IsWindows()) cookieKeys.ProtectKeysWithDpapi();
builder.Services.AddScoped<IVaultRepository, VaultRepository>();
builder.Services.AddScoped<VaultService>();
builder.Services.AddScoped<IdentityService>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Mail").Get<MailOptions>() ?? new MailOptions());
builder.Services.AddSingleton<DocumentProtection>();
builder.Services.AddSingleton<IOtpSender, SmtpOtpSender>();
builder.Services.AddSingleton<OtpService>();
builder.Services.AddSingleton<IDocumentStore>(new DocumentStore(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "documents")));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "LegacyVault.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    foreach (var policy in new[] { "login", "otp" })
        options.AddPolicy(policy, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// Reads environment variables and Development User Secrets.
// Startup does not open a connection or modify the database.
builder.Services.AddDbContext<LegacyVaultDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("LegacyVault");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new WorkflowException(503,
            "Missing ConnectionStrings:LegacyVault. Configure User Secrets or ConnectionStrings__LegacyVault.");
    }
    options.UseSqlServer(connectionString);
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    try
    {
        // A custom header prevents cross-site form submissions when using cookie authentication.
        if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) &&
            !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method) &&
            context.Request.Headers["X-Vault-Request"] != "1")
            throw new WorkflowException(400, "Set X-Vault-Request: 1 on mutation requests.");
        await next(context);
    }
    catch (WorkflowException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        await context.Response.WriteAsJsonAsync(new { status = ex.StatusCode, detail = ex.Message });
    }
    catch (RepositoryConflictException)
    {
        context.Response.StatusCode = 409;
        await context.Response.WriteAsJsonAsync(new { status = 409, detail = "The resource changed. Reload and retry." });
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError(ex, "API operation failed");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { status = 500, detail = "The operation could not be completed." });
    }
});
app.UseHttpsRedirection();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "LegacyVault API v1");
        options.UseRequestInterceptor("(request) => { request.headers['X-Vault-Request'] = '1'; request.credentials = 'same-origin'; return request; }");
    });
}
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Run();
