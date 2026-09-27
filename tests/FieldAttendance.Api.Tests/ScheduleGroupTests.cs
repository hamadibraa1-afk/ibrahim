using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>A work schedule is a group: HR moves several office employees onto it at once, from a date.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class ScheduleGroupTests(ApiFactory api)
{
    private static readonly DateOnly Tomorrow = new(2026, 9, 23);

    [Fact]
    public async Task Hr_moves_several_employees_onto_a_schedule_and_their_days_follow_it()
    {
        var hr = await api.ClientForAsync("1005");
        var branchHours = await ScheduleIdAsync("Branch hours");
        var movers = await HeadOfficeEmployeesAsync(3);

        var response = await hr.PostAsJsonAsync($"/api/hr/work-schedules/{branchHours}/members",
            new { profileIds = movers.Select(m => m.ProfileId), effectiveFrom = Tomorrow });

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var members = (await hr.GetFromJsonAsync<JsonElement[]>($"/api/hr/work-schedules/{branchHours}/members"))!
            .Select(m => m.GetProperty("profileId").GetGuid()).ToHashSet();
        Assert.All(movers, m => Assert.Contains(m.ProfileId, members));

        // Branch hours start at 07:30; head office at 08:00. Tomorrow's day follows the new group.
        var starts = await api.QueryAsync(db => db.AttendanceRecords
            .Where(r => movers.Select(m => m.UserId).Contains(r.EmployeeId) && r.ShiftDate == Tomorrow)
            .Select(r => r.ScheduledStart).ToListAsync());
        Assert.Equal(3, starts.Count);
        Assert.All(starts, s => Assert.Equal(new TimeOnly(7, 30), TimeOnly.FromDateTime(s.ToOffset(TimeSpan.FromHours(4)).DateTime)));
    }

    [Fact]
    public async Task A_group_change_with_a_field_employee_in_it_changes_nobody()
    {
        var hr = await api.ClientForAsync("1005");
        var branchHours = await ScheduleIdAsync("Branch hours");
        var office = (await HeadOfficeEmployeesAsync(4)).Last();
        var field = await api.QueryAsync(db => db.EmployeeProfiles.Where(p => p.Workforce == Workforce.Field).Select(p => p.Id).FirstAsync());

        var response = await hr.PostAsJsonAsync($"/api/hr/work-schedules/{branchHours}/members",
            new { profileIds = new[] { office.ProfileId, field }, effectiveFrom = Tomorrow });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var schedule = await api.QueryAsync(db => db.EmployeeProfiles.Where(p => p.Id == office.ProfileId).Select(p => p.WorkScheduleId).SingleAsync());
        Assert.NotEqual(branchHours, schedule);
    }

    private Task<Guid> ScheduleIdAsync(string nameEn) =>
        api.QueryAsync(db => db.WorkSchedules.Where(s => s.NameEn == nameEn).Select(s => s.Id).SingleAsync());

    /// <summary>Head-office employees on office hours, lowest numbers first, so other tests' people stay put.</summary>
    private async Task<List<(Guid ProfileId, Guid UserId)>> HeadOfficeEmployeesAsync(int count)
    {
        var office = await ScheduleIdAsync("Office hours");
        var rows = await api.QueryAsync(db => (
            from p in db.EmployeeProfiles join u in db.Users on p.UserId equals u.Id
            where p.WorkScheduleId == office && u.Role == UserRole.Employee && p.Workforce == Workforce.Office
            orderby u.EmployeeNumber
            select new { p.Id, p.UserId }).Take(count).ToListAsync());
        return rows.Select(r => (r.Id, r.UserId)).ToList();
    }
}
