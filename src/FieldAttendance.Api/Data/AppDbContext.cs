using FieldAttendance.Api.Common;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FieldAttendance.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IClock clock, ICurrentUser currentUser) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<ShiftTemplate> ShiftTemplates => Set<ShiftTemplate>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentOverride> AssignmentOverrides => Set<AssignmentOverride>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<TemporaryExit> TemporaryExits => Set<TemporaryExit>();
    public DbSet<PermissionRequest> PermissionRequests => Set<PermissionRequest>();
    public DbSet<AttendanceExceptionRequest> AttendanceExceptionRequests => Set<AttendanceExceptionRequest>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<AllowanceType> AllowanceTypes => Set<AllowanceType>();
    public DbSet<EmployeeAllowance> EmployeeAllowances => Set<EmployeeAllowance>();
    public DbSet<SupervisorLocation> SupervisorLocations => Set<SupervisorLocation>();
    public DbSet<Domain.Entities.Feedback> Feedback => Set<Domain.Entities.Feedback>();

    // HR module
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<JobTitle> JobTitles => Set<JobTitle>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<ContractType> ContractTypes => Set<ContractType>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();
    public DbSet<SalaryChange> SalaryChanges => Set<SalaryChange>();
    public DbSet<SalaryAllowance> SalaryAllowances => Set<SalaryAllowance>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<DeductionType> DeductionTypes => Set<DeductionType>();
    public DbSet<WarningLevel> WarningLevels => Set<WarningLevel>();
    public DbSet<DeductionProposal> DeductionProposals => Set<DeductionProposal>();
    public DbSet<Warning> Warnings => Set<Warning>();
    public DbSet<PayrollCycle> PayrollCycles => Set<PayrollCycle>();
    public DbSet<PayrollLine> PayrollLines => Set<PayrollLine>();
    public DbSet<ApprovalFlow> ApprovalFlows => Set<ApprovalFlow>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<ReturnToWork> ReturnsToWork => Set<ReturnToWork>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();

    /// <summary>
    /// Every instant is stored in UTC, whatever offset it arrived with, and read back in UTC.
    /// Local time is a presentation concern handled by OrgTime, so a server in another zone
    /// — or a daylight-saving change — cannot shift a recorded check-in.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> UtcConverter =
        new(value => value.ToUniversalTime(), value => value.ToUniversalTime());

    private static readonly ValueConverter<DateTimeOffset?, DateTimeOffset?> NullableUtcConverter =
        new(value => value == null ? null : value.Value.ToUniversalTime(),
            value => value == null ? null : value.Value.ToUniversalTime());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset)) property.SetValueConverter(UtcConverter);
                else if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(NullableUtcConverter);
            }
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            var entity = modelBuilder.Entity(entityType.ClrType);
            entity.HasKey(nameof(Entity.Id));
            entity.Property(nameof(Entity.Id)).ValueGeneratedNever();
            entity.Property(nameof(Entity.RowVersion)).IsRowVersion();
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.Now;
        var userId = currentUser.Id;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy ??= userId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userId;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Autosave/concurrent edits: compare against the version the client last saw.</summary>
    public void ExpectVersion(Entity entity, byte[]? rowVersion)
    {
        if (rowVersion is { Length: > 0 })
            Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;
    }
}
