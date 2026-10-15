namespace KeyValidation.Application;
public interface ISecretService
{
    GeneratedKey GenerateLicense();
    (string Digest, string Version) DigestLicense(string key);
    string DigestDevice(Guid applicationId, string deviceId);
    (string Token, string Digest) GenerateSession();
    string DigestToken(string token);
}
