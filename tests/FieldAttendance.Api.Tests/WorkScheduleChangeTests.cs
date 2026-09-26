using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class WorkScheduleChangeTests(ApiFactory api)
{
    /// <summary>
    /// A rule change needs a new shift template, created in the same save as the assignments that
    /// use it. Validation used to look templates up in the database only, so every such change was
    /// refused with schedule.shift_missing after the schedule itself had already been saved.
    /// The grace period is changed rather than flexible hours, so other tests keep their 30 minutes.
    /// </summary>
    [Fact]
    public async Task Changing_a_schedules_rules_reaches_the_employees_on_it()
    {
        var hr = await api.ClientForAsync("1005");
        var schedule = (await hr.GetFromJsonAsync<JsonElement[]>("/api/hr/work-schedules"))!
            .Single(s => s.GetProperty("nameEn").GetString() == "Office hours");

        var response = await hr.PutAsJsonAsync($"/api/hr/work-schedules/{schedule.GetProperty("id").GetGuid()}", new
        {
            nameAr = schedule.GetProperty("nameAr").GetString(),
            nameEn = schedule.GetProperty("nameEn").GetString(),
            graceMinutes = 12,
            earlyCheckInMinutes = schedule.GetProperty("earlyCheckInMinutes").GetInt32(),
            countEarlyArrivalAsOvertime = false,
            flexMinutes = schedule.GetProperty("flexMinutes").GetInt32(),
            days = schedule.GetProperty("days"),
            rowVersion = schedule.GetProperty("rowVersion").GetString(),
        });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var hrManager = await api.UserIdAsync("1005");
        var tomorrow = new DateOnly(2026, 9, 23);
        var grace = await api.QueryAsync(db => db.AttendanceRecords
            .Where(r => r.EmployeeId == hrManager && r.ShiftDate == tomorrow).Select(r => r.GraceMinutes).SingleAsync());
        Assert.Equal(12, grace);
    }
}
