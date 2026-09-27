using System.Net;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>
/// One employee portal for everyone. Field and office staff see the same screens about
/// themselves; only where their requests go differs. The portal endpoints used to accept a list
/// of office roles, so a collector or a supervisor was refused their own profile and payslips.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UnifiedPortalTests(ApiFactory api)
{
    [Theory]
    [InlineData("2001", "/api/my/profile")]
    [InlineData("2001", "/api/my/attendance")]
    [InlineData("2001", "/api/my/payslips")]
    [InlineData("2001", "/api/my/warnings")]
    [InlineData("2001", "/api/my/returns")]
    [InlineData("1002", "/api/my/profile")]
    [InlineData("1002", "/api/my/attendance")]
    [InlineData("3010", "/api/my/profile")]
    [InlineData("1001", "/api/my/profile")]
    [InlineData(ApiFactory.LegacyAccount, "/api/my/profile")]
    public async Task Every_employee_opens_their_own_portal(string employeeNumber, string path)
    {
        var client = await api.ClientForAsync(employeeNumber);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
