using Tamaneen.Infrastructure.Auth;
using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Auth;
using Tamaneen.Api.Patients;
using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Intake;
using Tamaneen.Hosting;
using Tamaneen.Identity;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Endpoints;

/// <summary>The signed-in patient's own account: onboarding state, consents, safety info, security, notifications, data rights.</summary>
public static class MeEndpoints
{
    /// <summary>Service identity recorded when a patient's own deletion request is executed.</summary>
    public static readonly Guid DataProtectionServiceId = new("5d1e8f4a-0b6c-4a7e-9e2d-3c8b7a1f0e42");

    public sealed record ChangeLanguageRequest(string? Language);

    public sealed record ChangeLanguageResponse(string Language, string Dir, string Message);

    public sealed record OnboardingDto(bool ConsentsDone, bool SafetyInfoDone, string? Path, bool ScreeningDone, string IntakeStatus, string TriageStatus);

    public sealed record MeDto(string PseudonymId, string Nickname, string Language, string TimeZone, OnboardingDto Onboarding, bool ChatLocked);

    public sealed record UpdateProfileRequest(string? Nickname, string? TimeZone);

    public sealed record PathRequest(string? Path);

    public sealed record ConsentDto(string Key, bool Required, bool Granted, string Version, DateTimeOffset? UpdatedAt, bool Withdrawable);

    public sealed record ConsentItem(string? Key, bool Granted);

    public sealed record UpdateConsentsRequest(List<ConsentItem>? Items);

    public sealed record SafetyInfoDto(string? ContactPhone, string? TrustedName, string? TrustedPhone, bool ShareLocation, DateTimeOffset? ShareLocationSince);

    public sealed record UpdateSafetyInfoRequest(string? ContactPhone, string? TrustedName, string? TrustedPhone, bool ShareLocation);

    public sealed record SessionDto(Guid Id, string DeviceLabel, bool Current, DateTimeOffset LastActiveAt);

    public sealed record SecurityDto(DateTimeOffset PasswordChangedAt, bool TotpEnabled, int RecoveryCodesRemaining, IReadOnlyList<SessionDto> Sessions);

    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public sealed record TotpSetupDto(string Secret, string OtpauthUri);

    public sealed record CodeRequest(string? Code);

    public sealed record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

    public sealed record NotificationPrefsDto(bool Push, bool Sms, bool Email, bool NeutralText, bool QuickExit, string HomeworkReminder, string ReminderTime, string QuietFrom, string QuietTo);

    public sealed record AccessLogEntryDto(string Actor, string Action, DateTimeOffset AtUtc);

    public sealed record DataRequestStatusDto(DateTimeOffset? ExportRequestedAt, DateTimeOffset? ExportReadyBy, bool ExportReady);

    public sealed record DeleteAccountRequest(string? Password);

    /// <summary>ExpectedBy: when the team will have reached out at the latest, from the triage SLA (PB-25).</summary>
    public sealed record ContactRequestCreated(DateTimeOffset RequestedAt, DateTimeOffset ExpectedBy, int WithinHours);

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        // Saves the user's language so notifications, crisis scripts and AI replies use it.
        app.MapPut("/v1/me/language", ChangeLanguageAsync).RequireAuthorization().WithName("ChangeMyLanguage");

