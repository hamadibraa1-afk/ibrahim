namespace Tamaneen.Identity;

/// <summary>Turns an email or phone number into a keyed hash used to find an account without storing the value.</summary>
public interface IContactHasher
{
    byte[] Hash(string emailOrPhone);
}
