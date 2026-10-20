using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Clinical;
using Tamaneen.Crisis;
using Tamaneen.Identity;
using Tamaneen.SharedKernel;

namespace Tamaneen.IntegrationTests;

[Collection(SqlServerGroup.Name)]
public sealed class RowLevelSecurityTests
{
    private readonly SqlServerFixture _sql;

    public RowLevelSecurityTests(SqlServerFixture sql) => _sql = sql;

    [Fact]
    public async Task Patient_sees_only_their_own_profile()
    {
        var noura = await _sql.CreatePatientAsync("نورة");
        var fahad = await _sql.CreatePatientAsync("فهد");

        await using var db = _sql.Context(StaticCurrentUser.ForPatient(noura.Id));
        var visible = await db.PatientProfiles.Select(p => p.PatientId).ToListAsync();
        visible.Should().ContainSingle().Which.Should().Be(noura.Id);
        (await db.PatientProfiles.AnyAsync(p => p.PatientId == fahad.Id)).Should().BeFalse();
        (await db.PatientKeys.Select(k => k.PatientId).ToListAsync()).Should().Equal(noura.Id);
    }

    [Fact]
    public async Task Therapist_sees_assigned_patients_only_and_loses_access_when_the_assignment_ends()
    {
        var therapist = await _sql.CreateStaffAsync(AppRoles.Therapist);
        var mine = await _sql.CreatePatientAsync("هيا");
        var notMine = await _sql.CreatePatientAsync("سلمان");
        var assignment = await _sql.AssignAsync(mine.Id, therapist.Id);
        var asTherapist = StaticCurrentUser.ForStaff(therapist.Id, AppRoles.Therapist);

        await using (var db = _sql.Context(asTherapist))
        {
            var ids = await db.PatientProfiles.Select(p => p.PatientId).ToListAsync();
            ids.Should().Contain(mine.Id).And.NotContain(notMine.Id);
            (await db.CareAssignments.Select(a => a.TherapistUserId).Distinct().ToListAsync()).Should().Equal(therapist.Id);
        }

        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            var row = await db.CareAssignments.SingleAsync(a => a.Id == assignment.Id);
            row.End("Transferred to another therapist", _sql.Clock);
            await db.SaveChangesAsync();
        }

        await using (var db = _sql.Context(asTherapist))
        {
            (await db.PatientProfiles.AnyAsync(p => p.PatientId == mine.Id)).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(null)]
    public async Task Admin_and_anonymous_see_no_patient_rows(string? role)
    {
        await _sql.CreatePatientAsync("ريم");
        var user = role is null ? StaticCurrentUser.Anonymous : StaticCurrentUser.ForStaff(Guid.NewGuid(), role);
        await using var db = _sql.Context(user);
        (await db.PatientProfiles.CountAsync()).Should().Be(0);
        (await db.PatientKeys.CountAsync()).Should().Be(0);
        (await db.CrisisEvents.CountAsync()).Should().Be(0);
        (await db.CareAssignments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Supervisor_and_crisis_service_see_all_patients()
    {
        var p = await _sql.CreatePatientAsync("خالد");
        foreach (var role in new[] { AppRoles.Supervisor, AppRoles.ClinicalLead, AppRoles.CrisisService })
        {
            await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), role));
            (await db.PatientProfiles.AnyAsync(x => x.PatientId == p.Id)).Should().BeTrue(role);
        }
    }

    [Fact]
    public async Task Patient_cannot_write_rows_for_another_patient()
    {
        var noura = await _sql.CreatePatientAsync("نورة");
        var fahad = await _sql.CreatePatientAsync("فهد");
        await using var db = _sql.Context(StaticCurrentUser.ForPatient(noura.Id));
        db.CrisisEvents.Add(CrisisEvent.Open(fahad.Id, fahad.PseudonymId, RiskLevel.Critical, "ar", "v1", TimeSpan.FromMinutes(2), _sql.Clock));
        var act = () => db.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<SqlException>()
            .Which.Message.Should().Contain("block predicate");
    }

    [Fact]
    public async Task Therapist_cannot_assign_a_patient_to_themselves()
    {
        var therapist = await _sql.CreateStaffAsync(AppRoles.Therapist);
        var patient = await _sql.CreatePatientAsync("منى");
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(therapist.Id, AppRoles.Therapist));
        db.CareAssignments.Add(CareAssignment.Start(patient.Id, therapist.Id, _sql.Clock));
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Session_context_cannot_be_changed_after_the_connection_opens()
    {
        var patient = await _sql.CreatePatientAsync("سارة");
        await using var db = _sql.Context(StaticCurrentUser.ForPatient(patient.Id));
        await db.Database.OpenConnectionAsync();
        var escalate = () => db.Database.ExecuteSqlRawAsync("EXEC sp_set_session_context @key = N'role', @value = N'Supervisor';");
        (await escalate.Should().ThrowAsync<SqlException>()).Which.Message.Should().Contain("read_only");
        (await db.PatientProfiles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Pooled_connections_do_not_leak_one_callers_identity_to_the_next()
    {
        var a = await _sql.CreatePatientAsync("أ");
        var b = await _sql.CreatePatientAsync("ب");
        for (var i = 0; i < 5; i++)
        {
            await using (var db = _sql.Context(StaticCurrentUser.ForPatient(a.Id)))
            {
                (await db.PatientProfiles.Select(p => p.PatientId).SingleAsync()).Should().Be(a.Id);
            }

            await using (var db = _sql.Context(StaticCurrentUser.ForPatient(b.Id)))
            {
                (await db.PatientProfiles.Select(p => p.PatientId).SingleAsync()).Should().Be(b.Id);
            }
        }
    }
}
