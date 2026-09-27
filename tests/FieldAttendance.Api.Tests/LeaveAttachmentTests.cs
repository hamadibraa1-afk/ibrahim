using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FieldAttendance.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FieldAttendance.Api.Tests;

/// <summary>Supporting documents on leave: attached by the employee, seen by the chain, required where the type says so.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class LeaveAttachmentTests(ApiFactory api)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");

    [Fact]
    public async Task A_sick_leave_cannot_be_approved_until_a_report_is_attached()
    {
        var (employee, number) = await OfficeEmployeeAsync(skip: 7);
        var client = await api.ClientForAsync(number);
        var leaveId = await SubmitSickLeaveAsync(client, new DateOnly(2026, 10, 12));
        var approver = await api.ClientForAsync(await FirstApproverNumberAsync(leaveId));

        var early = await approver.PostAsync($"/api/requests/leaves/{leaveId}/approve", null);
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
        Assert.Equal("leave.attachment_required", await CodeAsync(early));

        var upload = await UploadAsync(client, leaveId, Pdf, "تقرير طبي.pdf");
        Assert.True(upload.IsSuccessStatusCode, await upload.Content.ReadAsStringAsync());

        var approved = await approver.PostAsync($"/api/requests/leaves/{leaveId}/approve", null);
        Assert.True(approved.IsSuccessStatusCode, await approved.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_approver_can_open_the_document_and_an_unrelated_employee_cannot()
    {
        var (_, number) = await OfficeEmployeeAsync(skip: 8);
        var client = await api.ClientForAsync(number);
        var leaveId = await SubmitSickLeaveAsync(client, new DateOnly(2026, 10, 19));
        Assert.True((await UploadAsync(client, leaveId, Pdf, "report.pdf")).IsSuccessStatusCode);

        var approver = await api.ClientForAsync(await FirstApproverNumberAsync(leaveId));
        var list = (await approver.GetFromJsonAsync<JsonElement[]>($"/api/leaves/{leaveId}/attachments"))!;
        var doc = Assert.Single(list);
        Assert.Equal("report.pdf", doc.GetProperty("fileName").GetString());

        var content = await approver.GetAsync($"/api/leaves/{leaveId}/attachments/{doc.GetProperty("id").GetGuid()}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("application/pdf", content.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Pdf, await content.Content.ReadAsByteArrayAsync());

        var (_, stranger) = await OfficeEmployeeAsync(skip: 9);
        var refused = await (await api.ClientForAsync(stranger)).GetAsync($"/api/leaves/{leaveId}/attachments");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("request.not_found", await CodeAsync(refused));
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_or_image_is_refused_whatever_its_name()
    {
        var (_, number) = await OfficeEmployeeAsync(skip: 7);
        var client = await api.ClientForAsync(number);
        var leaveId = await SubmitSickLeaveAsync(client, new DateOnly(2026, 11, 2));

        var response = await UploadAsync(client, leaveId, Encoding.UTF8.GetBytes("<html><script>alert(1)</script>"), "report.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("attachment.type", await CodeAsync(response));
    }

    private async Task<Guid> SubmitSickLeaveAsync(HttpClient client, DateOnly from)
    {
        var sick = await api.QueryAsync(db => db.LeaveTypes.Where(t => t.NameEn == "Sick leave").Select(t => t.Id).SingleAsync());
        var response = await client.PostAsJsonAsync("/api/me/leaves", new { leaveTypeId = sick, fromDate = from, toDate = from.AddDays(1), reason = "مرض" });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid leaveId, byte[] bytes, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", name);
        return client.PostAsync($"/api/me/leaves/{leaveId}/attachments", form);
    }

    private Task<string> FirstApproverNumberAsync(Guid leaveId) =>
        api.QueryAsync(db => (
            from s in db.ApprovalSteps join u in db.Users on s.ApproverId equals u.Id
            where s.RequestId == leaveId && s.Status == ApprovalStepStatus.Pending
            orderby s.Order
            select u.EmployeeNumber).FirstAsync());

    private Task<(Guid UserId, string Number)> OfficeEmployeeAsync(int skip) =>
        api.QueryAsync(async db =>
        {
            var row = await (from p in db.EmployeeProfiles join u in db.Users on p.UserId equals u.Id
                             where u.Role == UserRole.Employee && p.Workforce == Workforce.Office
                             orderby u.EmployeeNumber descending
                             select new { u.Id, u.EmployeeNumber }).Skip(skip).FirstAsync();
            return (row.Id, row.EmployeeNumber);
        });

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
