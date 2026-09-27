using FieldAttendance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FieldAttendance.Api.Data;

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.FullName).HasMaxLength(150);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.Property(x => x.EmployeeNumber).HasMaxLength(30);
        b.Property(x => x.PhotoUrl).HasMaxLength(500);
        b.Property(x => x.PasswordHash).HasMaxLength(500);
        b.Property(x => x.PreferredLanguage).HasMaxLength(2);
        b.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
        b.HasIndex(x => x.EmployeeNumber).IsUnique();
    }
}

internal sealed class LocationConfig : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.Ignore(x => x.Point);
        b.Property(x => x.NameAr).HasMaxLength(150);
        b.Property(x => x.NameEn).HasMaxLength(150);
        b.Property(x => x.Address).HasMaxLength(300);
        b.HasIndex(x => x.QrToken).IsUnique();
        b.HasIndex(x => x.Kind);
    }
}

internal sealed class ShiftTemplateConfig : IEntityTypeConfiguration<ShiftTemplate>
{
    public void Configure(EntityTypeBuilder<ShiftTemplate> b)
    {
        b.Ignore(x => x.CrossesMidnight);
        b.Ignore(x => x.DurationMinutes);
        b.Property(x => x.NameAr).HasMaxLength(100);
        b.Property(x => x.NameEn).HasMaxLength(100);
    }
}

internal sealed class AssignmentConfig : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> b)
    {
        b.Property(x => x.Notes).HasMaxLength(500);
        b.HasIndex(x => new { x.EmployeeId, x.StartDate });
        b.HasIndex(x => x.LocationId);
    }
}

internal sealed class AssignmentOverrideConfig : IEntityTypeConfiguration<AssignmentOverride>
{
    public void Configure(EntityTypeBuilder<AssignmentOverride> b)
    {
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => new { x.EmployeeId, x.FromDate, x.ToDate });
    }
}

internal sealed class AttendanceRecordConfig : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> b)
    {
        b.Ignore(x => x.ScheduledWindow);
        b.Ignore(x => x.OpenExit);
        b.Ignore(x => x.HasCheckedIn);
        b.Ignore(x => x.IsOpen);

        // Spec 3.11: one record per employee per shift; also blocks double check-in races.
        b.HasIndex(x => new { x.EmployeeId, x.ScheduledStart }).IsUnique();
        b.HasIndex(x => new { x.ShiftDate, x.Status });
        b.HasIndex(x => new { x.LocationId, x.ShiftDate });

        b.HasMany(x => x.Exits).WithOne().HasForeignKey(e => e.AttendanceRecordId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Exits).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TemporaryExitConfig : IEntityTypeConfiguration<TemporaryExit>
{
    public void Configure(EntityTypeBuilder<TemporaryExit> b)
    {
        b.Ignore(x => x.PermissionWindow);
        b.HasIndex(x => x.PermissionRequestId).IsUnique();
    }
}

internal sealed class PermissionRequestConfig : IEntityTypeConfiguration<PermissionRequest>
{
    public void Configure(EntityTypeBuilder<PermissionRequest> b)
    {
        b.Ignore(x => x.IsSubmittedOnBehalf);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.RejectReason).HasMaxLength(500);
        b.HasIndex(x => new { x.EmployeeId, x.ShiftDate });
        b.HasIndex(x => x.Status);
    }
}

internal sealed class ExceptionRequestConfig : IEntityTypeConfiguration<AttendanceExceptionRequest>
{
    public void Configure(EntityTypeBuilder<AttendanceExceptionRequest> b)
    {
        b.Ignore(x => x.IsSubmittedOnBehalf);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.RejectReason).HasMaxLength(500);
        b.HasIndex(x => new { x.AttendanceRecordId, x.Status });
    }
}

internal sealed class LeaveTypeConfig : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> b)
    {
        b.Property(x => x.NameAr).HasMaxLength(100);
        b.Property(x => x.NameEn).HasMaxLength(100);
    }
}

internal sealed class LeaveBalanceConfig : IEntityTypeConfiguration<LeaveBalance>
{
    public void Configure(EntityTypeBuilder<LeaveBalance> b)
    {
        b.Ignore(x => x.RemainingDays);
        b.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.Year }).IsUnique();
    }
}

