using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FieldAttendance.Api.Auth;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Api.Features.Attendance;
using FieldAttendance.Api.Features.Requests;
using FieldAttendance.Api.Features.Schedule;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwt.Key) < 32)
    throw new InvalidOperationException("Jwt:Key must be at least 32 bytes. Set it via user-secrets or an environment variable.");

builder.Services.Configure<AttendanceOptions>(config.GetSection("Attendance"));
builder.Services.Configure<JwtOptions>(config.GetSection("Jwt"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<AccessScope>();
builder.Services.AddScoped<SelfServiceScope>();
builder.Services.AddScoped<PayrollLock>();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(config.GetConnectionString("Default"), sql => sql.EnableRetryOnFailure(3)));

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<ScheduleSnapshotLoader>();
builder.Services.AddScoped<MaterializationService>();
builder.Services.AddScoped<RecalculationService>();
builder.Services.AddScoped<AttendanceJobs>();
builder.Services.AddScoped<ComplianceService>();
builder.Services.AddScoped<ScheduleService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Requests.RequestInbox>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Leaves.LeaveService>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Hr.OfficeScheduleService>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Discipline.DeductionService>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Payroll.PayrollService>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Payroll.SalaryLookup>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Approvals.ApprovalService>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Notifications.NotificationService>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Notifications.INotificationChannel, FieldAttendance.Api.Features.Notifications.LoggingEmailChannel>();
builder.Services.AddScoped<FieldAttendance.Api.Features.Notifications.INotificationChannel, FieldAttendance.Api.Features.Notifications.LoggingSmsChannel>();
builder.Services.AddHostedService<FieldAttendance.Api.Features.Notifications.NotificationWorker>();
builder.Services.AddHostedService<AttendanceWorker>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.Converters.Add(new OrgTimeJsonConverter());
    });

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = AppClaims.Name,
        RoleClaimType = AppClaims.Role,
    };
});
builder.Services.AddAppAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5) }));
});

builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Field Attendance API", Version = "v1" });
    o.CustomSchemaIds(t => t.FullName);
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    o.AddSecurityDefinition("Bearer", scheme);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});

// The working time zone is configuration, not a constant: business hours mean local hours.
var configuredZone = builder.Configuration["Attendance:TimeZone"];
var app = builder.Build();
if (!string.IsNullOrWhiteSpace(configuredZone) && !FieldAttendance.Domain.Time.OrgTime.Configure(configuredZone))
    app.Logger.LogWarning("Unknown time zone '{Zone}'; keeping {Fallback}.", configuredZone, FieldAttendance.Domain.Time.OrgTime.Zone.Id);

app.UseMiddleware<ErrorHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseRouting();
app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Schema management.
//
// Migrations are the production path: each release applies only what changed and the data stays.
// EnsureCreated remains available for a throwaway development database, where the schema is
// rebuilt rather than migrated, and it is refused outside Development.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var useMigrations = config.GetValue("Database:UseMigrations", true);
    var reset = config.GetValue<bool>("ResetDatabaseOnStart")
                || config.GetValue<bool>("reset")
                || args.Any(a => a.Equals("--reset", StringComparison.OrdinalIgnoreCase));

    if (reset)
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("Database reset is only allowed in the Development environment.");
        app.Logger.LogWarning("Reset requested: dropping the database and recreating it with demo data.");
        await db.Database.EnsureDeletedAsync();
    }

    if (useMigrations && db.Database.GetMigrations().Any())
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
            app.Logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync();
    }
    else
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "No migrations found. Create the initial migration before running in production: dotnet ef migrations add Initial");
        app.Logger.LogWarning("No migrations found; creating the schema directly. This is development only.");
        await db.Database.EnsureCreatedAsync();
    }

    if (app.Environment.IsDevelopment() && config.GetValue<bool>("SeedDemoData"))
        await DevSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IClock>());

    // Pending requests filed before permissions went through approval chains would otherwise sit
    // in nobody's inbox. Idempotent, so it is safe on every start and in production.
    await scope.ServiceProvider.GetRequiredService<FieldAttendance.Api.Features.Approvals.ApprovalService>()
        .StartMissingChainsAsync(CancellationToken.None);
}

await app.RunAsync();

// Top-level statements generate an internal Program; the integration tests host the real
// pipeline through WebApplicationFactory<Program>, which needs to see it.
public partial class Program;