        var me = app.MapGroup("/v1/me").RequireAuthorization(AuthSetup.PatientPolicy);
        me.MapGet("", GetMeAsync);
        me.MapPut("/profile", UpdateProfileAsync);
        me.MapPut("/path", ChoosePathAsync);
        me.MapPost("/contact-request", ContactRequestAsync);
        me.MapGet("/consents", GetConsentsAsync);
        me.MapPut("/consents", UpdateConsentsAsync);
        me.MapGet("/safety-info", GetSafetyInfoAsync);
        me.MapPut("/safety-info", UpdateSafetyInfoAsync);
        me.MapDelete("/safety-info/trusted-contact", DeleteTrustedContactAsync);
        me.MapGet("/security", GetSecurityAsync);
        me.MapPut("/password", ChangePasswordAsync);
        me.MapPost("/totp/setup", TotpSetupAsync);
        me.MapPost("/totp/confirm", TotpConfirmAsync);
        me.MapDelete("/totp", TotpDisableAsync);
        me.MapPost("/recovery-codes", NewRecoveryCodesAsync);
        me.MapDelete("/sessions/{id:guid}", RevokeSessionAsync);
        me.MapPost("/sessions/revoke-all", RevokeAllAsync);
        me.MapGet("/notifications", GetNotificationsAsync);
        me.MapPut("/notifications", UpdateNotificationsAsync);
        me.MapGet("/access-log", AccessLogAsync);
        me.MapGet("/data-export", DataExportStatusAsync);
        me.MapPost("/data-export", RequestDataExportAsync);
        me.MapPost("/delete-account", DeleteAccountAsync);
        return app;
    }

    internal static async Task<Results<Ok<ChangeLanguageResponse>, ValidationProblem>> ChangeLanguageAsync(
        ChangeLanguageRequest request,
        ICurrentUser user,
        TamaneenDbContext db,
        IAuditLog audit,
        IStringLocalizer<SharedResource> localizer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Language))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["language"] = new[] { localizer["validation.language.required"].Value } },
                detail: localizer["validation.failed"]);
        }

        var account = await db.UserAccounts.FirstOrDefaultAsync(a => a.Id == user.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Account {user.UserId} not found.");

        account.ChangeLanguage(request.Language);
        audit.Append("user.language_changed", "UserAccount", account.Id.ToString(), reason: account.PreferredLanguage);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The confirmation is written in the language the user just chose.
        var message = LocalizationSetup.WithLanguage(account.PreferredLanguage, () => localizer["message.language_changed"].Value);
        return TypedResults.Ok(new ChangeLanguageResponse(account.PreferredLanguage, SupportedLanguages.Direction(account.PreferredLanguage), message));
    }

    internal static async Task<Ok<MeDto>> GetMeAsync(PatientContext p, IClock clock, CancellationToken ct) => TypedResults.Ok(await BuildMeAsync(p, clock, ct).ConfigureAwait(false));

    internal static async Task<MeDto> BuildMeAsync(PatientContext p, IClock clock, CancellationToken ct)
    {
        var account = await p.AccountAsync(ct).ConfigureAwait(false);
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var nickname = await p.DecryptOptionalAsync(Fields.DisplayName, profile.DisplayNameCipher, ct).ConfigureAwait(false) ?? string.Empty;
        var intake = await p.Db.IntakeSessions.Where(i => i.PatientId == p.PatientId).OrderByDescending(i => i.StartedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (intake is not null && intake.ExpireIfStale(clock))
        {
            await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var triage = await p.Db.TriageCases.Where(t => t.PatientId == p.PatientId).OrderByDescending(t => t.SubmittedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var screened = await p.Db.Screenings.AnyAsync(s => s.PatientId == p.PatientId, ct).ConfigureAwait(false);
        return new MeDto(
            account.PseudonymId,
            nickname,
            account.PreferredLanguage,
            profile.TimeZone,
            new OnboardingDto(
                profile.ConsentsCompletedAtUtc is not null,
                profile.SafetyInfoCompletedAtUtc is not null,
                profile.CarePath,
                screened,
                intake is null ? "none" : IntakeStatusCode(intake.Status),
                triage is null ? "none" : TriageStatusCode(triage.Status)),
            await p.IsChatLockedAsync(ct).ConfigureAwait(false));
    }

    internal static string IntakeStatusCode(IntakeStatus status) => status switch
    {
        IntakeStatus.Active => "active",
        IntakeStatus.Paused => "paused",
        IntakeStatus.Review => "review",
        IntakeStatus.Submitted => "submitted",
        _ => "expired",
    };

    internal static string TriageStatusCode(TriageStatus status) => status switch
    {
        TriageStatus.Pending => "pending",
        TriageStatus.Reviewed => "reviewed",
        _ => "matched",
    };

    internal static async Task<Ok<MeDto>> UpdateProfileAsync(UpdateProfileRequest request, PatientContext p, IClock clock, CancellationToken ct)
    {
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        if (!Nicknames.IsAcceptable(request.Nickname))
        {
            throw Invalid("nickname");
        }

        profile.SetDisplayName(await p.EncryptAsync(Fields.DisplayName, request.Nickname!.Trim(), ct).ConfigureAwait(false));
        if (!string.IsNullOrWhiteSpace(request.TimeZone))
        {
            profile.SetTimeZone(request.TimeZone);
        }

        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await BuildMeAsync(p, clock, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<MeDto>> ChoosePathAsync(PathRequest request, PatientContext p, IClock clock, CancellationToken ct)
    {
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        profile.ChooseCarePath(request.Path ?? string.Empty);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await BuildMeAsync(p, clock, ct).ConfigureAwait(false));
    }

    internal static async Task<Ok<ContactRequestCreated>> ContactRequestAsync(HttpContext http, PatientContext p, IAuditLog audit, IOptions<PlatformOptions> platform, IClock clock, CancellationToken ct)
    {
        var source = http.Request.Query["source"].ToString() is { Length: > 0 and <= 32 } s ? s : "app";
        var request = CareRequest.Create(p.PatientId, source, clock);
        p.Db.CareRequests.Add(request);
        audit.Append("care.contact_requested", "CareRequest", request.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        var hours = platform.Value.TriageWithinHours;
        return TypedResults.Ok(new ContactRequestCreated(request.RequestedAtUtc, request.RequestedAtUtc.AddHours(hours), hours));
    }

    // ---------- Consents (P03, P11) ----------

    internal static async Task<Ok<List<ConsentDto>>> GetConsentsAsync(PatientContext p, CancellationToken ct) => TypedResults.Ok(await ConsentsAsync(p, ct).ConfigureAwait(false));

    private static async Task<List<ConsentDto>> ConsentsAsync(PatientContext p, CancellationToken ct)
    {
        var mine = await p.Db.Consents.Where(c => c.PatientId == p.PatientId).ToDictionaryAsync(c => c.Key, ct).ConfigureAwait(false);
        return ConsentCatalog.All.Select(d =>
        {
            var current = mine.GetValueOrDefault(d.Key);
            var granted = current is { } c && c.Granted && c.Version == d.Version;
            return new ConsentDto(d.Key, d.Required, granted, d.Version, current?.UpdatedAtUtc, d.Withdrawable);
        }).ToList();
    }

    internal static async Task<Ok<List<ConsentDto>>> UpdateConsentsAsync(UpdateConsentsRequest request, PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var mine = await p.Db.Consents.Where(c => c.PatientId == p.PatientId).ToDictionaryAsync(c => c.Key, ct).ConfigureAwait(false);
        foreach (var item in request.Items ?? [])
        {
            var key = item.Key ?? string.Empty;
            ConsentCatalog.Get(key);
            if (mine.TryGetValue(key, out var existing))
            {
                if (existing.Change(item.Granted, clock))
                {
                    audit.Append("consent.changed", "Consent", key, profile.PseudonymId, item.Granted ? "granted" : "withdrawn");
                }
            }
            else
            {
                var consent = Consent.Record(p.PatientId, key, item.Granted, clock);
                p.Db.Consents.Add(consent);
                mine[key] = consent;
                audit.Append("consent.changed", "Consent", key, profile.PseudonymId, item.Granted ? "granted" : "withdrawn");
            }
        }

        if (profile.ConsentsCompletedAtUtc is null)
        {
            // The first time through (P03) every required consent must be given.
            var missing = ConsentCatalog.All.Where(d => d.Required).Any(d => !mine.TryGetValue(d.Key, out var c) || !c.Granted);
            if (missing)
            {
                throw new DomainException(ErrorCodes.ConsentsMissing, "Required consents not given.");
            }

            profile.CompleteConsents(clock);
        }

        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(await ConsentsAsync(p, ct).ConfigureAwait(false));
    }

    // ---------- Safety info (P21, P34) ----------

    internal static async Task<Ok<SafetyInfoDto>> GetSafetyInfoAsync(PatientContext p, CancellationToken ct)
    {
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var data = await p.DecryptJsonAsync<SafetyInfoData>(Fields.SafetyInfo, profile.EmergencyContactCipher, ct).ConfigureAwait(false);
        return TypedResults.Ok(new SafetyInfoDto(data?.ContactPhone, data?.TrustedName, data?.TrustedPhone, profile.ShareLocation, profile.ShareLocationSinceUtc));
    }

    internal static async Task<Ok<SafetyInfoDto>> UpdateSafetyInfoAsync(UpdateSafetyInfoRequest request, PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var contact = CleanPhone(request.ContactPhone, "contactPhone");
        var trustedPhone = CleanPhone(request.TrustedPhone, "trustedPhone");
        var trustedName = string.IsNullOrWhiteSpace(request.TrustedName) ? null : request.TrustedName.Trim();
        if (trustedName is { Length: > 80 })
        {
            throw Invalid("trustedName");
        }

        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var data = new SafetyInfoData(contact, trustedName, trustedPhone);
        var empty = contact is null && trustedName is null && trustedPhone is null;
        profile.SetSafetyInfo(empty ? null : await p.EncryptJsonAsync(Fields.SafetyInfo, data, ct).ConfigureAwait(false), request.ShareLocation, clock);
        await SyncLocationConsentAsync(p, request.ShareLocation, clock, ct).ConfigureAwait(false);
        audit.Append("patient.safety_info_updated", "PatientProfile", null, profile.PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new SafetyInfoDto(contact, trustedName, trustedPhone, profile.ShareLocation, profile.ShareLocationSinceUtc));
    }

    internal static async Task<NoContent> DeleteTrustedContactAsync(PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var profile = await p.ProfileAsync(ct).ConfigureAwait(false);
        var data = await p.DecryptJsonAsync<SafetyInfoData>(Fields.SafetyInfo, profile.EmergencyContactCipher, ct).ConfigureAwait(false);
        var kept = data?.ContactPhone;
        profile.SetSafetyInfo(kept is null ? null : await p.EncryptJsonAsync(Fields.SafetyInfo, new SafetyInfoData(kept, null, null), ct).ConfigureAwait(false), profile.ShareLocation, clock);
        audit.Append("patient.trusted_contact_deleted", "PatientProfile", null, profile.PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task SyncLocationConsentAsync(PatientContext p, bool share, IClock clock, CancellationToken ct)
    {
        var consent = await p.Db.Consents.FirstOrDefaultAsync(c => c.PatientId == p.PatientId && c.Key == ConsentCatalog.EmergencyLocation, ct).ConfigureAwait(false);
        if (consent is null)
        {
            p.Db.Consents.Add(Consent.Record(p.PatientId, ConsentCatalog.EmergencyLocation, share, clock));
        }
        else
        {
            consent.Change(share, clock);
        }
    }

    // ---------- Security (P32) ----------

    internal static async Task<Ok<SecurityDto>> GetSecurityAsync(HttpContext http, PatientContext p, IClock clock, CancellationToken ct)
    {
        var credential = await p.Db.Credentials.AsNoTracking().FirstAsync(c => c.UserId == p.PatientId, ct).ConfigureAwait(false);
        var remaining = await p.Db.RecoveryCodes.CountAsync(r => r.UserId == p.PatientId && r.UsedAtUtc == null, ct).ConfigureAwait(false);
        var now = clock.UtcNow;
        var current = CurrentSession(http);
        var sessions = await p.Db.AuthSessions.AsNoTracking().Where(s => s.UserId == p.PatientId && s.RevokedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.LastActiveAtUtc).ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new SecurityDto(
            credential.PasswordChangedAtUtc,
            credential.TotpEnabled,
            remaining,
            sessions.Select(s => new SessionDto(s.Id, s.DeviceLabel, s.Id == current, s.Id == current ? now : s.LastActiveAtUtc)).ToList()));
    }

    internal static async Task<NoContent> ChangePasswordAsync(ChangePasswordRequest request, HttpContext http, PatientContext p, PasswordService passwords, AuthService auth, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var credential = await p.Db.Credentials.FirstAsync(c => c.UserId == p.PatientId, ct).ConfigureAwait(false);
        if (passwords.Verify(credential.PasswordHash, request.CurrentPassword ?? string.Empty) == PasswordCheck.Failed)
        {
            throw new DomainException(ErrorCodes.PasswordMismatch, "Current password wrong.");
        }

        PasswordService.EnsureStrong(request.NewPassword);
        credential.ChangePassword(passwords.Hash(request.NewPassword!), clock);
        audit.Append("auth.password_changed", "UserAccount", p.PatientId.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Other devices must sign in again with the new password.
        await auth.RevokeAllAsync(p.PatientId, CurrentSession(http), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<Ok<TotpSetupDto>> TotpSetupAsync(PatientContext p, ServerSecrets secrets, CancellationToken ct)
    {
        var credential = await p.Db.Credentials.FirstAsync(c => c.UserId == p.PatientId, ct).ConfigureAwait(false);
        var secret = Totp.NewSecret();
        credential.BeginTotpSetup(secrets.Protect(AuthService.TotpPurpose, Convert.ToBase64String(secret)));
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        var base32 = Totp.Base32(secret);
        var account = await p.AccountAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new TotpSetupDto(base32, Totp.OtpAuthUri(base32, account.PseudonymId)));
    }

    internal static async Task<NoContent> TotpConfirmAsync(CodeRequest request, PatientContext p, ServerSecrets secrets, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var credential = await p.Db.Credentials.FirstAsync(c => c.UserId == p.PatientId, ct).ConfigureAwait(false);
        if (credential.TotpSecretCipher is null)
        {
            throw new DomainException(ErrorCodes.TotpNotSetUp, "No pending authenticator.");
        }

        var secret = Convert.FromBase64String(secrets.Unprotect(AuthService.TotpPurpose, credential.TotpSecretCipher));
        var step = Totp.Verify(secret, request.Code, clock.UtcNow) ?? throw new DomainException(ErrorCodes.MfaFailed, "Authenticator code rejected.");
        credential.EnableTotp(step);
        audit.Append("auth.totp_enabled", "UserAccount", p.PatientId.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<NoContent> TotpDisableAsync(HttpContext http, PatientContext p, AuthService auth, IAuditLog audit, CancellationToken ct)
    {
        var body = await http.Request.ReadFromJsonAsync<CodeRequest>(ct).ConfigureAwait(false);
        var credential = await p.Db.Credentials.FirstAsync(c => c.UserId == p.PatientId, ct).ConfigureAwait(false);
        if (!auth.VerifyTotp(credential, body?.Code))
        {
            throw new DomainException(ErrorCodes.MfaFailed, "Authenticator code rejected.");
        }

        credential.DisableTotp();
        audit.Append("auth.totp_disabled", "UserAccount", p.PatientId.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    internal static async Task<Ok<RecoveryCodesDto>> NewRecoveryCodesAsync(PatientContext p, AuthService auth, IAuditLog audit, CancellationToken ct)
    {
        var codes = auth.NewRecoveryCodes(p.Db, p.PatientId);
        audit.Append("auth.recovery_codes_regenerated", "UserAccount", p.PatientId.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new RecoveryCodesDto(codes));
    }

    internal static async Task<NoContent> RevokeSessionAsync(Guid id, PatientContext p, ISessionCheck sessions, IClock clock, CancellationToken ct)
    {
        var session = await p.Db.AuthSessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == p.PatientId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Session {id} not found.");
        session.Revoke(clock);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        sessions.Forget(id);
        return TypedResults.NoContent();
    }

    internal static async Task<NoContent> RevokeAllAsync(PatientContext p, AuthService auth, CancellationToken ct)
    {
        await auth.RevokeAllAsync(p.PatientId, null, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    // ---------- Notifications (P33) ----------

    internal static async Task<Ok<NotificationPrefsDto>> GetNotificationsAsync(PatientContext p, CancellationToken ct)
    {
        var prefs = await p.Db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(n => n.UserId == p.PatientId, ct).ConfigureAwait(false)
            ?? NotificationPreferences.Default(p.PatientId);
        return TypedResults.Ok(Map(prefs));
    }

    internal static async Task<Ok<NotificationPrefsDto>> UpdateNotificationsAsync(NotificationPrefsDto request, PatientContext p, CancellationToken ct)
    {
        var prefs = await p.Db.NotificationPreferences.FirstOrDefaultAsync(n => n.UserId == p.PatientId, ct).ConfigureAwait(false);
        if (prefs is null)
        {
            prefs = NotificationPreferences.Default(p.PatientId);
            p.Db.NotificationPreferences.Add(prefs);
        }

        var reminder = request.HomeworkReminder switch
        {
            "daily" => ReminderFrequency.Daily,
            "every2days" => ReminderFrequency.EveryTwoDays,
            "off" => ReminderFrequency.Off,
            _ => throw Invalid("homeworkReminder"),
        };
        prefs.Update(request.Push, request.Sms, request.Email, request.NeutralText, request.QuickExit, reminder, request.ReminderTime, request.QuietFrom, request.QuietTo);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(Map(prefs));
    }

    private static NotificationPrefsDto Map(NotificationPreferences n) => new(
        n.Push,
        n.Sms,
        n.Email,
        n.NeutralText,
        n.QuickExit,
        n.HomeworkReminder switch { ReminderFrequency.Daily => "daily", ReminderFrequency.EveryTwoDays => "every2days", _ => "off" },
        n.ReminderTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        n.QuietFrom.ToString("HH:mm", CultureInfo.InvariantCulture),
        n.QuietTo.ToString("HH:mm", CultureInfo.InvariantCulture));

    // ---------- Data rights (P11) ----------

    internal static async Task<Ok<List<AccessLogEntryDto>>> AccessLogAsync(PatientContext p, CancellationToken ct)
    {
        var pseudonym = (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId;

        // Who other than the patient touched their file. The ledger holds no content, only who, what and when.
        var entries = await p.Db.AuditEntries.AsNoTracking()
            .Where(a => a.PatientPseudonymId == pseudonym && (a.ActorRole == null || a.ActorRole != AppRoles.Patient))
            .OrderByDescending(a => a.OccurredAtUtc).Take(100)
            .Select(a => new { a.ActorRole, a.Action, a.OccurredAtUtc })
            .ToListAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(entries.Select(e => new AccessLogEntryDto(
            e.ActorRole switch
            {
                AppRoles.Therapist => "therapist",
                AppRoles.Supervisor => "supervisor",
                AppRoles.ClinicalLead => "clinical_lead",
                AppRoles.CrisisService => "crisis_service",
                _ => "system",
            },
            e.Action,
            e.OccurredAtUtc)).ToList());
    }

    internal static async Task<Ok<DataRequestStatusDto>> DataExportStatusAsync(PatientContext p, CancellationToken ct) =>
        TypedResults.Ok(await ExportStatusAsync(p, ct).ConfigureAwait(false));

    private static async Task<DataRequestStatusDto> ExportStatusAsync(PatientContext p, CancellationToken ct)
    {
        var latest = await p.Db.DataRequests.AsNoTracking().Where(r => r.PatientId == p.PatientId && r.Kind == DataRequestKind.Export)
            .OrderByDescending(r => r.RequestedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new DataRequestStatusDto(latest?.RequestedAtUtc, latest?.DueAtUtc, latest?.CompletedAtUtc is not null);
    }

    internal static async Task<Ok<DataRequestStatusDto>> RequestDataExportAsync(PatientContext p, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var open = await p.Db.DataRequests.AnyAsync(r => r.PatientId == p.PatientId && r.Kind == DataRequestKind.Export && r.CompletedAtUtc == null, ct).ConfigureAwait(false);
        if (!open)
        {
            var request = DataRequest.Create(p.PatientId, DataRequestKind.Export, clock);
            p.Db.DataRequests.Add(request);
            audit.Append("data.export_requested", "DataRequest", request.Id.ToString(), (await p.AccountAsync(ct).ConfigureAwait(false)).PseudonymId);
            await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return TypedResults.Ok(await ExportStatusAsync(p, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Closes the account, signs out everywhere and crypto-shreds the patient's data key, which makes every
    /// encrypted field unreadable, backups included. Content-free audit rows stay, as the law requires.
    /// </summary>
    internal static async Task<Accepted> DeleteAccountAsync(DeleteAccountRequest request, PatientContext p, PasswordService passwords, AuthService auth, ScopeRunner scopes, IAuditLog audit, IClock clock, CancellationToken ct)
    {
        var credential = await p.Db.Credentials.FirstAsync(c => c.UserId == p.PatientId, ct).ConfigureAwait(false);
        if (passwords.Verify(credential.PasswordHash, request.Password ?? string.Empty) == PasswordCheck.Failed)
        {
            throw new DomainException(ErrorCodes.PasswordMismatch, "Password wrong.");
        }

        var account = await p.AccountAsync(ct).ConfigureAwait(false);
        var deletion = DataRequest.Create(p.PatientId, DataRequestKind.Erasure, clock);
        p.Db.DataRequests.Add(deletion);
        account.Close();
        audit.Append("data.erasure_requested", "DataRequest", deletion.Id.ToString(), account.PseudonymId);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        await auth.RevokeAllAsync(p.PatientId, null, ct).ConfigureAwait(false);

        var patientId = p.PatientId;
        await scopes.AsServiceAsync(DataProtectionServiceId, AppRoles.DataProtectionService, async sp =>
        {
            await sp.GetRequiredService<CryptoShredder>().ShredAsync(patientId, "patient_request", ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

        deletion.Complete(clock);
        await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Accepted((string?)null);
    }

    internal static Guid? CurrentSession(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(TamaneenClaims.Session)?.Value, out var sid) ? sid : null;

    internal static string? CleanPhone(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = OneTimeCodes.Normalize(value.Replace("+", string.Empty, StringComparison.Ordinal).Replace("(", string.Empty, StringComparison.Ordinal).Replace(")", string.Empty, StringComparison.Ordinal));
        if (normalized is not { Length: >= 7 and <= 15 })
        {
            throw Invalid(field);
        }

        return value.TrimStart().StartsWith('+') ? "+" + normalized : normalized;
    }

    internal static DomainException Invalid(string field) =>
        new DomainException(ErrorCodes.Validation, $"{field} is invalid.").With("errors", new Dictionary<string, string[]> { [field] = ["invalid"] });
}
