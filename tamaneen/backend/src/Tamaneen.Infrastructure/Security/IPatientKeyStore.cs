namespace Tamaneen.Infrastructure.Security;

/// <summary>Creates, loads and destroys patient data keys.</summary>
public interface IPatientKeyStore
{
    /// <summary>Returns the patient's 32-byte data key, creating and persisting one on first use.</summary>
    Task<byte[]> GetOrCreateDataKeyAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>Returns the existing data key. Throws <see cref="PatientDataShreddedException"/> after shredding.</summary>
    Task<byte[]> GetDataKeyAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>Erases the wrapped key (the caller saves the unit of work) and evicts it from memory.</summary>
    Task ShredAsync(Guid patientId, CancellationToken cancellationToken);
}
