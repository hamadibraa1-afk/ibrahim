using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tamaneen.Infrastructure.Idempotency;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Patients;

/// <summary>
/// Makes a patient write idempotent by its <c>Idempotency-Key</c> header (EC-01, EC-07). The patient app's outbox
/// (P66) sends every queued write with a key that stays the same across retries. The first request with a key
/// reserves it and runs; once it succeeded its response is stored, encrypted with the patient's key, and every
/// repeat is answered with that first response (header <c>Idempotent-Replayed: true</c>) without running again,
/// so the entry is saved once and its text reaches the crisis gate once. A failed first request releases the key,
/// so the retry runs for real. A request without the header behaves as before.
/// </summary>
public sealed class IdempotencyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>A reservation this old belongs to a request that died before finishing; a retry may take it over.</summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(2);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return await next(context).ConfigureAwait(false);
        }

        if (values.Count != 1 || !Guid.TryParse(values[0], out var key) || key == Guid.Empty)
        {
            throw new DomainException(ErrorCodes.IdempotencyKeyInvalid, "Idempotency-Key must be one non-empty GUID.");
        }

        var services = http.RequestServices;
        var p = services.GetRequiredService<PatientContext>();
        var clock = services.GetRequiredService<IClock>();
        var ct = http.RequestAborted;
        var method = http.Request.Method;
        var path = (http.Request.Path.Value ?? string.Empty).ToLowerInvariant();

        var existing = await p.Db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.PatientId == p.PatientId && r.Key == key, ct).ConfigureAwait(false);
        Guid recordId;
        if (existing is not null)
        {
            if (!string.Equals(existing.Method, method, StringComparison.Ordinal) || !string.Equals(existing.Path, path, StringComparison.Ordinal))
            {
                throw new DomainException(ErrorCodes.IdempotencyKeyReused, $"Idempotency key {key} was first used for {existing.Method} {existing.Path}.");
            }

            if (existing.IsCompleted)
            {
                return await ReplayAsync(http, p, existing, ct).ConfigureAwait(false);
            }

            if (clock.UtcNow - existing.ReservedAtUtc < AbandonedAfter)
            {
                throw InProgress(key);
            }

            // The first request died before finishing. Take its reservation over, unless another retry just did.
            var reservedAt = existing.ReservedAtUtc;
            var taken = await p.Db.IdempotencyRecords.Where(r => r.Id == existing.Id && r.ReservedAtUtc == reservedAt && r.CompletedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReservedAtUtc, clock.UtcNow), ct).ConfigureAwait(false);
            if (taken == 0)
            {
                throw InProgress(key);
            }

            recordId = existing.Id;
        }
        else
        {
            var record = IdempotencyRecord.Reserve(p.PatientId, key, method, path, clock);
            p.Db.IdempotencyRecords.Add(record);
            try
            {
                await p.Db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_IdempotencyRecords_PatientKey", StringComparison.Ordinal) == true)
            {
                // The same write arrived twice at once; the other copy runs it and the client retries for its result.
                throw InProgress(key);
            }
            finally
            {
                p.Db.Entry(record).State = EntityState.Detached;
            }

            recordId = record.Id;
        }

        object? result;
        try
        {
            result = await next(context).ConfigureAwait(false);
        }
        catch
        {
            await ReleaseAsync(p, recordId).ConfigureAwait(false);
            throw;
        }

        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        if (status is < 200 or >= 300)
        {
            await ReleaseAsync(p, recordId).ConfigureAwait(false);
            return result;
        }

        byte[]? cipher = null;
        if (result is IValueHttpResult { Value: { } value })
        {
            var json = JsonSerializer.Serialize(value, value.GetType(), SerializerOptions(services));
            cipher = await p.EncryptAsync(Fields.IdempotentResponse, json, ct).ConfigureAwait(false);
        }

        var completedAt = clock.UtcNow;
        await p.Db.IdempotencyRecords.Where(r => r.Id == recordId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StatusCode, status).SetProperty(r => r.ResponseCipher, cipher).SetProperty(r => r.CompletedAtUtc, completedAt), CancellationToken.None)
            .ConfigureAwait(false);
        return result;
    }

    private static async Task<IResult> ReplayAsync(HttpContext http, PatientContext p, IdempotencyRecord record, CancellationToken ct)
    {
        http.Response.Headers[ReplayedHeader] = "true";
        var status = record.StatusCode ?? StatusCodes.Status200OK;
        if (record.ResponseCipher is null)
        {
            return TypedResults.StatusCode(status);
        }

        var json = await p.DecryptAsync(Fields.IdempotentResponse, record.ResponseCipher, ct).ConfigureAwait(false);
        return TypedResults.Text(json, "application/json; charset=utf-8", statusCode: status);
    }

    /// <summary>Frees the key after a failed first request, so its retry runs instead of waiting for a result that never comes.</summary>
    private static Task<int> ReleaseAsync(PatientContext p, Guid recordId) =>
        p.Db.IdempotencyRecords.Where(r => r.Id == recordId && r.CompletedAtUtc == null).ExecuteDeleteAsync(CancellationToken.None);

    private static DomainException InProgress(Guid key) =>
        new DomainException(ErrorCodes.IdempotencyInProgress, $"Idempotency key {key} is still being processed.").With("retryAfterSeconds", 2);

    private static JsonSerializerOptions SerializerOptions(IServiceProvider services) =>
        services.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? PatientContext.Json;
}

public static class IdempotencyEndpointExtensions
{
    /// <summary>Answers a repeated <c>Idempotency-Key</c> with the first result (see <see cref="IdempotencyFilter"/>).</summary>
    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder) => builder.AddEndpointFilter<IdempotencyFilter>();
}
