namespace FieldAttendance.Domain.Common;

internal static class Guard
{
    public static string Required(string? value, string code, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(code, $"{code}: value is required.");
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new DomainException(code, $"{code}: exceeds {maxLength} characters.");
        return trimmed;
    }

    public static string? Optional(string? value, string code, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new DomainException(code, $"{code}: exceeds {maxLength} characters.");
        return trimmed;
    }

    public static int InRange(int value, int min, int max, string code)
    {
        if (value < min || value > max)
            throw new DomainException(code, $"{code}: must be between {min} and {max}.");
        return value;
    }

    public static Guid NotEmpty(Guid value, string code)
    {
        if (value == Guid.Empty)
            throw new DomainException(code, $"{code}: identifier is required.");
        return value;
    }
}
