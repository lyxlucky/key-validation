using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public interface IOrderDeliveryProtection
{
    string Protect(Guid orderId, IReadOnlyList<IssuedKey> keys);
    IReadOnlyList<IssuedKey> Unprotect(Guid orderId, string ciphertext);
}
