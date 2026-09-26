using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// A complaint or a suggestion submitted by a customer from the site's QR page.
/// It carries a human-readable reference the customer can quote, and a small
/// workflow so supervisors can show who is handling it and how it ended.
/// </summary>
public sealed class Feedback : Entity
{
    private Feedback() { } // EF Core

    public Feedback(string reference, FeedbackKind kind, Guid locationId, Guid? employeeId, Guid? attendanceRecordId,
        string customerName, string customerPhone, string? customerEmail, string message, DateTimeOffset submittedAt)
    {
        Reference = Guard.Required(reference, "feedback.reference", 30);
        Kind = kind;
        LocationId = Guard.NotEmpty(locationId, "feedback.location");
        EmployeeId = employeeId;
        AttendanceRecordId = attendanceRecordId;
        CustomerName = Guard.Required(customerName, "feedback.name", 120);
        CustomerPhone = User.NormalizePhone(customerPhone);
        CustomerEmail = Guard.Optional(customerEmail, "feedback.email", 254)?.ToLowerInvariant();
        if (CustomerEmail is not null && !CustomerEmail.Contains('@', StringComparison.Ordinal))
            throw new DomainException("feedback.email_invalid", "Email is not valid.");
        Message = Guard.Required(message, "feedback.message", 1000);
        SubmittedAt = submittedAt;
    }

    public string Reference { get; private set; } = string.Empty;
    public FeedbackKind Kind { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public Guid? AttendanceRecordId { get; private set; }
    public string CustomerName { get; private set; } = string.Empty;
    public string CustomerPhone { get; private set; } = string.Empty;
    public string? CustomerEmail { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; private set; }

    public FeedbackStatus Status { get; private set; } = FeedbackStatus.New;
    public Guid? AssignedToId { get; private set; }
    public string? ResolutionNote { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public int? HandlingMinutes => ClosedAt is { } closed ? (int)(closed - SubmittedAt).TotalMinutes : null;

    public void Assign(Guid userId)
    {
        EnsureOpen();
        AssignedToId = Guard.NotEmpty(userId, "feedback.assignee");
        if (Status == FeedbackStatus.New) Status = FeedbackStatus.InProgress;
    }

    public void Escalate(Guid userId, string? note)
    {
        EnsureOpen();
        AssignedToId ??= userId;
        Status = FeedbackStatus.Escalated;
        ResolutionNote = Guard.Optional(note, "feedback.note", 1000) ?? ResolutionNote;
    }

    public void Close(Guid userId, string resolutionNote, DateTimeOffset at)
    {
        EnsureOpen();
        ResolutionNote = Guard.Required(resolutionNote, "feedback.note", 1000);
        AssignedToId ??= userId;
        Status = FeedbackStatus.Closed;
        ClosedAt = at;
    }

    public void Reopen()
    {
        if (Status != FeedbackStatus.Closed)
            throw new DomainException("feedback.not_closed", "Only a closed item can be reopened.");
        Status = FeedbackStatus.InProgress;
        ClosedAt = null;
    }

    /// <summary>Reference shown to the customer, e.g. SCI-2026-00042.</summary>
    public static string BuildReference(int year, int sequence) =>
        $"SCI-{year}-{sequence:D5}";

    private void EnsureOpen()
    {
        if (Status == FeedbackStatus.Closed)
            throw new DomainException("feedback.closed", "This item is already closed.");
    }
}
