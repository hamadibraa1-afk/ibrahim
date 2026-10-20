namespace Tamaneen.AiGateway.Deidentification;

public enum PiiType
{
    Person = 0,
    Phone = 1,
    Email = 2,
    NationalId = 3,
    Iban = 4,
    CreditCard = 5,
    Url = 6,
    IpAddress = 7,
    Address = 8,
    Date = 9,
}

/// <summary>A span of personal data found in a text. Start and Length index the original string.</summary>
public sealed record PiiSpan(int Start, int Length, PiiType Type, double Score)
{
    public int End => Start + Length;
}

/// <summary>Values that identify this particular patient (their own name, emergency contact name, city).</summary>
public sealed record KnownIdentifiers(IReadOnlyList<string> Names, IReadOnlyList<string> Places)
{
    public static KnownIdentifiers None { get; } = new(Array.Empty<string>(), Array.Empty<string>());
}

public interface IPiiDetector
{
    Task<IReadOnlyList<PiiSpan>> DetectAsync(string text, string language, KnownIdentifiers known, CancellationToken cancellationToken);
}
