using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Entities;

/// <summary>A single configurable value, so policy numbers live in the database, not in code.</summary>
public sealed class SystemSetting : Entity
{
    private SystemSetting() { }

    public SystemSetting(string key, string value)
    {
        Key = Guard.Required(key, "setting.key", 100);
        SetValue(value);
    }

    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;

    public void SetValue(string value) => Value = Guard.Required(value, "setting.value", 500);
}