internal sealed class LeaveRequestConfig : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> b)
    {
        b.Ignore(x => x.IsSubmittedOnBehalf);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.RejectReason).HasMaxLength(500);
        b.HasIndex(x => new { x.EmployeeId, x.FromDate, x.ToDate });
    }
}

internal sealed class AllowanceTypeConfig : IEntityTypeConfiguration<AllowanceType>
{
    public void Configure(EntityTypeBuilder<AllowanceType> b)
    {
        b.Property(x => x.NameAr).HasMaxLength(100);
        b.Property(x => x.NameEn).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(300);
        b.Property(x => x.DailyAmount).HasPrecision(10, 2);
    }
}

internal sealed class EmployeeAllowanceConfig : IEntityTypeConfiguration<EmployeeAllowance>
{
    public void Configure(EntityTypeBuilder<EmployeeAllowance> b)
    {
        b.Ignore(x => x.Days);
        b.Property(x => x.Notes).HasMaxLength(300);
        b.HasIndex(x => new { x.EmployeeId, x.FromDate, x.ToDate });
    }
}

internal sealed class SupervisorLocationConfig : IEntityTypeConfiguration<SupervisorLocation>
{
    public void Configure(EntityTypeBuilder<SupervisorLocation> b) =>
        b.HasIndex(x => new { x.SupervisorId, x.LocationId }).IsUnique();
}

internal abstract class LookupConfig<T> : IEntityTypeConfiguration<T> where T : LookupEntity
{
    public void Configure(EntityTypeBuilder<T> b)
    {
        b.Property(x => x.NameAr).HasMaxLength(120);
        b.Property(x => x.NameEn).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(300);
        Extend(b);
    }

    protected virtual void Extend(EntityTypeBuilder<T> b) { }
}

internal sealed class JobTitleConfig : LookupConfig<JobTitle> { }

internal sealed class GradeConfig : LookupConfig<Grade> { }

internal sealed class ContractTypeConfig : LookupConfig<ContractType> { }

internal sealed class DepartmentConfig : LookupConfig<Department>
{
    protected override void Extend(EntityTypeBuilder<Department> b) => b.HasIndex(x => x.BranchLocationId);
}

internal sealed class SectionConfig : LookupConfig<Section>
{
    protected override void Extend(EntityTypeBuilder<Section> b) => b.HasIndex(x => x.DepartmentId);
}

internal sealed class HolidayConfig : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> b)
    {
        b.Ignore(x => x.Days);
        b.Property(x => x.NameAr).HasMaxLength(120);
        b.Property(x => x.NameEn).HasMaxLength(120);
        b.HasIndex(x => new { x.FromDate, x.ToDate });
    }
}

internal sealed class WorkScheduleConfig : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> b)
    {
        b.Ignore(x => x.WorkingDays);
        b.Ignore(x => x.WeeklyMinutes);
        b.Property(x => x.NameAr).HasMaxLength(120);
        b.Property(x => x.NameEn).HasMaxLength(120);
        b.HasMany(x => x.Days).WithOne().HasForeignKey(d => d.WorkScheduleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Days).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class WorkScheduleDayConfig : IEntityTypeConfiguration<WorkScheduleDay>
{
    public void Configure(EntityTypeBuilder<WorkScheduleDay> b)
    {
        b.Ignore(x => x.DurationMinutes);
        b.Ignore(x => x.WorkMinutes);
        b.HasIndex(x => new { x.WorkScheduleId, x.Day }).IsUnique();
    }
}

internal sealed class EmployeeProfileConfig : IEntityTypeConfiguration<EmployeeProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeProfile> b)
    {
        b.Ignore(x => x.IsEmployed);
        b.Property(x => x.BasicSalary).HasPrecision(12, 2);
        b.Property(x => x.Nationality).HasMaxLength(80);
        b.Property(x => x.IdNumber).HasMaxLength(40);
        b.Property(x => x.PassportNumber).HasMaxLength(40);
        b.Property(x => x.ResidencyNumber).HasMaxLength(40);
        b.Property(x => x.Iban).HasMaxLength(40);
        b.Property(x => x.EmergencyContactName).HasMaxLength(120);
        b.Property(x => x.EmergencyContactPhone).HasMaxLength(20);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.EndReason).HasMaxLength(300);
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => new { x.DepartmentId, x.Status });
        b.HasIndex(x => x.BranchLocationId);
    }
}

