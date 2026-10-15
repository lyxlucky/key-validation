using KeyValidation.Domain;

namespace KeyValidation.Application;
public sealed class ClientVersionException(string code, ClientVersionInfo version) : RuleException(code, 426)
{
    public ClientVersionInfo Version { get; } = version;
}
