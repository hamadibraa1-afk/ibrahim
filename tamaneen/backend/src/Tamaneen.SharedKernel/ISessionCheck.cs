namespace Tamaneen.SharedKernel;

/// <summary>Answers whether a sign-in session (the "sid" claim of an access token) is still active.</summary>
public interface ISessionCheck
{
    Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken);

    void Forget(Guid sessionId);
}