internal sealed class SalaryChangeConfig : IEntityTypeConfiguration<SalaryChange>
{
    public void Configure(EntityTypeBuilder<SalaryChange> b)
    {
        b.Property(x => x.OldSalary).HasPrecision(12, 2);
        b.Property(x => x.NewSalary).HasPrecision(12, 2);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom });
    }
}

internal sealed class SalaryAllowanceConfig : IEntityTypeConfiguration<SalaryAllowance>
{
    public void Configure(EntityTypeBuilder<SalaryAllowance> b)
    {
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.MonthlyAmount).HasPrecision(12, 2);
        b.HasIndex(x => new { x.EmployeeId, x.FromDate });
    }
}

internal sealed class ExtraPaymentConfig : IEntityTypeConfiguration<ExtraPayment>
{
    public void Configure(EntityTypeBuilder<ExtraPayment> b)
    {
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Ignore(x => x.FirstDay);
        b.HasIndex(x => new { x.Year, x.Month, x.EmployeeId });
    }
}

internal sealed class LeaveAttachmentConfig : IEntityTypeConfiguration<LeaveAttachment>
{
    public void Configure(EntityTypeBuilder<LeaveAttachment> b)
    {
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.Content).HasMaxLength(LeaveAttachment.MaxBytes);
        b.HasIndex(x => x.LeaveRequestId);
    }
}

internal sealed class SystemSettingConfig : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> b)
    {
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.Value).HasMaxLength(500);
        b.HasIndex(x => x.Key).IsUnique();
    }
}

internal sealed class DeductionTypeConfig : LookupConfig<DeductionType>
{
    protected override void Extend(EntityTypeBuilder<DeductionType> b)
    {
        b.Property(x => x.Amount).HasPrecision(10, 2);
        b.Property(x => x.MonthlyCapPercent).HasPrecision(5, 2);
    }
}

internal sealed class WarningLevelConfig : LookupConfig<WarningLevel>
{
    protected override void Extend(EntityTypeBuilder<WarningLevel> b)
    {
        b.Property(x => x.Template).HasMaxLength(2000);
        b.HasIndex(x => x.Order);
    }
}

internal sealed class DeductionProposalConfig : IEntityTypeConfiguration<DeductionProposal>
{
    public void Configure(EntityTypeBuilder<DeductionProposal> b)
    {
        b.Ignore(x => x.IsPending);
        b.Property(x => x.Units).HasPrecision(6, 2);
        b.Property(x => x.ApprovedAmount).HasPrecision(12, 2);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.DecisionNote).HasMaxLength(500);
        b.HasIndex(x => new { x.EmployeeId, x.OnDate });
        b.HasIndex(x => x.Status);
        // One proposal per attendance record per type: re-scanning cannot duplicate a charge.
        b.HasIndex(x => new { x.AttendanceRecordId, x.DeductionTypeId }).IsUnique()
            .HasFilter("[AttendanceRecordId] IS NOT NULL");
    }
}

internal sealed class WarningConfig : IEntityTypeConfiguration<Warning>
{
    public void Configure(EntityTypeBuilder<Warning> b)
    {
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.Objection).HasMaxLength(1000);
        b.Property(x => x.ObjectionResponse).HasMaxLength(1000);
        b.HasIndex(x => new { x.EmployeeId, x.IssuedAt });
    }
}

internal sealed class PayrollCycleConfig : IEntityTypeConfiguration<PayrollCycle>
{
    public void Configure(EntityTypeBuilder<PayrollCycle> b)
    {
        b.Ignore(x => x.FirstDay);
        b.Ignore(x => x.LastDay);
        b.Ignore(x => x.IsOpen);
        b.Property(x => x.OvertimeFactor).HasPrecision(5, 2);
        b.Property(x => x.MaxDeductionPercent).HasPrecision(5, 2);
        b.HasIndex(x => new { x.Year, x.Month }).IsUnique();
    }
}

