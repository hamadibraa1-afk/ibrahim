using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Tamaneen.Infrastructure;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

public class LocalKeyEncryptionKeyProviderTests
{
    [Fact]
    public async Task Wrap_then_unwrap_returns_the_data_key()
    {
        var kek = new LocalKeyEncryptionKeyProvider(RandomNumberGenerator.GetBytes(32));
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrapped = await kek.WrapAsync(dataKey, default);
        wrapped.Should().NotEqual(dataKey);
        (await kek.UnwrapAsync(wrapped, kek.CurrentKeyId, default)).Should().Equal(dataKey);
    }

    [Fact]
    public async Task Tampered_wrap_or_wrong_key_fails()
    {
        var kek = new LocalKeyEncryptionKeyProvider(RandomNumberGenerator.GetBytes(32));
        var wrapped = await kek.WrapAsync(RandomNumberGenerator.GetBytes(32), default);
        wrapped[^1] ^= 0xFF;
        var tampered = () => kek.UnwrapAsync(wrapped, kek.CurrentKeyId, default);
        await tampered.Should().ThrowAsync<CryptographicException>();

        var other = new LocalKeyEncryptionKeyProvider(RandomNumberGenerator.GetBytes(32));
        var wrongKey = () => other.UnwrapAsync(wrapped, kek.CurrentKeyId, default);
        await wrongKey.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public void Local_provider_is_refused_in_production()
    {
        var options = new EncryptionOptions { Provider = KeyEncryptionProvider.Local, LocalKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) };
        var production = () => DependencyInjection.CreateKekProvider(options, new FakeEnvironment(Environments.Production));
        production.Should().Throw<OptionsValidationException>();
        DependencyInjection.CreateKekProvider(options, new FakeEnvironment(Environments.Development)).Should().BeOfType<LocalKeyEncryptionKeyProvider>();
    }

    [Fact]
    public void Key_vault_provider_needs_an_absolute_key_uri()
    {
        var options = new EncryptionOptions { Provider = KeyEncryptionProvider.AzureKeyVault, KeyVaultKeyId = "patient-kek" };
        var act = () => DependencyInjection.CreateKekProvider(options, new FakeEnvironment(Environments.Production));
        act.Should().Throw<OptionsValidationException>();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public FakeEnvironment(string name) => EnvironmentName = name;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public class PatientDataProtectorTests
{
    private readonly InMemoryKeyStore _keys = new();

    [Fact]
    public async Task Round_trip_in_arabic_and_english()
    {
        var protector = new PatientDataProtector(_keys);
        var patient = Guid.NewGuid();
        foreach (var text in new[] { "نورة محمد", "Noura M.", "+966 55 123 4567" })
        {
            var cipher = await protector.EncryptAsync(patient, ProtectedFields.DisplayName, text, default);
            cipher[0].Should().Be(1);
            (await protector.DecryptAsync(patient, ProtectedFields.DisplayName, cipher, default)).Should().Be(text);
        }
    }

    [Fact]
    public async Task Same_text_encrypts_differently_each_time()
    {
        var protector = new PatientDataProtector(_keys);
        var patient = Guid.NewGuid();
        var a = await protector.EncryptAsync(patient, ProtectedFields.DisplayName, "نورة", default);
        var b = await protector.EncryptAsync(patient, ProtectedFields.DisplayName, "نورة", default);
        a.Should().NotEqual(b);
    }

    [Fact]
    public async Task Ciphertext_moved_to_another_patient_or_field_does_not_decrypt()
    {
        var protector = new PatientDataProtector(_keys);
        var patient = Guid.NewGuid();
        var cipher = await protector.EncryptAsync(patient, ProtectedFields.EmergencyContact, "أختي 0551234567", default);

        var otherField = () => protector.DecryptAsync(patient, ProtectedFields.DisplayName, cipher, default);
        await otherField.Should().ThrowAsync<CryptographicException>();

        var other = Guid.NewGuid();
        _keys.Share(patient, other);
        var otherPatient = () => protector.DecryptAsync(other, ProtectedFields.EmergencyContact, cipher, default);
        await otherPatient.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Tampering_is_detected()
    {
        var protector = new PatientDataProtector(_keys);
        var patient = Guid.NewGuid();
        var cipher = await protector.EncryptAsync(patient, ProtectedFields.DateOfBirth, "1995-04-02", default);
        cipher[^1] ^= 0x01;
        var act = () => protector.DecryptAsync(patient, ProtectedFields.DateOfBirth, cipher, default);
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task After_shredding_nothing_decrypts()
    {
        var protector = new PatientDataProtector(_keys);
        var patient = Guid.NewGuid();
        var cipher = await protector.EncryptAsync(patient, ProtectedFields.DisplayName, "Noura", default);
        await _keys.ShredAsync(patient, default);
        var act = () => protector.DecryptAsync(patient, ProtectedFields.DisplayName, cipher, default);
        (await act.Should().ThrowAsync<PatientDataShreddedException>()).Which.Code.Should().Be(ErrorCodes.PatientDataShredded);
    }

    private sealed class InMemoryKeyStore : IPatientKeyStore
    {
        private readonly Dictionary<Guid, byte[]?> _keys = new();

        public Task<byte[]> GetOrCreateDataKeyAsync(Guid patientId, CancellationToken cancellationToken)
        {
            if (!_keys.TryGetValue(patientId, out var key))
            {
                key = RandomNumberGenerator.GetBytes(32);
                _keys[patientId] = key;
            }

            return key is null ? throw new PatientDataShreddedException(patientId) : Task.FromResult(key);
        }

        public Task<byte[]> GetDataKeyAsync(Guid patientId, CancellationToken cancellationToken) =>
            _keys.TryGetValue(patientId, out var key) && key is not null
                ? Task.FromResult(key)
                : throw new PatientDataShreddedException(patientId);

        public Task ShredAsync(Guid patientId, CancellationToken cancellationToken)
        {
            _keys[patientId] = null;
            return Task.CompletedTask;
        }

        public void Share(Guid from, Guid to) => _keys[to] = _keys[from];
    }
}
