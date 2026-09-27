using System.Text;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using Xunit;

namespace FieldAttendance.Domain.Tests;

public class LeaveAttachmentTests
{
    private static readonly Guid Leave = Guid.NewGuid(), Employee = Guid.NewGuid();
    internal static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF");
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16];

    [Fact]
    public void The_type_comes_from_the_file_itself()
    {
        Assert.Equal("application/pdf", new LeaveAttachment(Leave, "report.pdf", Pdf, Employee).ContentType);
        Assert.Equal("image/png", new LeaveAttachment(Leave, "scan.png", Png, Employee).ContentType);
        Assert.Equal("image/jpeg", new LeaveAttachment(Leave, "photo.jpg", Jpeg, Employee).ContentType);
    }

    [Fact]
    public void A_renamed_file_of_another_kind_is_refused()
    {
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        var ex = Assert.Throws<DomainException>(() => new LeaveAttachment(Leave, "report.pdf", html, Employee));
        Assert.Equal("attachment.type", ex.Code);
    }

    [Fact]
    public void Empty_and_oversized_files_are_refused()
    {
        Assert.Throws<DomainException>(() => new LeaveAttachment(Leave, "a.pdf", [], Employee));
        var big = new byte[LeaveAttachment.MaxBytes + 1];
        Pdf.CopyTo(big, 0);
        Assert.Equal("attachment.too_large", Assert.Throws<DomainException>(() => new LeaveAttachment(Leave, "a.pdf", big, Employee)).Code);
    }

    [Theory]
    [InlineData("C:\\Users\\me\\تقرير طبي.pdf", "تقرير طبي.pdf")]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("a\"; filename=x.pdf", "a filename=x.pdf")]
    [InlineData("", "document")]
    public void The_name_keeps_no_path_or_header_tricks(string given, string kept) =>
        Assert.Equal(kept, new LeaveAttachment(Leave, given, Pdf, Employee).FileName);
}
