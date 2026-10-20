using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

internal sealed class TestClock : IClock
{
    public TestClock(DateTimeOffset start) => UtcNow = start;

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan by) => UtcNow += by;
}
