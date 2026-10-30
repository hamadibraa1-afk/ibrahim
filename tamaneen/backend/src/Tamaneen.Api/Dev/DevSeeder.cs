using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Auth;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Hosting;
using Tamaneen.Identity;
using Tamaneen.Identity.Auth;
using Tamaneen.Infrastructure.Auth;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Dev;

/// <summary>
/// Development and test data: staff who can sign in to the portal (known password and authenticator secret),
/// the therapist directory and two weeks of bookable times. Runs only in Development and Testing, is
/// idempotent, and never touches patient data. Production staff are provisioned through S05–S12.
/// </summary>
public sealed partial class DevSeeder(IServiceScopeFactory scopes, IHostEnvironment environment, ILogger<DevSeeder> logger) : IHostedService
{
    /// <summary>Password of every seeded staff account (development only).</summary>
    public const string StaffPassword = "Tamaneen-Dev-2026!";

    /// <summary>Authenticator secret of every seeded staff account (development only), base32.</summary>
    public static readonly byte[] StaffTotpSecret = "tamaneen-dev-totp-20b"u8.ToArray()[..20];

    public static readonly Guid SchedulerId = new("2f8d4c1a-7e3b-4a69-b5d0-8c1e9f2a7b44");

    public sealed record SeedStaff(string Email, string Role, string NameAr, string NameEn, byte? OnCallTier, SeedTherapist? Therapist);

    public sealed record SeedTherapist(string TitleAr, string TitleEn, string BioAr, string BioEn, string Gender, string[] Specialties, string[] Languages);

    public static readonly IReadOnlyList<SeedStaff> Staff =
    [
        new("sara@staff.tamaneen.dev", AppRoles.Therapist, "أ. سارة م.", "Sara M.", null, new(
            "أخصائية نفسية إكلينيكية", "Clinical psychologist",
            "أعمل مع البالغين الذين يمرّون بالقلق وضغط العمل ومشكلات النوم، بالعلاج المعرفي السلوكي وتمارين اليقظة. في الجلسة الأولى نتعارف ونحدد معًا ما تريد أن يتغيّر.",
            "I work with adults facing anxiety, work stress and sleep problems, using CBT and mindfulness exercises. In the first session we get to know each other and agree on what you want to change.",
            "female", ["anxiety", "stress", "sleep", "cbt"], ["ar", "en"])),
        new("khalid@staff.tamaneen.dev", AppRoles.Therapist, "أ. خالد ع.", "Khalid A.", 0, new(
            "أخصائي نفسي", "Psychologist",
            "أساعد على تعلّم طرق عملية لتهدئة القلق، مع مواعيد مسائية لمن يعمل في النهار.",
            "I help people learn practical ways to calm anxiety, with evening times for those who work during the day.",
            "male", ["anxiety", "mindfulness"], ["ar"])),
        new("noura@staff.tamaneen.dev", AppRoles.Therapist, "أ. نورة ح.", "Noura H.", null, new(
            "أخصائية نفسية", "Psychologist",
            "أركّز على الضغط المهني والتكيّف مع التغيّرات الكبيرة في الحياة.",
            "I focus on work stress and adjusting to big changes in life.",
            "female", ["work_stress", "adjustment", "stress"], ["ar", "en"])),
        new("mona@staff.tamaneen.dev", AppRoles.Therapist, "أ. منى ف.", "Mona F.", null, new(
            "أخصائية نفسية إكلينيكية", "Clinical psychologist",
            "أعمل مع القلق الاجتماعي والاكتئاب بالعلاج المعرفي السلوكي.",
            "I work with social anxiety and depression using CBT.",
            "female", ["social_anxiety", "depression", "cbt"], ["ar", "en"])),
        new("fahad@staff.tamaneen.dev", AppRoles.Supervisor, "د. فهد ر.", "Dr. Fahad R.", 1, null),
        new("lead@staff.tamaneen.dev", AppRoles.ClinicalLead, "د. هند س.", "Dr. Hind S.", null, null),
    ];

