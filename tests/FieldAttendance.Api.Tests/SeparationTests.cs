using System.Net.Http.Json;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>The field module shows field sites and field staff only; HR sees both workforces.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class SeparationTests(ApiFactory api)
{
    private const string Day = "/api/attendance?from=2026-09-22&to=2026-09-22";

    [Fact]
    public async Task The_field_attendance_log_holds_field_sites_only_and_the_office_log_office_branches_only()
    {
        var hr = await api.ClientForAsync("1005");
        var (fieldSites, officeSites) = await SiteNamesAsync();

        var field = (await hr.GetFromJsonAsync<JsonElement[]>(Day + "&workforce=Field"))!;
        var office = (await hr.GetFromJsonAsync<JsonElement[]>(Day + "&workforce=Office"))!;
        var all = (await hr.GetFromJsonAsync<JsonElement[]>(Day))!;

        Assert.NotEmpty(field);
        Assert.NotEmpty(office);
        Assert.All(field, r => Assert.Contains(r.GetProperty("locationName").GetString()!, fieldSites));
        Assert.All(office, r => Assert.Contains(r.GetProperty("locationName").GetString()!, officeSites));
        Assert.Equal(field.Length + office.Length, all.Length);
    }

    [Fact]
    public async Task The_field_dashboard_shows_field_sites_and_no_office_staff()
    {
        var admin = await api.ClientForAsync("1001");
        var (fieldSites, _) = await SiteNamesAsync();

        var dashboard = await admin.GetFromJsonAsync<JsonElement>("/api/dashboard");

        var board = dashboard.GetProperty("board").EnumerateArray().ToList();
        Assert.NotEmpty(board);
        Assert.All(board, b => Assert.Contains(b.GetProperty("nameAr").GetString()!, fieldSites));
        // Office staff used to appear here without a name, because only collectors were looked up.
        Assert.All(board.SelectMany(b => b.GetProperty("employees").EnumerateArray()),
            e => Assert.NotEqual("?", e.GetProperty("name").GetString()));
    }

    private Task<(HashSet<string> Field, HashSet<string> Office)> SiteNamesAsync() =>
        api.QueryAsync(async db =>
        {
            var all = await db.Locations.Select(l => new { l.NameAr, l.Kind }).ToListAsync();
            return (all.Where(l => l.Kind == LocationKind.Field).Select(l => l.NameAr).ToHashSet(),
                    all.Where(l => l.Kind == LocationKind.Office).Select(l => l.NameAr).ToHashSet());
        });
}
