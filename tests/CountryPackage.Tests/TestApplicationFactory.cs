using CountryPackage.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CountryPackage.Tests;

public sealed class TestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"country-package-tests-{Guid.NewGuid():N}.db");
    public string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = $"Data Source={_databasePath}",
                ["Storage:SourceDirectory"] = Path.Combine(RepositoryRoot, "sources")
            });
        });
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(x => x.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null) services.Remove(descriptor);
            services.AddSingleton<AuditFailureSwitch>();
            services.AddSingleton<AuditFailureInterceptor>();
            services.AddDbContext<AppDbContext>((provider, options) => options
                .UseSqlite($"Data Source={_databasePath}")
                .AddInterceptors(provider.GetRequiredService<AuditFailureInterceptor>()));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public sealed class AuditFailureSwitch
{
    public bool Enabled { get; set; }
}

public sealed class AuditFailureInterceptor(AuditFailureSwitch failure) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (failure.Enabled && eventData.Context?.ChangeTracker.Entries<CountryPackage.Api.Domain.AuditEntryEntity>()
                .Any(x => x.State == EntityState.Added) == true)
            throw new InvalidOperationException("Injected audit persistence failure.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
