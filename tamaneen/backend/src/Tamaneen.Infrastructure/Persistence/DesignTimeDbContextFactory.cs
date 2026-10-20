using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` only. Reads TAMANEEN_SQL; migrations never touch patient rows.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TamaneenDbContext>
{
    public TamaneenDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("TAMANEEN_SQL")
            ?? "Server=localhost,14333;Database=Tamaneen;User Id=sa;Password=change-me;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<TamaneenDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", Schemas.Security))
            .AddInterceptors(new SessionContextInterceptor(StaticCurrentUser.Anonymous))
            .Options;
        return new TamaneenDbContext(options);
    }
}
