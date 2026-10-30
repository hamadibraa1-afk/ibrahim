using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Care;

/// <summary>
/// The patient asked to continue care (§10.4, S57: «أو طلب المريض الاستمرار»). Two ways in:
/// <list type="bullet">
///   <item><c>patient</c>: during care, from the patient's app (POST /v1/me/care/continue); it blocks closing until the
///   patient withdraws it;</item>
///   <item><c>return</c>: P53's way back after a closure («أحتاج أرجع للجلسات», POST /v1/me/care/return), linked to that
///   closure. It is met once care starts again, so it never blocks the next episode's closure on its own.</item>
/// </list>
/// Encrypted text is not needed: the request holds no free text.
/// </summary>
public sealed class CareContinueRequest : Entity
{
    public const string FromPatient = "patient";
    public const string FromReturn = "return";

    private CareContinueRequest(Guid id, Guid patientId, string source, Guid? closureId, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        Source = source;
        ClosureId = closureId;
        RequestedAtUtc = now;
    }

    // EF Core
    private CareContinueRequest()
    {
        Source = string.Empty;
    }

    public Guid PatientId { get; private set; }

    /// <summary><see cref="FromPatient"/> or <see cref="FromReturn"/>.</summary>
    public string Source { get; private set; }

    /// <summary>For <see cref="FromReturn"/>: the closure the patient came back from (P53).</summary>
    public Guid? ClosureId { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset? WithdrawnAtUtc { get; private set; }

    public bool IsOpen => WithdrawnAtUtc is null;

    public static CareContinueRequest ByPatient(Guid patientId, IClock clock) => new(Guid.NewGuid(), patientId, FromPatient, null, clock.UtcNow);

    public static CareContinueRequest ByReturn(Guid patientId, Guid closureId, IClock clock) => new(Guid.NewGuid(), patientId, FromReturn, closureId, clock.UtcNow);

    public void Withdraw(IClock clock) => WithdrawnAtUtc ??= clock.UtcNow;
}
