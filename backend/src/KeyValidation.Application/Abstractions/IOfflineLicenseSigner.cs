using KeyValidation.Domain;

namespace KeyValidation.Application;
public interface IOfflineLicenseSigner
{
    OfflineConfigurationDto Configuration();
    (string Content, string KeyId) Sign(OfflineLicensePayload payload);
    OfflineLicensePayload Verify(string content);
}
