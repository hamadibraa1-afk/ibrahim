using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Security;

public sealed class PatientKeyStore : IPatientKeyStore
{
    private readonly TamaneenDbContext _db;
    private readonly IKeyEncryptionKeyProvider _kek;
    private readonly IMemoryCache _cache;
    private readonly IClock _clock;
    private readonly TimeSpan _cacheFor;

    public PatientKeyStore(TamaneenDbContext db, IKeyEncryptionKeyProvider kek, IMemoryCache cache, IClock clock, IOptions<EncryptionOptions> options)
    {
        _db = db;
        _kek = kek;
        _cache = cache;
        _clock = clock;
        _cacheFor = TimeSpan.FromSeconds(options.Value.DataKeyCacheSeconds);
    }

    public async Task<byte[]> GetOrCreateDataKeyAsync(Guid patientId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey(patientId), out byte[]? cached) && cached is not null)
        {
            return cached;
        }

        var row = await FindAsync(patientId, cancellationToken).ConfigureAwait(false);
        if (row is not null)
        {
            return await UnwrapAsync(row, cancellationToken).ConfigureAwait(false);
        }

        var dataKey = RandomNumberGenerator.GetBytes(32);
        var wrapped = await _kek.WrapAsync(dataKey, cancellationToken).ConfigureAwait(false);
        _db.PatientKeys.Add(new PatientKey(patientId, wrapped, _kek.CurrentKeyId, _clock.UtcNow));

        // Not cached until it is persisted: if the unit of work is rolled back, nothing may have been
        // encrypted with a key that does not exist in the database.
        return dataKey;
    }

    public async Task<byte[]> GetDataKeyAsync(Guid patientId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey(patientId), out byte[]? cached) && cached is not null)
        {
            return cached;
        }

        var row = await FindAsync(patientId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"No data key exists for patient {patientId}.");
        return await UnwrapAsync(row, cancellationToken).ConfigureAwait(false);
    }

    public async Task ShredAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(patientId, cancellationToken).ConfigureAwait(false);
        row?.Shred(_clock.UtcNow);
        _cache.Remove(CacheKey(patientId));
    }

    private async Task<PatientKey?> FindAsync(Guid patientId, CancellationToken cancellationToken) =>
        _db.PatientKeys.Local.FirstOrDefault(k => k.PatientId == patientId)
        ?? await _db.PatientKeys.FirstOrDefaultAsync(k => k.PatientId == patientId, cancellationToken).ConfigureAwait(false);

    private async Task<byte[]> UnwrapAsync(PatientKey row, CancellationToken cancellationToken)
    {
        if (row.IsShredded || row.WrappedKey is null)
        {
            throw new PatientDataShreddedException(row.PatientId);
        }

        var dataKey = await _kek.UnwrapAsync(row.WrappedKey, row.KeyEncryptionKeyId, cancellationToken).ConfigureAwait(false);
        Remember(row.PatientId, dataKey);
        return dataKey;
    }

    private void Remember(Guid patientId, byte[] dataKey)
    {
        if (_cacheFor > TimeSpan.Zero)
        {
            _cache.Set(CacheKey(patientId), dataKey, _cacheFor);
        }
    }

    private static string CacheKey(Guid patientId) => "pdk:" + patientId.ToString("N");
}
