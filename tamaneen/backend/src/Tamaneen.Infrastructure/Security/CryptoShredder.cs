using Microsoft.EntityFrameworkCore;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Security;

/// <summary>
/// Executes an approved deletion: destroys the patient's data key, clears ciphertext on the profile and
/// writes an audit row, all in one transaction. The ledger keeps the content-free audit trail.
/// </summary>
public sealed class CryptoShredder
{
    private readonly TamaneenDbContext _db;
    private readonly IPatientKeyStore _keys;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public CryptoShredder(TamaneenDbContext db, IPatientKeyStore keys, IAuditLog audit, IClock clock)
    {
        _db = db;
        _keys = keys;
        _audit = audit;
        _clock = clock;
    }

    public async Task ShredAsync(Guid patientId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(ErrorCodes.ReasonRequired, "A reason is required to shred patient data.");
        }

        var profile = await _db.PatientProfiles.FirstOrDefaultAsync(p => p.PatientId == patientId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException(ErrorCodes.NotFound, $"Patient {patientId} was not found.");

        await _keys.ShredAsync(patientId, cancellationToken).ConfigureAwait(false);
        profile.MarkShredded(_clock);
        _audit.Append("patient.crypto_shred", "PatientProfile", null, profile.PseudonymId, reason.Trim());
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
