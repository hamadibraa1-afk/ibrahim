namespace Tamaneen.SharedKernel;

/// <summary>
/// Clinical risk level. Numeric order matters: a higher value is always more severe,
/// and the highest level triggered wins. AI may raise a level but never lower it.
/// </summary>
public enum RiskLevel
{
    None = 0,
    Routine = 1,
    Elevated = 2,
    High = 3,
    Critical = 4,
}
