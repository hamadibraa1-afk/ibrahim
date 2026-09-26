using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

public sealed class User : Entity
{
    private User() { } // EF Core

    public User(string fullName, string? email, string phone, UserRole role, string employeeNumber, string preferredLanguage)
    {
        Role = role;
        UpdateProfile(fullName, email, phone, employeeNumber);
        SetPreferredLanguage(preferredLanguage);
    }

    public string FullName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string EmployeeNumber { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public string? PhotoUrl { get; private set; }
    public string? PasswordHash { get; private set; }
    public string PreferredLanguage { get; private set; } = "ar";
    public bool SmsOtpEnabled { get; private set; }

    /// <summary>The employee number is the login identifier, so it is required for every role.</summary>
    public void UpdateProfile(string fullName, string? email, string phone, string employeeNumber)
    {
        FullName = Guard.Required(fullName, "user.full_name", 150);
        Email = Guard.Optional(email, "user.email", 254)?.ToLowerInvariant();
        if (Email is not null && !Email.Contains('@', StringComparison.Ordinal))
            throw new DomainException("user.email_invalid", "Email is not valid.");
        Phone = NormalizePhone(phone);
        EmployeeNumber = Guard.Required(employeeNumber, "user.employee_number", 30);
    }

    public void ChangeRole(UserRole role) => Role = role;

    public void SetPreferredLanguage(string language) =>
        PreferredLanguage = language is "ar" or "en"
            ? language
            : throw new DomainException("user.language_invalid", "Language must be 'ar' or 'en'.");

    public void SetPasswordHash(string hash) => PasswordHash = Guard.Required(hash, "user.password_hash", 500);
    public void SetPhoto(string? url) => PhotoUrl = Guard.Optional(url, "user.photo_url", 500);
    public void SetSmsOtp(bool enabled) => SmsOtpEnabled = enabled;

    /// <summary>Stores UAE mobile numbers in E.164 (+9715XXXXXXXX) so the SMS gateway always gets one format.</summary>
    internal static string NormalizePhone(string phone)
    {
        var digits = new string(Guard.Required(phone, "user.phone", 20).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.StartsWith("05", StringComparison.Ordinal) && digits.Length == 10) digits = "971" + digits[1..];
        if (digits.StartsWith('5') && digits.Length == 9) digits = "971" + digits;
        if (digits.Length is < 8 or > 15)
            throw new DomainException("user.phone_invalid", "Phone number is not valid.");
        return "+" + digits;
    }
}
