namespace Tamaneen.SharedKernel;

/// <summary>Abstracts the current time so SLA timers and expiry rules can be tested.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
