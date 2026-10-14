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
using LegacyVault.API.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LegacyVault API",
        Version = "v1",
        Description = "Login returns a Bearer accessToken, or 202 with a mandatory 2FA challenge. Use Authorize with the JWT. Google uses temporary browser cookies only until OTP completes."
    });
    options.OperationFilter<LegacyVault.API.Swagger.MutationHeaderFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() });
});
var cookieKeys = builder.Services.AddDataProtection()
    .SetApplicationName("LegacyVault")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "auth-keys")));
if (OperatingSystem.IsWindows()) cookieKeys.ProtectKeysWithDpapi();
builder.Services.AddScoped<IVaultRepository, VaultRepository>();
builder.Services.AddScoped<VaultService>();
builder.Services.AddScoped<IWorkflowRepository, WorkflowRepository>();
builder.Services.AddScoped<ManagementService>();
builder.Services.AddScoped<IBeneficiaryService, BeneficiaryService>();
builder.Services.AddScoped<IdentityService>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions());
builder.Services.AddSingleton(builder.Configuration.GetSection("Mail").Get<MailOptions>() ?? new MailOptions());
builder.Services.AddSingleton<DocumentProtection>();
builder.Services.AddSingleton<IOtpSender, SmtpOtpSender>();
builder.Services.AddSingleton<OtpService>();
builder.Services.AddSingleton<IDocumentStore>(new DocumentStore(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "documents")));
builder.Services.AddVaultJwt(builder.Configuration);
builder.Services.AddAuthentication().AddCookie("GoogleTemporary", options => { options.Cookie.SecurePolicy = CookieSecurePolicy.Always; });
builder.Services.AddAuthorization();
builder.Services.AddGoogleEmailAuthentication(builder.Configuration);
builder.Services.AddVaultCors(builder.Configuration);
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
app.UseVaultErrors();
app.UseHttpsRedirection();

// Bật Swagger cho mọi môi trường (kể cả Production khi chạy Docker)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LegacyVault API v1");
    options.UseRequestInterceptor("(request) => { request.headers['X-Vault-Request'] = '1'; request.credentials = 'include'; return request; }");
});

app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Run();
