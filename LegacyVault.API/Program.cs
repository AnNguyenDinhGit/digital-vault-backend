using LegacyVault.DAL.Context;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Reads environment variables and Development User Secrets.
// Startup does not open a connection or modify the database.
builder.Services.AddDbContext<LegacyVaultDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("LegacyVault");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Missing ConnectionStrings:LegacyVault. Configure User Secrets or ConnectionStrings__LegacyVault.");
    }
    options.UseSqlServer(connectionString);
});

var app = builder.Build();
app.UseHttpsRedirection();
app.Run();
