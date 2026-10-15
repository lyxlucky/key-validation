using System.Text.Json;
using KeyValidation.Application;
using Microsoft.AspNetCore.DataProtection;

namespace KeyValidation.Infrastructure;
public sealed class OrderDeliveryProtection(IDataProtectionProvider provider) : IOrderDeliveryProtection
{
    private IDataProtector Protector(Guid id) => provider.CreateProtector("KeyValidation.OrderDelivery.v1", id.ToString("N"));
    public string Protect(Guid orderId, IReadOnlyList<IssuedKey> keys) => Protector(orderId).Protect(JsonSerializer.Serialize(keys));
    public IReadOnlyList<IssuedKey> Unprotect(Guid orderId, string ciphertext) => JsonSerializer.Deserialize<IssuedKey[]>(Protector(orderId).Unprotect(ciphertext)) ?? throw new InvalidDataException("Delivery unavailable.");
}
