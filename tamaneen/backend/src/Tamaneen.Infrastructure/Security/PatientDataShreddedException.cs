using Tamaneen.SharedKernel;

namespace Tamaneen.Infrastructure.Security;

public sealed class PatientDataShreddedException : DomainException
{
    public PatientDataShreddedException(Guid patientId)
        : base(ErrorCodes.PatientDataShredded, $"The data key of patient {patientId} has been destroyed.")
    {
        PatientId = patientId;
    }

    public Guid PatientId { get; }
}
