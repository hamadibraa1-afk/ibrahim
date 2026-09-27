using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>
/// Storage is UTC and EF reads instants back as UTC. The client shows the wall-clock part of the
/// string it receives, so an instant leaving the API in UTC displayed every time four hours early
/// in the UAE: a 07:30 shift start read "03:30".
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class TimeZoneOutputTests(ApiFactory api)
{
    /// <summary>The demo data's two office start times: branches at 07:30, head office at 08:00.</summary>
    private static readonly string[] LocalStarts = ["07:30", "08:00"];

    [Fact]
    public async Task Instants_read_from_the_database_leave_the_api_in_uae_time()
    {
        var hr = await api.ClientForAsync("1005");

        var rows = await hr.GetFromJsonAsync<JsonElement[]>("/api/attendance?from=2026-09-21&to=2026-09-21");

        var row = rows!.First(r => r.GetProperty("checkInAt").ValueKind == JsonValueKind.String);
        var start = row.GetProperty("scheduledStart").GetString()!;
        Assert.EndsWith("+04:00", start);
        Assert.Contains(start.Substring(11, 5), LocalStarts);
        Assert.EndsWith("+04:00", row.GetProperty("checkInAt").GetString()!);
    }
}
