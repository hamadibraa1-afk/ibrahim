using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Security;
using ProposalSystem.Api.Workflow;

namespace ProposalSystem.Api.Controllers;

[ApiController]
[Route("api/proposals/{proposalId:int}/attachments")]
[Authorize]
public sealed class AttachmentsController(
    AppDbContext db,
    CurrentUser me,
    ProposalAccess access,
    IdentityRevealPolicy reveal,
    AuditTrail audit,
    IOptions<AttachmentOptions> options,
    IWebHostEnvironment env,
    TimeProvider clock) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private Viewer Viewer => new(me.Id, me.Role);

    [HttpGet]
    public async Task<List<AttachmentDto>> List(int proposalId, CancellationToken ct)
    {
        var p = await LoadVisible(proposalId, ct);
        return p.Attachments.OrderBy(a => a.UploadedAt).Select((a, i) => ToDto(p, a, i)).ToList();
    }

    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<List<AttachmentDto>> Upload(int proposalId, [FromForm] List<IFormFile> files, CancellationToken ct)
    {
        var p = await LoadVisible(proposalId, ct);
        if (!access.CanEdit(p, Viewer))
            throw ApiException.Forbidden("لا يمكن إضافة مرفقات للمقترح في حالته الحالية.");
        var o = options.Value;
        if (files.Count == 0)
            throw ApiException.BadRequest("لم يتم اختيار أي ملف.");
        if (p.Attachments.Count + files.Count > o.MaxFiles)
            throw ApiException.BadRequest($"الحد الأقصى {o.MaxFiles} ملفات لكل مقترح.");
        if (p.Attachments.Sum(a => a.SizeBytes) + files.Sum(f => f.Length) > o.MaxTotalBytes)
            throw ApiException.BadRequest("الحجم الإجمالي للمرفقات يتجاوز 15 ميجابايت.");

        // Validate the whole batch before touching the disk: one bad file refuses all of them,
        // and nothing is left behind.
        var accepted = new List<(IFormFile File, string Name, string Ext)>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file.FileName);
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (!o.AllowedExtensions.Contains(ext))
                throw ApiException.BadRequest($"نوع الملف \"{name}\" غير مسموح به.");
            if (file.Length == 0 || file.Length > o.MaxFileBytes)
                throw ApiException.BadRequest($"حجم الملف \"{name}\" غير مقبول (الحد 10 ميجابايت).");
            accepted.Add((file, name, ext));
        }

        var dir = StorageDirectory(env, o);
        Directory.CreateDirectory(dir);
        var now = clock.GetUtcNow().UtcDateTime;
        var written = new List<Attachment>();
        try
        {
            foreach (var (file, name, ext) in accepted)
            {
                // Random stored name: the user's file name never touches the file system.
                var stored = $"{Guid.NewGuid():N}{ext}";
                await using (var target = System.IO.File.Create(Path.Combine(dir, stored)))
                    await file.CopyToAsync(target, ct);

                var attachment = new Attachment
                {
                    FileName = name.Length <= 260 ? name : name[^260..],
                    StoredName = stored,
                    // Content type from our own extension map, never from the client.
                    ContentType = ContentTypes.TryGetContentType(name, out var type) ? type : "application/octet-stream",
                    SizeBytes = file.Length,
                    UploadedAt = now,
                    UploadedById = me.Id,
                };
                written.Add(attachment);
                p.Attachments.Add(attachment);
                audit.Record(p, p.Submitter, AuditActions.AttachmentAdded, notes: name);
            }
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed copy or save must not leave unreferenced files in App_Data.
            DeleteFiles(written, HttpContext.RequestServices);
            throw;
        }
        return p.Attachments.OrderBy(a => a.UploadedAt).Select((a, i) => ToDto(p, a, i)).ToList();
    }

    [HttpDelete("{attachmentId:int}")]
    public async Task<IActionResult> Delete(int proposalId, int attachmentId, CancellationToken ct)
    {
        var p = await LoadVisible(proposalId, ct);
        if (!access.CanEdit(p, Viewer))
            throw ApiException.Forbidden("لا يمكن حذف مرفقات المقترح في حالته الحالية.");
        var a = p.Attachments.FirstOrDefault(x => x.Id == attachmentId) ?? throw ApiException.NotFound("المرفق غير موجود.");
        p.Attachments.Remove(a);
        db.Attachments.Remove(a);
        audit.Record(p, p.Submitter, AuditActions.AttachmentRemoved, notes: a.FileName);
        await db.SaveChangesAsync(ct);
        DeleteFiles([a], HttpContext.RequestServices);
        return NoContent();
    }

    [HttpGet("{attachmentId:int}/download")]
    public Task<IActionResult> Download(int proposalId, int attachmentId, CancellationToken ct) => Serve(proposalId, attachmentId, inline: false, ct);

    [HttpGet("{attachmentId:int}/preview")]
    public Task<IActionResult> Preview(int proposalId, int attachmentId, CancellationToken ct) => Serve(proposalId, attachmentId, inline: true, ct);

    private async Task<IActionResult> Serve(int proposalId, int attachmentId, bool inline, CancellationToken ct)
    {
        var p = await LoadVisible(proposalId, ct);
        var ordered = p.Attachments.OrderBy(a => a.UploadedAt).ToList();
        var index = ordered.FindIndex(a => a.Id == attachmentId);
        if (index < 0)
            throw ApiException.NotFound("المرفق غير موجود.");
        var a = ordered[index];

        var path = Path.Combine(StorageDirectory(env, options.Value), a.StoredName);
        if (!System.IO.File.Exists(path))
            throw ApiException.NotFound("ملف المرفق غير موجود على الخادم.");

        var previewable = a.ContentType == "application/pdf" || a.ContentType.StartsWith("image/", StringComparison.Ordinal);
        if (inline && !previewable)
            throw ApiException.BadRequest("لا يمكن معاينة هذا النوع من الملفات.");

        // Opened in its own tab: sandbox it so a crafted PDF/SVG cannot script against the app.
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'";
        var downloadName = ToDto(p, a, index).FileName;
        return PhysicalFile(path, a.ContentType, inline ? null : downloadName, enableRangeProcessing: false);
    }

    /// <summary>
    /// File names often carry a person's name ("ahmed-idea.pdf"), so blind reviewers see a neutral
    /// name until the proposer is revealed.
    /// </summary>
    private AttachmentDto ToDto(Proposal p, Attachment a, int index)
    {
        var name = reveal.CanSeeIdentity(p, Viewer) ? a.FileName : $"مرفق-{index + 1}{Path.GetExtension(a.FileName)}";
        return new AttachmentDto(a.Id, name, a.ContentType, a.SizeBytes, a.UploadedAt);
    }

    private async Task<Proposal> LoadVisible(int id, CancellationToken ct)
    {
        var p = await db.Proposals.Include(x => x.Attachments).Include(x => x.Submitter).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw ApiException.NotFound("المقترح غير موجود.");
        if (!access.CanView(p, Viewer))
            throw ApiException.Forbidden("ليست لديك صلاحية لعرض هذا المقترح.");
        return p;
    }

    internal static string StorageDirectory(IWebHostEnvironment env, AttachmentOptions o) =>
        Path.IsPathRooted(o.StoragePath) ? o.StoragePath : Path.Combine(env.ContentRootPath, o.StoragePath);

    internal static void DeleteFiles(IEnumerable<Attachment> attachments, IServiceProvider services)
    {
        var env = services.GetRequiredService<IWebHostEnvironment>();
        var o = services.GetRequiredService<IOptions<AttachmentOptions>>().Value;
        foreach (var a in attachments)
        {
            var path = Path.Combine(StorageDirectory(env, o), a.StoredName);
            try { System.IO.File.Delete(path); }
            catch (IOException) { /* orphaned file; harmless */ }
        }
    }
}
