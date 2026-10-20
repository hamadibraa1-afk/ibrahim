using Tamaneen.AiGateway;
using Tamaneen.AiGateway.Gateway;
using Tamaneen.Infrastructure.Persistence;

namespace Tamaneen.Infrastructure.Ai;

/// <summary>Stores AI call metadata. The record type has no content fields, so no text can end up here.</summary>
internal sealed class AiCallLog(TamaneenDbContext db) : IAiCallLog
{
    public async Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken)
    {
        db.AiCallRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
