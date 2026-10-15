using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace KeyValidation.Infrastructure;
public sealed class BackupFailure(string code) : Exception(code)
{
    public string Code { get; } = code;
}
