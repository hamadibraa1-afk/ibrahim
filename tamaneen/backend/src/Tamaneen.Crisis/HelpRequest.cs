using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis;

/// <summary>
/// One press of "Help now" (PB-26), keyed by the id the patient's device generated for it. The device keeps
/// that id while the request is queued offline (EC-04) and on every retry, so a double tap or a resend after a
/// lost response is answered from here instead of reaching the crisis gate a second time.
/// </summary>
public sealed class HelpRequest : Entity
{
    private HelpRequest(Guid id, Guid patientId, Guid requestKey, Guid? crisisEventId, DateTimeOffset receivedAtUtc)
        : base(id)
    {
        PatientId = patientId;
        RequestKey = requestKey;
        CrisisEventId = crisisEventId;
        ReceivedAtUtc = receivedAtUtc;
    }

    // EF Core
    private HelpRequest()
    {
    }

    public Guid PatientId { get; private set; }

    /// <summary>The device-generated idempotency key. Unique per patient.</summary>
    public Guid RequestKey { get; private set; }

    /// <summary>The event the request opened or joined; null when the gate failed closed without one.</summary>
    public Guid? CrisisEventId { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public static HelpRequest Record(Guid patientId, Guid requestKey, Guid? crisisEventId, IClock clock)
    {
        if (requestKey == Guid.Empty)
        {
            throw new ArgumentException("Request key must not be empty.", nameof(requestKey));
        }

        return new HelpRequest(Guid.NewGuid(), patientId, requestKey, crisisEventId, clock.UtcNow);
    }
}
