using System.Text;
using Microsoft.Extensions.Options;
using Tamaneen.Api.Auth;
using Tamaneen.Identity.Auth;
using Tamaneen.Infrastructure.Auth;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests.Auth;

public class TotpTests
{
    // RFC 6238 appendix B, SHA-1 secret "12345678901234567890"; we use the last 6 of the 8 digits.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Codes_match_the_rfc_test_vectors(long unixSeconds, string expected)
    {
        Totp.Code(RfcSecret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))).Should().Be(expected);
    }

    [Fact]
    public void Verification_allows_one_step_of_drift_and_arabic_indic_digits()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = Totp.StepAt(now);
        Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 1), now).Should().Be(step - 1);
        Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 2), now).Should().BeNull();
        Totp.Verify(RfcSecret, "٠٠٥٩٢٤", now).Should().Be(step);
        Totp.Verify(RfcSecret, "abc", now).Should().BeNull();
    }

    [Fact]
    public void A_step_can_be_used_once()
    {
        var credential = Credential.Create(Guid.NewGuid(), "hash", new TestClock(DateTimeOffset.UnixEpoch));
        credential.BeginTotpSetup([1, 2, 3]);
        credential.EnableTotp(100);
        credential.TryUseTotpStep(100).Should().BeFalse();
        credential.TryUseTotpStep(101).Should().BeTrue();
        credential.TryUseTotpStep(101).Should().BeFalse();
    }

    [Fact]
    public void Base32_and_the_otpauth_uri_follow_the_authenticator_format()
    {
        Totp.Base32(Encoding.ASCII.GetBytes("foobar")).Should().Be("MZXW6YTBOI");
        Totp.OtpAuthUri("MZXW6YTBOI", "P-AB12-CD34").Should().Be("otpauth://totp/Tamaneen%3AP-AB12-CD34?secret=MZXW6YTBOI&issuer=Tamaneen&algorithm=SHA1&digits=6&period=30");
    }
}

public class PasswordAndCodeTests
{
    [Theory]
    [InlineData("short1!", false)]
    [InlineData("password1234", false)]
    [InlineData("aaaaaaaaaaaaaa", false)]
    [InlineData("abcdefghijklmnop", false)]
    [InlineData("Quiet-Morning-Walk-77", true)]
    [InlineData("ظل-النخيل-هادئ-2026", true)]
    public void The_password_policy_rejects_short_common_and_sequential_passwords(string password, bool ok)
    {
        PasswordService.IsStrong(password).Should().Be(ok);
    }

