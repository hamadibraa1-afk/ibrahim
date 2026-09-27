using FieldAttendance.Domain.Common;

namespace FieldAttendance.Domain.Entities;

/// <summary>
/// A document supporting a leave request, such as a medical report. Stored with the request so it
/// travels with it through the approval chain and stays with it afterwards.
/// </summary>
public sealed class LeaveAttachment : Entity
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerLeave = 5;

    private LeaveAttachment() { } // EF Core

    public LeaveAttachment(Guid leaveRequestId, string fileName, byte[] content, Guid uploadedBy)
    {
        ArgumentNullException.ThrowIfNull(content);
        LeaveRequestId = Guard.NotEmpty(leaveRequestId, "attachment.leave");
        UploadedBy = Guard.NotEmpty(uploadedBy, "attachment.uploaded_by");
        if (content.Length == 0)
            throw new DomainException("attachment.empty", "The file is empty.");
        if (content.Length > MaxBytes)
            throw new DomainException("attachment.too_large", "The file is larger than 5 MB.");

        // The type comes from the file's own bytes, never from its name or the browser's claim,
        // so a renamed script cannot be stored and later served as a document.
        ContentType = AttachmentFormat.Detect(content)
            ?? throw new DomainException("attachment.type", "Only PDF, JPG and PNG files can be attached.");
        FileName = Clean(fileName);
        Size = content.Length;
        Content = content;
    }

    public Guid LeaveRequestId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public int Size { get; private set; }
    public byte[] Content { get; private set; } = [];
    public Guid UploadedBy { get; private set; }

    /// <summary>Keeps only the last path segment and printable characters, so a name cannot carry a path or header tricks.</summary>
    private static string Clean(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c) && c is not '"' and not ';').ToArray()).Trim();
        if (name.Length == 0) name = "document";
        return name.Length > 200 ? name[^200..] : name;
    }
}

/// <summary>Recognises the allowed document formats from their leading bytes.</summary>
public static class AttachmentFormat
{
    public static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("%PDF-"u8)) return "application/pdf";
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        return null;
    }
}
