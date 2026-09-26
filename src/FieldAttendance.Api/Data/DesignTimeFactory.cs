using FieldAttendance.Api.Common;
using FieldAttendance.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FieldAttendance.Api.Data;

/// <summary>
/// Lets "dotnet ef" build the model without starting the application.
/// The connection string here is only used to read the provider; migrations are applied
/// at runtime against whatever connection appsettings supplies.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("FIELDATTENDANCE_CONNECTION")
            ?? "Server=localhost\\SQLEXPRESS;Database=FieldAttendance;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection)
            .Options;

        // The context needs a clock and a user for auditing; at design time nothing is written,
        // so a plain clock and an empty user are enough to build the model.
        return new AppDbContext(options, new DesignTimeClock(), new DesignTimeUser());
    }

    private sealed class DesignTimeClock : IClock
    {
        public DateTimeOffset Now => DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private sealed class DesignTimeUser : ICurrentUser
    {
        public Guid? Id => null;
        public Guid RequiredId => throw new InvalidOperationException("No user at design time.");
    }
}