internal sealed class PayrollLineConfig : IEntityTypeConfiguration<PayrollLine>
{
    public void Configure(EntityTypeBuilder<PayrollLine> b)
    {
        foreach (var money in new[] { "BasicSalary", "Earnings", "Deductions", "CappedDeductions", "NetPay" })
            b.Property(money).HasPrecision(12, 2);
        b.HasIndex(x => new { x.PayrollCycleId, x.EmployeeId }).IsUnique();
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.PayrollLineId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PayrollLineItemConfig : IEntityTypeConfiguration<PayrollLineItem>
{
    public void Configure(EntityTypeBuilder<PayrollLineItem> b)
    {
        b.Property(x => x.Amount).HasPrecision(12, 2);
        b.Property(x => x.Label).HasMaxLength(150);
        b.Property(x => x.SourceKey).HasMaxLength(100);
    }
}

internal sealed class ApprovalFlowConfig : IEntityTypeConfiguration<ApprovalFlow>
{
    public void Configure(EntityTypeBuilder<ApprovalFlow> b)
    {
        b.Property(x => x.NameAr).HasMaxLength(120);
        b.Property(x => x.NameEn).HasMaxLength(120);
        b.HasIndex(x => x.Kind);
        b.HasMany(x => x.Levels).WithOne().HasForeignKey(l => l.ApprovalFlowId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Levels).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ApprovalFlowLevelConfig : IEntityTypeConfiguration<ApprovalFlowLevel>
{
    public void Configure(EntityTypeBuilder<ApprovalFlowLevel> b) =>
        b.HasIndex(x => new { x.ApprovalFlowId, x.Order }).IsUnique();
}

internal sealed class ApprovalStepConfig : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> b)
    {
        b.Ignore(x => x.IsOpen);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.Kind, x.RequestId, x.Order }).IsUnique();
        b.HasIndex(x => new { x.ApproverId, x.Status });
    }
}

internal sealed class ReturnToWorkConfig : IEntityTypeConfiguration<ReturnToWork>
{
    public void Configure(EntityTypeBuilder<ReturnToWork> b)
    {
        b.Ignore(x => x.LateDays);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => x.LeaveRequestId).IsUnique();
        b.HasIndex(x => new { x.EmployeeId, x.ExpectedDate });
    }
}

internal sealed class NotificationConfig : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.Ignore(x => x.IsUnread);
        b.Property(x => x.DedupeKey).HasMaxLength(200);
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Value).HasMaxLength(100);
        b.Property(x => x.Link).HasMaxLength(300);
        // The same fact can only be stored once, whatever raises it.
        b.HasIndex(x => x.DedupeKey).IsUnique();
        b.HasIndex(x => new { x.RecipientId, x.ReadAt });
    }
}

internal sealed class NotificationSettingConfig : IEntityTypeConfiguration<NotificationSetting>
{
    public void Configure(EntityTypeBuilder<NotificationSetting> b) => b.HasIndex(x => x.Kind).IsUnique();
}

internal sealed class FeedbackConfig : IEntityTypeConfiguration<Domain.Entities.Feedback>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Feedback> b)
    {
        b.Ignore(x => x.HandlingMinutes);
        b.Property(x => x.Reference).HasMaxLength(30);
        b.Property(x => x.CustomerName).HasMaxLength(120);
        b.Property(x => x.CustomerPhone).HasMaxLength(20);
        b.Property(x => x.CustomerEmail).HasMaxLength(254);
        b.Property(x => x.Message).HasMaxLength(1000);
        b.Property(x => x.ResolutionNote).HasMaxLength(1000);
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => new { x.LocationId, x.Status });
        b.HasIndex(x => new { x.Kind, x.SubmittedAt });
    }
}

internal sealed class RatingConfig : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> b)
    {
        b.Property(x => x.Comment).HasMaxLength(500);
        b.Property(x => x.DeviceToken).HasMaxLength(100);
        b.Property(x => x.IpHash).HasMaxLength(100);
        b.HasIndex(x => new { x.LocationId, x.ScannedAt });
        b.HasIndex(x => new { x.EmployeeId, x.ScannedAt });
        b.HasIndex(x => new { x.DeviceToken, x.LocationId });
    }
}
