using Microsoft.EntityFrameworkCore;
using Tamaneen.Infrastructure.Persistence;
using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Auth;

/// <summary>
/// Second verification for sensitive actions (S04: show a patient's contact data or the words that
/// triggered an event). The signed-in staff member re-enters a fresh authenticator code; a code is accepted
/// once only.
/// </summary>
public sealed class StepUpVerifier(TamaneenDbContext db, ServerSecrets secrets, IClock clock)
{
    /// <summary>Same purpose string the sign-in flow uses to protect authenticator secrets.</summary>
    public const string TotpPurpose = "totp";

    public async Task EnsureAsync(Guid userId, string? code, CancellationToken cancellationToken)
    {
        var credential = await db.Credentials.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken).ConfigureAwait(false);
        if (credential is not { TotpEnabled: true, TotpSecretCipher: { } cipher })
        {
            throw new DomainException(ErrorCodes.StepUpRequired, "No authenticator for step-up.");
        }

        var secret = Convert.FromBase64String(secrets.Unprotect(TotpPurpose, cipher));
        if (Totp.Verify(secret, code, clock.UtcNow) is not { } step || !credential.TryUseTotpStep(step))
        {
            throw new DomainException(ErrorCodes.StepUpRequired, "Step-up code rejected.");
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
