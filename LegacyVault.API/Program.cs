using LegacyVault.DAL.Context;
using Microsoft.EntityFrameworkCore;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.OpenApi.Models;
using LegacyVault.API.Authentication;
using LegacyVault.BLL.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 1. Đăng ký các Services Bảo mật & JWT
builder.Services.AddSingleton<IAccountEncryptionService, AccountEncryptionService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// 2. Cấu hình Swagger UI hỗ trợ nút "Authorize" (Bearer Token)
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LegacyVault API",
        Version = "v1",
        Description = "LegacyVault API with JWT Bearer Authentication & AES Encryption."
    });

    // Thêm cấu hình nút Authorize trên Swagger UI
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập Access Token theo định dạng: Bearer {your_token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
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

// 3. Chuyển Cấu hình Authentication từ Cookie sang JWT Bearer
var jwtKey = builder.Configuration["Jwt:Key"] ?? "LegacyVault_Secret_JWT_Key_For_Signing_Tokens_2026_Super_Secret!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "LegacyVaultAPI";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "LegacyVaultClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();
builder.Services.AddGoogleEmailAuthentication(builder.Configuration);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    foreach (var policy in new[] { "login", "otp" })
        options.AddPolicy(policy, ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

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

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "LegacyVault API v1");
    options.UseRequestInterceptor("(request) => { request.headers['X-Vault-Request'] = '1'; return request; }");
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.Run();