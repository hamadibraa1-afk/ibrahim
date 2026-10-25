using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Intake;

/// <summary>
/// The treating therapist's work on one clinical file (S02): statements dismissed (with the reason kept in the
/// audit log), edits saved with the approval, and the 1–5 usefulness rating. Edited text and the rating comment are
/// encrypted with the patient's key; statement keys are plain (they name a section, never patient content).
/// </summary>
public sealed class FileReview : Entity
{
    public const int MaxCommentLength = 500;

    private FileReview(Guid id, Guid patientId, Guid triageCaseId, DateTimeOffset now)
        : base(id)
    {
        PatientId = patientId;
        TriageCaseId = triageCaseId;
        UpdatedAtUtc = now;
    }

    // EF Core
    private FileReview()
    {
    }

    public Guid PatientId { get; private set; }

    public Guid TriageCaseId { get; private set; }

    /// <summary>Encrypted JSON: statement key → the therapist's wording, saved when the file is approved.</summary>
    public byte[]? EditsCipher { get; private set; }

    /// <summary>Comma-separated statement keys hidden from the file.</summary>
    public string DismissedKeys { get; private set; } = string.Empty;

    public byte? Rating { get; private set; }

    public byte[]? RatingCommentCipher { get; private set; }

    public Guid? RatedBy { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyList<string> Dismissed => DismissedKeys.Split(',', StringSplitOptions.RemoveEmptyEntries);

    public static FileReview Start(Guid patientId, Guid triageCaseId, IClock clock) => new(Guid.NewGuid(), patientId, triageCaseId, clock.UtcNow);

    public void Dismiss(string statementKey, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementKey);
        if (!Dismissed.Contains(statementKey, StringComparer.Ordinal))
        {
            DismissedKeys = string.Join(',', Dismissed.Append(statementKey));
        }

        UpdatedAtUtc = clock.UtcNow;
    }

    public void SaveEdits(byte[]? editsCipher, IClock clock)
    {
        EditsCipher = editsCipher;
        UpdatedAtUtc = clock.UtcNow;
    }

    public void Rate(Guid therapistUserId, int rating, byte[]? commentCipher, IClock clock)
    {
        if (rating is < 1 or > 5)
        {
            throw new DomainException(ErrorCodes.Validation, "Rating must be between 1 and 5.");
        }

        Rating = (byte)rating;
        RatingCommentCipher = commentCipher;
        RatedBy = therapistUserId;
        UpdatedAtUtc = clock.UtcNow;
    }
}
