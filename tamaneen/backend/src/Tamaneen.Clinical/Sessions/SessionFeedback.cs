using Tamaneen.SharedKernel;

namespace Tamaneen.Clinical.Sessions;

/// <summary>
/// The patient's private rating of one session (P43). One per appointment. It reaches supervisors only, never the
/// therapist (row-level security leaves therapists out). The optional comment is encrypted with the patient's key.
/// </summary>
public sealed class SessionFeedback : Entity
{
    public const int MaxCommentLength = 500;

    /// <summary>The three answers of «كيف كانت الجلسة بالنسبة لك؟», stored as 1 to 3.</summary>
    public static readonly IReadOnlyList<string> Ratings = ["not_helpful", "okay", "helpful"];

    private SessionFeedback(Guid id, Guid appointmentId, Guid patientId, Guid therapistUserId, byte? rating, bool comfortable, byte[]? commentCipher, DateTimeOffset now)
        : base(id)
    {
        AppointmentId = appointmentId;
        PatientId = patientId;
        TherapistUserId = therapistUserId;
        Rating = rating;
        Comfortable = comfortable;
        CommentCipher = commentCipher;
        CreatedAtUtc = now;
    }

    // EF Core
    private SessionFeedback()
    {
    }

    public Guid AppointmentId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid TherapistUserId { get; private set; }

    /// <summary>1 not helpful, 2 okay, 3 helpful; null when the patient only ticked the box or wrote a comment.</summary>
    public byte? Rating { get; private set; }

    /// <summary>«شعرت بالارتياح مع الأخصائية».</summary>
    public bool Comfortable { get; private set; }

    public byte[]? CommentCipher { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public string? RatingCode => Rating is { } r && r >= 1 && r <= Ratings.Count ? Ratings[r - 1] : null;

    public static byte? ParseRating(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        var index = Ratings.ToList().IndexOf(code);
        return index < 0 ? throw new DomainException(ErrorCodes.Validation, "Unknown rating.") : (byte)(index + 1);
    }

    public static SessionFeedback Record(Guid appointmentId, Guid patientId, Guid therapistUserId, byte? rating, bool comfortable, byte[]? commentCipher, IClock clock)
    {
        if (rating is null && !comfortable && commentCipher is null)
        {
            throw new DomainException(ErrorCodes.Validation, "Feedback is empty.");
        }

        return new SessionFeedback(Guid.NewGuid(), appointmentId, patientId, therapistUserId, rating, comfortable, commentCipher, clock.UtcNow);
    }
}
