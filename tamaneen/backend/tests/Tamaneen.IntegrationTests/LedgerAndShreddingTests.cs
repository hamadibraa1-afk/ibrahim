using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Tamaneen.Infrastructure.Auditing;
using Tamaneen.Infrastructure.Security;
using Tamaneen.SharedKernel;

namespace Tamaneen.IntegrationTests;

[Collection(SqlServerGroup.Name)]
public sealed class LedgerAndShreddingTests
{
    private readonly SqlServerFixture _sql;

    public LedgerAndShreddingTests(SqlServerFixture sql) => _sql = sql;

    [Fact]
    public async Task Audit_table_is_an_append_only_ledger()
    {
        await using var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Admin));
        new AuditLog(db, StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Admin), _sql.Clock).Append("settings.changed", "PlatformSetting", "sla.l4", reason: "تجربة");
        await db.SaveChangesAsync();

        var ledgerType = await db.Database.SqlQueryRaw<string>(
            "SELECT ledger_type_desc AS [Value] FROM sys.tables WHERE object_id = OBJECT_ID(N'audit.AuditEntries')").SingleAsync();
        ledgerType.Should().Be("APPEND_ONLY_LEDGER_TABLE");

        var update = () => db.Database.ExecuteSqlRawAsync("UPDATE audit.AuditEntries SET Reason = N'edited'");
        (await update.Should().ThrowAsync<SqlException>()).Which.Message.Should().Contain("append only Ledger table");
        var delete = () => db.Database.ExecuteSqlRawAsync("DELETE FROM audit.AuditEntries");
        (await delete.Should().ThrowAsync<SqlException>()).Which.Message.Should().Contain("append only Ledger table");
    }

    [Fact]
    public async Task Assigned_therapist_can_decrypt_the_emergency_contact()
    {
        var therapist = await _sql.CreateStaffAsync(AppRoles.Therapist);
        var patient = await _sql.CreatePatientAsync("نورة");
        await _sql.AssignAsync(patient.Id, therapist.Id);

        await using var db = _sql.Context(StaticCurrentUser.ForStaff(therapist.Id, AppRoles.Therapist));
        var profile = await db.PatientProfiles.SingleAsync(p => p.PatientId == patient.Id);
        var (_, protector) = _sql.Crypto(db);
        (await protector.DecryptAsync(patient.Id, ProtectedFields.EmergencyContact, profile.EmergencyContactCipher!, default))
            .Should().Contain("أختي").And.Contain("0551234567");
        (await protector.DecryptAsync(patient.Id, ProtectedFields.DisplayName, profile.DisplayNameCipher!, default)).Should().Be("نورة");
    }

    [Fact]
    public async Task Crypto_shredding_makes_old_ciphertext_unreadable_and_leaves_an_audit_row()
    {
        var patient = await _sql.CreatePatientAsync("فهد");
        byte[] savedCopy;
        await using (var db = _sql.Context(StaticCurrentUser.ForPatient(patient.Id)))
        {
            savedCopy = (await db.PatientProfiles.SingleAsync()).DisplayNameCipher!;
        }

        var service = StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.DataProtectionService);
        await using (var db = _sql.Context(service))
        {
            var (keys, _) = _sql.Crypto(db);
            await new CryptoShredder(db, keys, new AuditLog(db, service, _sql.Clock), _sql.Clock).ShredAsync(patient.Id, "DR-0041 approved deletion", default);
        }

        await using (var db = _sql.Context(StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor)))
        {
            var profile = await db.PatientProfiles.SingleAsync(p => p.PatientId == patient.Id);
            profile.IsShredded.Should().BeTrue();
            profile.DisplayNameCipher.Should().BeNull();
            (await db.PatientKeys.SingleAsync(k => k.PatientId == patient.Id)).WrappedKey.Should().BeNull();

            // A copy of the ciphertext taken earlier (e.g. from a backup) can no longer be decrypted.
            var (_, protector) = _sql.Crypto(db);
            var read = () => protector.DecryptAsync(patient.Id, ProtectedFields.DisplayName, savedCopy, default);
            await read.Should().ThrowAsync<PatientDataShreddedException>();

            var audit = await db.AuditEntries.Where(a => a.PatientPseudonymId == patient.PseudonymId && a.Action == "patient.crypto_shred").ToListAsync();
            audit.Should().ContainSingle().Which.Reason.Should().Be("DR-0041 approved deletion");
        }
    }

    [Fact]
    public async Task Shredding_requires_a_reason()
    {
        var patient = await _sql.CreatePatientAsync("ليلى");
        var service = StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.DataProtectionService);
        await using var db = _sql.Context(service);
        var (keys, _) = _sql.Crypto(db);
        var act = () => new CryptoShredder(db, keys, new AuditLog(db, service, _sql.Clock), _sql.Clock).ShredAsync(patient.Id, " ", default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.ReasonRequired);
    }
}

[Collection(SqlServerGroup.Name)]
public sealed class LedgerBatchTests
{
    private readonly SqlServerFixture _sql;

    public LedgerBatchTests(SqlServerFixture sql) => _sql = sql;

    [Fact]
    public async Task Many_audit_rows_in_one_save_are_accepted_by_the_ledger()
    {
        var user = StaticCurrentUser.ForStaff(Guid.NewGuid(), AppRoles.Supervisor);
        await using var db = _sql.Context(user);
        var audit = new AuditLog(db, user, _sql.Clock);
        for (var i = 0; i < 25; i++)
        {
            audit.Append("batch.test", "Test", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        await db.SaveChangesAsync();
        (await db.AuditEntries.CountAsync(a => a.Action == "batch.test")).Should().Be(25);
    }
}
