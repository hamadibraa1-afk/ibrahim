using System.Text.Json;
using System.Text.Json.Serialization;
using FieldAttendance.Domain.Time;

namespace FieldAttendance.Api.Common;

/// <summary>
/// Every instant leaves the API in the organisation's local time (Attendance:TimeZone).
///
/// Storage is UTC and EF reads instants back as UTC, while the client displays the wall-clock
/// part of the string it receives. Serialised as UTC, every time read from the database showed
/// four hours early in the UAE. Converting here, once, keeps storage untouched and covers every
/// endpoint; nullable instants are handled by the serializer through this same converter.
/// </summary>
public sealed class OrgTimeJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(OrgTime.ToLocal(value));
    }
}