    [Fact]
    public void A_password_containing_the_nickname_or_contact_is_refused()
    {
        PasswordService.IsStrong("calmpalm-2026-xyz", "Calm Palm", null).Should().BeTrue();
        PasswordService.IsStrong("mynamecalmpalm77", "calmpalm").Should().BeFalse();
        PasswordService.IsStrong("x0551234567x-pw", null, "0551234567").Should().BeFalse();
        var act = () => PasswordService.EnsureStrong("password1234");
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.WeakPassword);
    }

    [Fact]
    public void Password_hashes_verify_and_never_repeat()
    {
        var service = new PasswordService();
        var a = service.Hash("Quiet-Morning-Walk-77");
        var b = service.Hash("Quiet-Morning-Walk-77");
        a.Should().NotBe(b).And.StartWith("pbkdf2-sha256$600000$");
        service.Verify(a, "Quiet-Morning-Walk-77").Should().Be(PasswordCheck.Succeeded);
        service.Verify(a, "quiet-morning-walk-77").Should().Be(PasswordCheck.Failed);
        service.Verify("pbkdf2-sha256$1000$" + a.Split('$')[2] + "$" + a.Split('$')[3], "x").Should().Be(PasswordCheck.Failed);
    }

    [Fact]
    public void Recovery_codes_are_readable_and_compared_loosely()
    {
        var code = OneTimeCodes.NewRecoveryCode();
        code.Should().MatchRegex("^[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}$");
        var user = Guid.NewGuid();
        OneTimeCodes.RecoveryHash(user, code.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal)).Should().Equal(OneTimeCodes.RecoveryHash(user, code));
        OneTimeCodes.RecoveryHash(Guid.NewGuid(), code).Should().NotEqual(OneTimeCodes.RecoveryHash(user, code));
    }

    [Theory]
    [InlineData("+966551234521", "phone", "••••4521")]
    [InlineData("sara.q@example.com", "email", "s•••@example.com")]
    public void Contacts_are_masked(string contact, string type, string masked)
    {
        OneTimeCodes.Mask(contact, type).Should().Be(masked);
    }

    [Fact]
    public void Server_secrets_are_bound_to_their_purpose()
    {
        var secrets = new ServerSecrets(Options.Create(new ServerSecretsOptions { ServerKeyBase64 = Convert.ToBase64String(new byte[32]) }));
        var sealedValue = secrets.Protect("totp", "secret");
        secrets.Unprotect("totp", sealedValue).Should().Be("secret");
        var wrongPurpose = () => secrets.Unprotect("challenge", sealedValue);
        wrongPurpose.Should().Throw<System.Security.Cryptography.CryptographicException>();
        secrets.Mac("a", "x").Should().NotEqual(secrets.Mac("b", "x"));
        var weak = () => new ServerSecrets(Options.Create(new ServerSecretsOptions { ServerKeyBase64 = Convert.ToBase64String(new byte[16]) }));
        weak.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("نخلة هادئة", true)]
    [InlineData("Calm Palm", true)]
    [InlineData("x", false)]
    [InlineData("me@example.com", false)]
    [InlineData("0551234567", false)]
    public void Nicknames_cannot_be_contacts(string nickname, bool ok)
    {
        Nicknames.IsAcceptable(nickname).Should().Be(ok);
    }

    [Fact]
    public void Suggested_nicknames_are_in_the_requested_language()
    {
        Nicknames.Suggest("ar").Should().MatchRegex(@"^\p{IsArabic}+ \p{IsArabic}+$");
        Nicknames.Suggest("en").Should().MatchRegex("^[A-Z][a-z]+ [A-Z][a-z]+$");
        Nicknames.IsAcceptable(Nicknames.Suggest("ar")).Should().BeTrue();
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Version/17.0 Mobile/15E148 Safari/604.1", "Safari · iPhone")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/128.0 Safari/537.36 Edg/128.0", "Edge · Windows")]
    [InlineData("", "unknown")]
    public void Device_labels_are_short_and_contain_no_identifiers(string userAgent, string label)
    {
        AuthService.DeviceLabel(userAgent).Should().Be(label);
    }
}

public class AuthDomainTests
{
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Five_failures_lock_for_fifteen_minutes_and_the_lock_expires()
    {
        var credential = Credential.Create(Guid.NewGuid(), "hash", _clock);
        Enumerable.Range(0, 4).Select(_ => credential.RecordFailure(_clock)).Should().Equal(4, 3, 2, 1);
        credential.RecordFailure(_clock).Should().Be(0);
        credential.IsLocked(_clock).Should().BeTrue();
        _clock.Advance(TimeSpan.FromMinutes(15));
        credential.IsLocked(_clock).Should().BeFalse();
        credential.RecordFailure(_clock).Should().Be(4, "the counter restarts after the lock");
    }

    [Fact]
    public void Challenges_expire_count_attempts_and_respect_the_resend_cooldown()
    {
        var challenge = AuthChallenge.Create(ChallengePurpose.Signup, Guid.NewGuid(), [1], [2], null, _clock);
        for (var i = 0; i < AuthChallenge.MaxAttempts; i++)
        {
            challenge.RegisterAttempt(_clock).Should().BeTrue();
        }

        challenge.IsUsable(_clock).Should().BeFalse();
        var tooSoon = () => challenge.Resend([3], _clock);
        tooSoon.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ResendTooSoon);
        _clock.Advance(TimeSpan.FromSeconds(61));
        challenge.Resend([3], _clock);
        challenge.IsUsable(_clock).Should().BeTrue("a resend gives fresh attempts");
        _clock.Advance(AuthChallenge.Lifetime + TimeSpan.FromSeconds(1));
        challenge.IsUsable(_clock).Should().BeFalse();
    }

    [Fact]
    public void Refresh_rotation_remembers_the_previous_token_and_revocation_is_final()
    {
        var session = AuthSession.Start(Guid.NewGuid(), "patient", "Safari · iPhone", [1], _clock);
        session.Rotate([2], _clock);
        session.PreviousRefreshTokenHash.Should().Equal(1);
        session.RefreshTokenHash.Should().Equal(2);
        session.Revoke(_clock);
        session.IsActive(_clock).Should().BeFalse();
        AuthSession.Start(Guid.NewGuid(), "staff", "x", [1], _clock).ExpiresAtUtc.Should().Be(_clock.UtcNow.AddHours(8));
    }
}