    private static readonly int[] SlotHours = [10, 11, 13, 16, 17, 19];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return;
        }

        try
        {
            await SeedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
        {
            // An unmigrated development database must not stop the host; the developer sees why in the log.
            LogSeedFailed(logger, ex.GetType().Name, ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SeedAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        // Staff and directory tables are outside row-level security; care assignments are not touched.
        scope.ServiceProvider.GetRequiredService<RequestCurrentUser>().ActAsService(SchedulerId, AppRoles.SchedulingService);
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<TamaneenDbContext>();
        var hasher = sp.GetRequiredService<IContactHasher>();
        var passwords = sp.GetRequiredService<PasswordService>();
        var secrets = sp.GetRequiredService<ServerSecrets>();
        var clock = sp.GetRequiredService<IClock>();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(sp.GetRequiredService<IOptions<PlatformOptions>>().Value.TimeZone);
        var created = 0;

        foreach (var s in Staff)
        {
            var lookup = hasher.Hash(s.Email);
            var account = await db.UserAccounts.FirstOrDefaultAsync(a => a.ContactLookupHash == lookup, ct).ConfigureAwait(false);
            if (account is null)
            {
                account = UserAccount.Register(s.Role, lookup, "ar", clock);
                account.Activate();
                db.UserAccounts.Add(account);
                var credential = Credential.Create(account.Id, passwords.Hash(StaffPassword), clock);
                credential.BeginTotpSetup(secrets.Protect(StepUpVerifier.TotpPurpose, Convert.ToBase64String(StaffTotpSecret)));
                credential.EnableTotp(Totp.StepAt(clock.UtcNow) - 10);
                db.Credentials.Add(credential);
                db.StaffProfiles.Add(StaffProfile.Create(account.Id, s.NameAr, s.NameEn, s.OnCallTier));
                if (s.Therapist is { } t)
                {
                    db.TherapistProfiles.Add(TherapistProfile.Create(account.Id, t.TitleAr, t.TitleEn, t.BioAr, t.BioEn, t.Gender, t.Specialties, t.Languages, licenseVerified: true));
                }

                created++;
            }

            if (s.Therapist is not null)
            {
                await EnsureSlotsAsync(db, account.Id, zone, clock, ct).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // S21: verified licences carry an expiry date and who verified them (the clinical lead). Mona's runs out
        // within 30 days, so the warning shows in development.
        var leadLookup = hasher.Hash("lead@staff.tamaneen.dev");
        var leadId = await db.UserAccounts.Where(a => a.ContactLookupHash == leadLookup).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (leadId is { } lead)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
            foreach (var s in Staff.Where(x => x.Therapist is not null))
            {
                var lookup = hasher.Hash(s.Email);
                var id = await db.UserAccounts.Where(a => a.ContactLookupHash == lookup).Select(a => a.Id).FirstAsync(ct).ConfigureAwait(false);
                var profile = await db.TherapistProfiles.FirstAsync(t => t.UserId == id, ct).ConfigureAwait(false);
                if (profile.LicenseVerified && profile.LicenseExpiresOn is null)
                {
                    var expires = s.Email.StartsWith("mona@", StringComparison.Ordinal) ? today.AddDays(20) : new DateOnly(2028, 3, 14);
                    profile.SetLicenseVerification(true, expires, today, lead, clock);
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        LogSeeded(logger, created);
    }

    /// <summary>Sunday to Thursday, six times a day, for the next 14 days (Riyadh time).</summary>
    private static async Task EnsureSlotsAsync(TamaneenDbContext db, Guid therapistId, TimeZoneInfo zone, IClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var existing = (await db.AvailabilitySlots.Where(s => s.TherapistUserId == therapistId && s.StartsAtUtc > now).Select(s => s.StartsAtUtc).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        for (var d = 0; d < 14; d++)
        {
            var day = today.AddDays(d);
            if (day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
            {
                continue;
            }

            foreach (var hour in SlotHours)
            {
                var local = day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified);
                var start = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
                if (start > now && !existing.Contains(start))
                {
                    db.AvailabilitySlots.Add(AvailabilitySlot.Create(therapistId, start, 50));
                }
            }
        }
    }

    [LoggerMessage(EventId = 4901, Level = LogLevel.Information, Message = "Development seed: {Count} staff accounts created; slots topped up.")]
    private static partial void LogSeeded(ILogger logger, int count);

    [LoggerMessage(EventId = 4902, Level = LogLevel.Warning, Message = "Development seed skipped ({Error}): {Message}")]
    private static partial void LogSeedFailed(ILogger logger, string error, string message);
}
