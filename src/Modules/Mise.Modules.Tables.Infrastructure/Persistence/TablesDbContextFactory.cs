using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mise.Modules.Tables.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations add` — the CLI builds this directly instead of
/// spinning up Mise.ApiService's DI container. The connection string here is never used at
/// runtime; only the schema/model matters for generating migration files.
/// </summary>
public sealed class TablesDbContextFactory : IDesignTimeDbContextFactory<TablesDbContext>
{
    public TablesDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TablesDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=misedb;Username=postgres;Password=postgres",
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", "tables"));

        return new TablesDbContext(optionsBuilder.Options);
    }
}
