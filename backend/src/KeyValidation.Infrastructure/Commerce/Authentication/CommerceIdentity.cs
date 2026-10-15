using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class CommerceIdentity
{
    public string ActorId { get; internal set; } = "";
    public Guid? CustomerId { get; internal set; }
    public Guid? SessionId { get; internal set; }
    public Guid? CredentialId { get; internal set; }
}
