using System.Security.Cryptography;
using Tamaneen.Identity;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

public class ContactHasherTests
{
    private static readonly byte[] Pepper = RandomNumberGenerator.GetBytes(32);

    [Theory]
    [InlineData("  Noura@Example.COM ", "e:noura@example.com")]
    [InlineData("+966 55 123 4567", "p:+966551234567")]
    [InlineData("00966551234567", "p:+966551234567")]
    [InlineData("٠٥٥١٢٣٤٥٦٧", "p:0551234567")]
    [InlineData("۰۵۵-۱۲۳-۴۵۶۷", "p:0551234567")]
    public void Normalize_produces_one_canonical_form(string input, string expected) =>
        ContactHasher.Normalize(input).Should().Be(expected);

    [Fact]
    public void Same_person_written_differently_hashes_the_same()
    {
        var hasher = new ContactHasher(Pepper);
        hasher.Hash("٠٥٥١٢٣٤٥٦٧").Should().Equal(hasher.Hash("055 123 4567"));
        hasher.Hash("A@B.co").Should().Equal(hasher.Hash("a@b.co"));
        hasher.Hash("a@b.co").Should().HaveCount(32);
    }

    [Fact]
    public void Different_pepper_gives_a_different_hash()
    {
        var a = new ContactHasher(RandomNumberGenerator.GetBytes(32)).Hash("a@b.co");
        var b = new ContactHasher(RandomNumberGenerator.GetBytes(32)).Hash("a@b.co");
        a.Should().NotEqual(b);
    }

    [Fact]
    public void Short_pepper_and_garbage_input_are_rejected()
    {
        var shortPepper = () => new ContactHasher(new byte[16]);
        shortPepper.Should().Throw<ArgumentException>();
        var garbage = () => ContactHasher.Normalize("abc");
        garbage.Should().Throw<ArgumentException>();
    }
}

public class UserAccountTests
{
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Register_assigns_a_pseudonym_and_normalizes_language()
    {
        var account = UserAccount.Register(AppRoles.Patient, new byte[32], "en-GB", _clock);
        PseudonymId.TryParse(account.PseudonymId, out _).Should().BeTrue();
        account.PreferredLanguage.Should().Be("en");
        account.Status.Should().Be(AccountStatus.PendingVerification);
        account.CreatedAtUtc.Should().Be(_clock.UtcNow);
    }

    [Theory]
    [InlineData(AppRoles.CrisisService)]
    [InlineData(AppRoles.DataProtectionService)]
    [InlineData("Root")]
    public void Service_and_unknown_roles_cannot_be_given_to_a_person(string role)
    {
        var act = () => UserAccount.Register(role, new byte[32], "ar", _clock);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unsupported_language_is_a_domain_error_with_the_language_as_argument()
    {
        var account = UserAccount.Register(AppRoles.Therapist, new byte[32], "ar", _clock);
        var act = () => account.ChangeLanguage("fr");
        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be(ErrorCodes.UnsupportedLanguage);
        error.Arguments.Should().ContainSingle().Which.Should().Be("fr");
        account.ChangeLanguage("EN");
        account.PreferredLanguage.Should().Be("en");
    }

    [Fact]
    public void Contact_hash_must_be_32_bytes()
    {
        var act = () => UserAccount.Register(AppRoles.Patient, new byte[16], "ar", _clock);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Patient_profile_requires_a_patient_and_clears_ciphertext_on_shred()
    {
        var staff = UserAccount.Register(AppRoles.Therapist, new byte[32], "ar", _clock);
        var wrongRole = () => PatientProfile.Create(staff, "RUH", _clock);
        wrongRole.Should().Throw<ArgumentException>();

        var patient = UserAccount.Register(AppRoles.Patient, new byte[32], "ar", _clock);
        var profile = PatientProfile.Create(patient, " ruh ", _clock);
        profile.CityCode.Should().Be("RUH");
        profile.PseudonymId.Should().Be(patient.PseudonymId);
        profile.SetEncryptedFields(new byte[] { 1 }, new byte[] { 2 }, new byte[] { 3 });

        profile.MarkShredded(_clock);
        profile.IsShredded.Should().BeTrue();
        profile.DisplayNameCipher.Should().BeNull();
        profile.EmergencyContactCipher.Should().BeNull();
        var write = () => profile.SetEncryptedFields(new byte[] { 1 }, null, null);
        write.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.PatientDataShredded);
    }
}
