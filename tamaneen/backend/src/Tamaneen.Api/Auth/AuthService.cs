using Tamaneen.Infrastructure.Auth;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Tamaneen.Hosting;
using Tamaneen.Identity;
using Tamaneen.Identity.Auth;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Auth;

public sealed record ChallengeResult(Guid ChallengeId, string MaskedContact, int ResendAfterSeconds, int ExpiresInSeconds);

public sealed record SignupResult(TokenPair Tokens, IReadOnlyList<string> RecoveryCodes);

public sealed record LoginResult(TokenPair? Tokens, string? MfaToken, IReadOnlyList<string> Methods, string? MaskedPhone);

public sealed record MfaResult(TokenPair Tokens, string? DeviceToken);

/// <summary>
/// Patient and staff sign-in (P02, P15–P20, S06). Nothing here reveals whether an account exists: unknown
/// contacts get the same answers, the same timing and the same attempt limits as real ones.
/// </summary>
public sealed partial class AuthService(
    TamaneenDbContext db,
    IContactHasher contactHasher,
    PasswordService passwords,
    ServerSecrets secrets,
    TokenService tokens,
    IMessageSender sender,
    MessageComposer composer,
    ScopeRunner scopes,
    ISessionCheck sessionCheck,
    IMemoryCache cache,
    IClock clock,
    IOptions<PlatformOptions> platform,
    ILogger<AuthService> logger)
{
    public const string ContactField = "identity.contact";
    public const string DisplayNameField = ProtectedFields.DisplayName;
    private const string EligibilityPurpose = "eligibility";
    private const string ChallengeSecret = "challenge";
    private const string MfaPurpose = "mfa";
    public const string TotpPurpose = "totp";
    private static readonly TimeSpan EligibilityLifetime = TimeSpan.FromHours(2);
    private static readonly TimeSpan MfaLifetime = TimeSpan.FromMinutes(5);
    private const int MaxMfaFailures = 5;

    // ---------------- Eligibility (P15) ----------------

    public string? IssueEligibility(bool isAdult, string cityCode, out string? reason)
    {
        EnsureSignupsOpen();
        reason = null;
        if (!isAdult)
        {
            reason = "age";
            return null;
        }

        var city = platform.Value.Cities.FirstOrDefault(c => string.Equals(c.Code, cityCode, StringComparison.OrdinalIgnoreCase));
        if (city is null)
        {
            reason = "region";
            return null;
        }

        var payload = JsonSerializer.Serialize(new EligibilityPayload(city.Code, clock.UtcNow.Add(EligibilityLifetime)));
        return OneTimeCodes.Base64Url(secrets.Protect(EligibilityPurpose, payload));
    }

    // ---------------- Sign-up (P02, P16, P17) ----------------

    /// <summary>EC-26: while sign-ups are paused no eligibility token is issued and no account is created.</summary>
    private void EnsureSignupsOpen()
    {
        if (!platform.Value.SignupsOpen)
        {
            throw new DomainException(ErrorCodes.SignupsPaused, "New sign-ups are paused.");
        }
    }

    public async Task<ChallengeResult> StartSignupAsync(string eligibilityToken, string nickname, string contactType, string contact, string password, string language, CancellationToken ct)
    {
        EnsureSignupsOpen();
        var eligibility = ReadEligibility(eligibilityToken);
        if (!Nicknames.IsAcceptable(nickname))
        {
            throw new DomainException(ErrorCodes.Validation, "Nickname rejected.").With("errors", new Dictionary<string, string[]> { ["nickname"] = ["invalid"] });
        }

        var (normalized, type) = NormalizeContact(contact, contactType);
        PasswordService.EnsureStrong(password, nickname, normalized);
        var lookup = contactHasher.Hash(normalized);
        var masked = OneTimeCodes.Mask(normalized, type);
        var existing = await db.UserAccounts.FirstOrDefaultAsync(a => a.ContactLookupHash == lookup, ct).ConfigureAwait(false);

        if (existing is not null && existing.Status != AccountStatus.PendingVerification)
        {
            // Already registered: tell the owner through their own inbox, answer the caller as for a new account.
            await SendAsync(type, normalized, existing.PreferredLanguage, MessagePurpose.AlreadyRegistered, null, ct).ConfigureAwait(false);
            var decoy = AuthChallenge.Create(ChallengePurpose.Decoy, null, secrets.Mac(ChallengeSecret, OneTimeCodes.NewToken()), secrets.Protect(ChallengeSecret, normalized), null, clock);
            db.AuthChallenges.Add(decoy);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result(decoy, masked);
        }

        UserAccount account;
        if (existing is null)
        {
            account = UserAccount.Register(AppRoles.Patient, lookup, language, clock);
            db.UserAccounts.Add(account);
            db.Credentials.Add(Credential.Create(account.Id, passwords.Hash(password), clock));
        }
        else
        {
            // An earlier sign-up never confirmed its code: start over with the new details.
            account = existing;
            account.ChangeLanguage(language);
            var credential = await db.Credentials.FirstAsync(c => c.UserId == account.Id, ct).ConfigureAwait(false);
            credential.ChangePassword(passwords.Hash(password), clock);
            await db.AuthChallenges.Where(c => c.UserId == account.Id && c.ConsumedAtUtc == null).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        }

        var code = OneTimeCodes.NewNumericCode();
        var payload = JsonSerializer.Serialize(new SignupPayload(nickname.Trim(), eligibility.CityCode, type));
        var challenge = AuthChallenge.Create(ChallengePurpose.Signup, account.Id, Array.Empty<byte>(), secrets.Protect(ChallengeSecret, normalized), secrets.Protect(ChallengeSecret, payload), clock);
        SetCode(challenge, code);
        db.AuthChallenges.Add(challenge);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await SendAsync(type, normalized, account.PreferredLanguage, MessagePurpose.SignupCode, code, ct).ConfigureAwait(false);
        return Result(challenge, masked);
    }

    public async Task<ChallengeResult> ResendAsync(Guid challengeId, CancellationToken ct)
    {
        var challenge = await db.AuthChallenges.FirstOrDefaultAsync(c => c.Id == challengeId, ct).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.ChallengeExpired, $"Challenge {challengeId} not found.");
        var wait = challenge.ResendWait(clock);
        if (wait > TimeSpan.Zero)
        {
            var seconds = (int)Math.Ceiling(wait.TotalSeconds);
            throw new DomainException(ErrorCodes.ResendTooSoon, "Resend during cooldown.", seconds).With("retryAfterSeconds", seconds);
        }

        var contact = secrets.Unprotect(ChallengeSecret, challenge.ContactCipher);
        var type = contact.Contains('@', StringComparison.Ordinal) ? "email" : "phone";
        var code = OneTimeCodes.NewNumericCode();
        challenge.Resend(secrets.Mac(ChallengeSecret, $"{challenge.Id:N}:{code}"), clock);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (challenge.Purpose is ChallengePurpose.Signup or ChallengePurpose.Recover or ChallengePurpose.LoginSms)
        {
            var language = challenge.UserId is { } uid ? await LanguageOf(uid, ct).ConfigureAwait(false) : SupportedLanguages.Default;
            var purpose = challenge.Purpose switch
            {
                ChallengePurpose.Signup => MessagePurpose.SignupCode,
                ChallengePurpose.Recover => MessagePurpose.RecoverCode,
                _ => MessagePurpose.LoginCode,
            };
            await SendAsync(type, contact, language, purpose, code, ct).ConfigureAwait(false);
        }

        return Result(challenge, OneTimeCodes.Mask(contact, type));
    }

    public async Task<SignupResult> VerifySignupAsync(Guid challengeId, string code, string userAgent, CancellationToken ct)
    {
        var challenge = await CheckCodeAsync(challengeId, code, ChallengePurpose.Signup, ct).ConfigureAwait(false);
        var accountId = challenge.UserId!.Value;
        var contact = secrets.Unprotect(ChallengeSecret, challenge.ContactCipher);
        var payload = JsonSerializer.Deserialize<SignupPayload>(secrets.Unprotect(ChallengeSecret, challenge.PayloadCipher!))!;
        var language = await LanguageOf(accountId, ct).ConfigureAwait(false);

        // Everything that touches the patient's vault runs as the patient, in one transaction.
        return await scopes.AsUserAsync(accountId, AppRoles.Patient, language, async sp =>
        {
            var pdb = sp.GetRequiredService<TamaneenDbContext>();
            var protector = sp.GetRequiredService<IPatientDataProtector>();
            var audit = sp.GetRequiredService<IAuditLog>();
            var account = await pdb.UserAccounts.FirstAsync(a => a.Id == accountId, ct).ConfigureAwait(false);
            var tracked = await pdb.AuthChallenges.FirstAsync(c => c.Id == challengeId, ct).ConfigureAwait(false);
            tracked.Consume(clock);
            account.Activate();

            var profile = PatientProfile.Create(account, payload.CityCode, clock);
            profile.SetContact(await protector.EncryptAsync(account.Id, ContactField, contact, ct).ConfigureAwait(false), payload.ContactType);
            profile.SetDisplayName(await protector.EncryptAsync(account.Id, DisplayNameField, payload.Nickname, ct).ConfigureAwait(false));
            pdb.PatientProfiles.Add(profile);
            pdb.NotificationPreferences.Add(NotificationPreferences.Default(account.Id));

            var codes = NewRecoveryCodes(pdb, account.Id);
            var (session, refresh) = StartSession(pdb, account.Id, "patient", userAgent);
            audit.Append("auth.signup_verified", "UserAccount", account.Id.ToString(), account.PseudonymId);
            await pdb.SaveChangesAsync(ct).ConfigureAwait(false);
            return new SignupResult(tokens.Issue(account, session, refresh), codes);
        }).ConfigureAwait(false);
    }

    // ---------------- Sign-in (P18, P19, S06) ----------------

    public async Task<LoginResult> LoginAsync(string contact, string password, string client, string? deviceToken, string userAgent, CancellationToken ct)
    {
        string normalized;
        try
        {
            normalized = ContactHasher.Normalize(contact);
        }
        catch (ArgumentException)
        {
            throw new DomainException(ErrorCodes.InvalidCredentials, "Unusable contact.");
        }

        _ = normalized;
        var lookup = contactHasher.Hash(contact);
        var account = await db.UserAccounts.FirstOrDefaultAsync(a => a.ContactLookupHash == lookup, ct).ConfigureAwait(false);
        var credential = account is null ? null : await db.Credentials.FirstOrDefaultAsync(c => c.UserId == account.Id, ct).ConfigureAwait(false);
        var staffClient = client == "staff";
        var usable = account is not null && credential is not null && account.Status == AccountStatus.Active && (account.Role == AppRoles.Patient) != staffClient;

        if (!usable)
        {
            passwords.VerifyDummy(password);
            throw UnknownAccountFailure(lookup);
        }

        if (credential!.IsLocked(clock))
        {
            throw Locked(credential.LockedUntilUtc!.Value);
        }

        var check = passwords.Verify(credential.PasswordHash, password);
        if (check == PasswordCheck.Failed)
        {
            var remaining = credential.RecordFailure(clock);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (remaining == 0)
            {
                throw Locked(credential.LockedUntilUtc!.Value);
            }

            throw new DomainException(ErrorCodes.InvalidCredentials, "Wrong password.").With("remainingAttempts", remaining);
        }

        credential.RecordSuccess();
        if (check == PasswordCheck.SucceededRehashNeeded)
        {
            credential.UpgradeHash(passwords.Hash(password));
        }

        var needsMfa = staffClient || (credential.TotpEnabled && !await IsTrustedDeviceAsync(account!.Id, deviceToken, ct).ConfigureAwait(false));
        if (!needsMfa)
        {
            var (session, refresh) = StartSession(db, account!.Id, client, userAgent);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new LoginResult(tokens.Issue(account, session, refresh), null, [], null);
        }

        if (staffClient && !credential.TotpEnabled)
        {
            // Staff accounts are provisioned with an authenticator; without one they cannot sign in.
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new DomainException(ErrorCodes.InvalidCredentials, "Staff account without authenticator.");
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var methods = new List<string>();
        if (credential.TotpEnabled)
        {
            methods.Add("totp");
        }

        string? maskedPhone = null;
        if (!staffClient)
        {
            var phone = await PatientContactAsync(account!.Id, account.PreferredLanguage, ct).ConfigureAwait(false);
            if (phone is { Type: "phone" })
            {
                methods.Add("sms");
                maskedPhone = OneTimeCodes.Mask(phone.Value.Contact, "phone");
            }
        }

        methods.Add("recovery");
        var mfa = new MfaPayload(account!.Id, client, clock.UtcNow.Add(MfaLifetime), Guid.NewGuid());
        return new LoginResult(null, OneTimeCodes.Base64Url(secrets.Protect(MfaPurpose, JsonSerializer.Serialize(mfa))), methods, maskedPhone);
    }

    public async Task SendLoginSmsAsync(string mfaToken, CancellationToken ct)
    {
        var mfa = ReadMfa(mfaToken);
        var account = await db.UserAccounts.FirstAsync(a => a.Id == mfa.UserId, ct).ConfigureAwait(false);
        var phone = await PatientContactAsync(account.Id, account.PreferredLanguage, ct).ConfigureAwait(false);
        if (phone is not { Type: "phone" })
        {
            throw new DomainException(ErrorCodes.MfaFailed, "No phone on file.");
        }

        var existing = await db.AuthChallenges.FirstOrDefaultAsync(c => c.UserId == account.Id && c.Purpose == ChallengePurpose.LoginSms && c.ConsumedAtUtc == null && c.ExpiresAtUtc > clock.UtcNow, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await ResendAsync(existing.Id, ct).ConfigureAwait(false);
            return;
        }

        var code = OneTimeCodes.NewNumericCode();
        var challenge = AuthChallenge.Create(ChallengePurpose.LoginSms, account.Id, Array.Empty<byte>(), secrets.Protect(ChallengeSecret, phone.Value.Contact), null, clock);
        SetCode(challenge, code);
        db.AuthChallenges.Add(challenge);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await SendAsync("phone", phone.Value.Contact, account.PreferredLanguage, MessagePurpose.LoginCode, code, ct).ConfigureAwait(false);
    }

    public async Task<MfaResult> CompleteMfaAsync(string mfaToken, string method, string code, bool rememberDevice, string userAgent, CancellationToken ct)
    {
        var mfa = ReadMfa(mfaToken);
        var failuresKey = $"mfa-failures:{mfa.Nonce:N}";
        if (cache.TryGetValue(failuresKey, out int failures) && failures >= MaxMfaFailures)
        {
            throw new DomainException(ErrorCodes.ChallengeExpired, "Too many MFA failures for this sign-in.");
        }

        var account = await db.UserAccounts.FirstAsync(a => a.Id == mfa.UserId, ct).ConfigureAwait(false);
        var credential = await db.Credentials.FirstAsync(c => c.UserId == account.Id, ct).ConfigureAwait(false);
        var ok = method switch
        {
            "totp" => VerifyTotp(credential, code),
            "recovery" => await UseRecoveryCodeAsync(account.Id, code, ct).ConfigureAwait(false),
            "sms" when mfa.Client == "patient" => await UseLoginSmsAsync(account.Id, code, ct).ConfigureAwait(false),
            _ => false,
        };

        if (!ok)
        {
            cache.Set(failuresKey, failures + 1, MfaLifetime);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new DomainException(ErrorCodes.MfaFailed, "Second factor rejected.");
        }

        string? device = null;
        if (rememberDevice && mfa.Client == "patient")
        {
            device = OneTimeCodes.NewToken();
            db.TrustedDevices.Add(TrustedDevice.Create(account.Id, OneTimeCodes.TokenHash(device), clock));
        }

        var (session, refresh) = StartSession(db, account.Id, mfa.Client, userAgent);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache.Remove(failuresKey);
        return new MfaResult(tokens.Issue(account, session, refresh), device);
    }

    // ---------------- Recovery (P20) ----------------

    public async Task<ChallengeResult> StartRecoveryAsync(string contact, CancellationToken ct)
    {
        string normalized;
        try
        {
            normalized = ContactHasher.Normalize(contact);
        }
        catch (ArgumentException)
        {
            throw new DomainException(ErrorCodes.Validation, "Unusable contact.").With("errors", new Dictionary<string, string[]> { ["contact"] = ["invalid"] });
        }

        var type = normalized.StartsWith("e:", StringComparison.Ordinal) ? "email" : "phone";
        var plain = normalized[2..];
        var lookup = contactHasher.Hash(contact);
        var account = await db.UserAccounts.FirstOrDefaultAsync(a => a.ContactLookupHash == lookup && a.Status == AccountStatus.Active && a.Role == AppRoles.Patient, ct).ConfigureAwait(false);
        AuthChallenge challenge;
        if (account is null)
        {
            challenge = AuthChallenge.Create(ChallengePurpose.Decoy, null, secrets.Mac(ChallengeSecret, OneTimeCodes.NewToken()), secrets.Protect(ChallengeSecret, plain), null, clock);
            db.AuthChallenges.Add(challenge);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var code = OneTimeCodes.NewNumericCode();
            challenge = AuthChallenge.Create(ChallengePurpose.Recover, account.Id, Array.Empty<byte>(), secrets.Protect(ChallengeSecret, plain), null, clock);
            SetCode(challenge, code);
            db.AuthChallenges.Add(challenge);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await SendAsync(type, plain, account.PreferredLanguage, MessagePurpose.RecoverCode, code, ct).ConfigureAwait(false);
        }

        return Result(challenge, OneTimeCodes.Mask(plain, type));
    }

    public async Task CompleteRecoveryAsync(Guid challengeId, string code, string newPassword, CancellationToken ct)
    {
        PasswordService.EnsureStrong(newPassword);
        var challenge = await CheckCodeAsync(challengeId, code, ChallengePurpose.Recover, ct).ConfigureAwait(false);
        challenge.Consume(clock);
        await ResetPasswordAsync(challenge.UserId!.Value, newPassword, "auth.password_recovered", ct).ConfigureAwait(false);
    }

    public async Task RecoverWithCodeAsync(string contact, string recoveryCode, string newPassword, CancellationToken ct)
    {
        PasswordService.EnsureStrong(newPassword);
        Guid? accountId = null;
        try
        {
            var lookup = contactHasher.Hash(contact);
            accountId = await db.UserAccounts.Where(a => a.ContactLookupHash == lookup && a.Status == AccountStatus.Active && a.Role == AppRoles.Patient)
                .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }

        if (accountId is null || !await UseRecoveryCodeAsync(accountId.Value, recoveryCode, ct).ConfigureAwait(false))
        {
            throw new DomainException(ErrorCodes.InvalidCode, "Recovery code rejected.");
        }

        await ResetPasswordAsync(accountId.Value, newPassword, "auth.password_recovered_code", ct).ConfigureAwait(false);
    }

    // ---------------- Sessions ----------------

    public async Task<TokenPair> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = OneTimeCodes.TokenHash(refreshToken);
        var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct).ConfigureAwait(false);
        if (session is null)
        {
            // A rotated-away token used again means it was copied: end that session for everyone.
            var stolen = await db.AuthSessions.FirstOrDefaultAsync(s => s.PreviousRefreshTokenHash == hash && s.RevokedAtUtc == null, ct).ConfigureAwait(false);
            if (stolen is not null)
            {
                stolen.Revoke(clock);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                sessionCheck.Forget(stolen.Id);
                LogReuse(logger, stolen.Id);
            }

            throw new DomainException(ErrorCodes.SessionRevoked, "Unknown refresh token.");
        }

        var account = await db.UserAccounts.FirstAsync(a => a.Id == session.UserId, ct).ConfigureAwait(false);
        if (!session.IsActive(clock) || account.Status != AccountStatus.Active)
        {
            throw new DomainException(ErrorCodes.SessionRevoked, "Session ended.");
        }

        var next = OneTimeCodes.NewToken();
        session.Rotate(OneTimeCodes.TokenHash(next), clock);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return tokens.Issue(account, session, next);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        var hash = OneTimeCodes.TokenHash(refreshToken);
        var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == hash, ct).ConfigureAwait(false);
        if (session is not null)
        {
            session.Revoke(clock);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            sessionCheck.Forget(session.Id);
        }
    }

    public async Task RevokeAllAsync(Guid userId, Guid? except, CancellationToken ct)
    {
        var sessions = await db.AuthSessions.Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.Id != except).ToListAsync(ct).ConfigureAwait(false);
        foreach (var s in sessions)
        {
            s.Revoke(clock);
            sessionCheck.Forget(s.Id);
        }

        await db.TrustedDevices.Where(d => d.UserId == userId).ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public IReadOnlyList<string> NewRecoveryCodes(TamaneenDbContext context, Guid userId)
    {
        var existing = context.RecoveryCodes.Where(r => r.UserId == userId);
        context.RecoveryCodes.RemoveRange(existing);
        var codes = Enumerable.Range(0, RecoveryCode.SetSize).Select(_ => OneTimeCodes.NewRecoveryCode()).Distinct().ToList();
        foreach (var c in codes)
        {
            context.RecoveryCodes.Add(RecoveryCode.Create(userId, OneTimeCodes.RecoveryHash(userId, c), clock));
        }

        return codes;
    }

    public bool VerifyTotp(Credential credential, string? code)
    {
        if (!credential.TotpEnabled || credential.TotpSecretCipher is null)
        {
            return false;
        }

        var secret = Convert.FromBase64String(secrets.Unprotect(TotpPurpose, credential.TotpSecretCipher));
        return Totp.Verify(secret, code, clock.UtcNow) is { } step && credential.TryUseTotpStep(step);
    }

    // ---------------- helpers ----------------

    private (AuthSession Session, string Refresh) StartSession(TamaneenDbContext context, Guid userId, string client, string userAgent)
    {
        var refresh = OneTimeCodes.NewToken();
        var session = AuthSession.Start(userId, client, DeviceLabel(userAgent), OneTimeCodes.TokenHash(refresh), clock);
        context.AuthSessions.Add(session);
        return (session, refresh);
    }

    private async Task ResetPasswordAsync(Guid userId, string newPassword, string auditAction, CancellationToken ct)
    {
        var credential = await db.Credentials.FirstAsync(c => c.UserId == userId, ct).ConfigureAwait(false);
        credential.ChangePassword(passwords.Hash(newPassword), clock);
        var pseudonym = await db.UserAccounts.Where(a => a.Id == userId).Select(a => a.PseudonymId).FirstAsync(ct).ConfigureAwait(false);
        db.AuditEntries.Add(new AuditEntry(clock.UtcNow, userId, AppRoles.Patient, auditAction, "UserAccount", userId.ToString(), pseudonym, null, System.Diagnostics.Activity.Current?.TraceId.ToString()));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await RevokeAllAsync(userId, null, ct).ConfigureAwait(false);
    }

    private async Task<AuthChallenge> CheckCodeAsync(Guid challengeId, string code, ChallengePurpose expected, CancellationToken ct)
    {
        var challenge = await db.AuthChallenges.FirstOrDefaultAsync(c => c.Id == challengeId, ct).ConfigureAwait(false);
        if (challenge is null || !challenge.IsUsable(clock))
        {
            throw new DomainException(ErrorCodes.ChallengeExpired, $"Challenge {challengeId} is not usable.");
        }

        challenge.RegisterAttempt(clock);
        var digits = OneTimeCodes.Normalize(code) ?? string.Empty;
        var matches = ServerSecrets.Equal(challenge.CodeHash, secrets.Mac(ChallengeSecret, $"{challenge.Id:N}:{digits}"));
        if (!matches || challenge.Purpose != expected)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw new DomainException(ErrorCodes.InvalidCode, "Code rejected.").With("remainingAttempts", Math.Max(0, AuthChallenge.MaxAttempts - challenge.Attempts));
        }

        return challenge;
    }

    private async Task<bool> UseLoginSmsAsync(Guid userId, string code, CancellationToken ct)
    {
        var challenge = await db.AuthChallenges.Where(c => c.UserId == userId && c.Purpose == ChallengePurpose.LoginSms && c.ConsumedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (challenge is null || !challenge.RegisterAttempt(clock))
        {
            return false;
        }

        var digits = OneTimeCodes.Normalize(code) ?? string.Empty;
        if (!ServerSecrets.Equal(challenge.CodeHash, secrets.Mac(ChallengeSecret, $"{challenge.Id:N}:{digits}")))
        {
            return false;
        }

        challenge.Consume(clock);
        return true;
    }

    private async Task<bool> UseRecoveryCodeAsync(Guid userId, string code, CancellationToken ct)
    {
        var hash = OneTimeCodes.RecoveryHash(userId, code);
        var match = await db.RecoveryCodes.FirstOrDefaultAsync(r => r.UserId == userId && r.CodeHash == hash && r.UsedAtUtc == null, ct).ConfigureAwait(false);
        if (match is null)
        {
            return false;
        }

        match.MarkUsed(clock);
        return true;
    }

    private async Task<bool> IsTrustedDeviceAsync(Guid userId, string? deviceToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(deviceToken))
        {
            return false;
        }

        var hash = OneTimeCodes.TokenHash(deviceToken);
        var now = clock.UtcNow;
        return await db.TrustedDevices.AnyAsync(d => d.UserId == userId && d.TokenHash == hash && d.ExpiresAtUtc > now, ct).ConfigureAwait(false);
    }

    private Task<(string Contact, string Type)?> PatientContactAsync(Guid patientId, string language, CancellationToken ct) =>
        scopes.AsUserAsync<(string, string)?>(patientId, AppRoles.Patient, language, async sp =>
        {
            var pdb = sp.GetRequiredService<TamaneenDbContext>();
            var profile = await pdb.PatientProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.PatientId == patientId, ct).ConfigureAwait(false);
            if (profile?.ContactCipher is null)
            {
                return null;
            }

            var contact = await sp.GetRequiredService<IPatientDataProtector>().DecryptAsync(patientId, ContactField, profile.ContactCipher, ct).ConfigureAwait(false);
            return (contact, profile.ContactType);
        });

    private DomainException UnknownAccountFailure(byte[] lookup)
    {
        // Mirror the real counter so an unknown contact behaves exactly like a known one.
        var key = $"login-unknown:{Convert.ToHexString(lookup)}";
        var entry = cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = Credential.LockDuration;
            return new UnknownCounter();
        })!;
        if (entry.LockedUntil is { } until && until > clock.UtcNow)
        {
            return Locked(until);
        }

        entry.Failures++;
        if (entry.Failures >= Credential.MaxFailures)
        {
            entry.LockedUntil = clock.UtcNow + Credential.LockDuration;
            entry.Failures = 0;
            return Locked(entry.LockedUntil.Value);
        }

        return new DomainException(ErrorCodes.InvalidCredentials, "Unknown or unusable account.").With("remainingAttempts", Credential.MaxFailures - entry.Failures);
    }

    private DomainException Locked(DateTimeOffset until)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling((until - clock.UtcNow).TotalSeconds));
        var minutes = (int)Math.Ceiling(seconds / 60.0);
        return new DomainException(ErrorCodes.AccountLocked, "Sign-in locked.", minutes).With("retryAfterSeconds", seconds);
    }

    // The code's MAC includes the challenge id, which exists only once the challenge is created.
    private void SetCode(AuthChallenge challenge, string code) => challenge.InitializeCode(secrets.Mac(ChallengeSecret, $"{challenge.Id:N}:{code}"));

    private static ChallengeResult Result(AuthChallenge c, string masked) =>
        new(c.Id, masked, (int)AuthChallenge.ResendCooldown.TotalSeconds, (int)AuthChallenge.Lifetime.TotalSeconds);

    private async Task SendAsync(string type, string contact, string language, MessagePurpose purpose, string? code, CancellationToken ct)
    {
        try
        {
            await sender.SendAsync(composer.Compose(type, contact, language, purpose, code), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The caller still gets the challenge and can resend after the cooldown.
            LogSendFailed(logger, purpose.ToString(), ex.GetType().Name);
        }
    }

    private async Task<string> LanguageOf(Guid userId, CancellationToken ct) =>
        await db.UserAccounts.Where(a => a.Id == userId).Select(a => a.PreferredLanguage).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? SupportedLanguages.Default;

    private EligibilityPayload ReadEligibility(string token)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<EligibilityPayload>(secrets.Unprotect(EligibilityPurpose, FromBase64Url(token)));
            if (payload is not null && payload.ExpiresAt > clock.UtcNow)
            {
                return payload;
            }
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or JsonException or ArgumentException)
        {
        }

        throw new DomainException(ErrorCodes.NotEligible, "Eligibility token missing, invalid or expired.");
    }

    private MfaPayload ReadMfa(string token)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<MfaPayload>(secrets.Unprotect(MfaPurpose, FromBase64Url(token)));
            if (payload is not null && payload.ExpiresAt > clock.UtcNow)
            {
                return payload;
            }
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or JsonException or ArgumentException)
        {
        }

        throw new DomainException(ErrorCodes.ChallengeExpired, "MFA token missing, invalid or expired.");
    }

    private static (string Normalized, string Type) NormalizeContact(string contact, string contactType)
    {
        try
        {
            var normalized = ContactHasher.Normalize(contact);
            var type = normalized.StartsWith("e:", StringComparison.Ordinal) ? "email" : "phone";
            if (type != (contactType == "email" ? "email" : "phone"))
            {
                throw new ArgumentException("Contact does not match its type.");
            }

            return (normalized[2..], type);
        }
        catch (ArgumentException)
        {
            throw new DomainException(ErrorCodes.Validation, "Unusable contact.").With("errors", new Dictionary<string, string[]> { ["contact"] = ["invalid"] });
        }
    }

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '='));
    }

    internal static string DeviceLabel(string userAgent)
    {
        var ua = userAgent ?? string.Empty;
        var device = ua.Contains("iPhone", StringComparison.Ordinal) ? "iPhone"
            : ua.Contains("iPad", StringComparison.Ordinal) ? "iPad"
            : ua.Contains("Android", StringComparison.Ordinal) ? "Android"
            : ua.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : ua.Contains("Mac OS", StringComparison.Ordinal) ? "Mac"
            : ua.Contains("Linux", StringComparison.Ordinal) ? "Linux" : "unknown";
        var browser = ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
            : ua.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome"
            : ua.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox"
            : ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari" : null;
        return browser is null ? device : $"{browser} · {device}";
    }

    private sealed class UnknownCounter
    {
        public int Failures { get; set; }

        public DateTimeOffset? LockedUntil { get; set; }
    }

    private sealed record EligibilityPayload(string CityCode, DateTimeOffset ExpiresAt);

    private sealed record SignupPayload(string Nickname, string CityCode, string ContactType);

    private sealed record MfaPayload(Guid UserId, string Client, DateTimeOffset ExpiresAt, Guid Nonce);

    [LoggerMessage(EventId = 4201, Level = LogLevel.Warning, Message = "Sending a {Purpose} message failed ({Error}).")]
    private static partial void LogSendFailed(ILogger logger, string purpose, string error);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Warning, Message = "Refresh token reuse detected; session {SessionId} revoked.")]
    private static partial void LogReuse(ILogger logger, Guid sessionId);
}
