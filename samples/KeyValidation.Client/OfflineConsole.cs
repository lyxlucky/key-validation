namespace KeyValidation.Client;

public static class OfflineConsole
{
    public static async Task RunAsync(string licensePath, string publicKeysPath, string trustedTimePath, string applicationCode,
        string deviceId, string? requiredFeature, CancellationToken cancellationToken)
    {
        string content;
        try
        {
            if (new FileInfo(licensePath).Length > 20000) throw new OfflineLicenseException("OFFLINE_LICENSE_INVALID");
            content = (await File.ReadAllTextAsync(licensePath, cancellationToken)).Trim();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new OfflineLicenseException("OFFLINE_FILE_UNAVAILABLE"); }
        var verifier = new OfflineLicenseVerifier(TrustedOfflineKeys.Load(publicKeysPath), new FileTrustedTimeStore(trustedTimePath));
        var session = verifier.Open(content, applicationCode, deviceId, string.IsNullOrWhiteSpace(requiredFeature) ? [] : [requiredFeature]);
        Console.WriteLine($"离线授权已验证，到期时间：{session.ExpiresAt:O}。");
        Console.WriteLine("离线设备无法即时获知撤销、冻结、禁用或解绑；最长延迟为许可证剩余有效期。按 Ctrl+C 退出。");
        while (!cancellationToken.IsCancellationRequested)
        {
            // A real protected operation calls Demand immediately before use.
            session.Demand(string.IsNullOrWhiteSpace(requiredFeature) ? null : requiredFeature);
            var remaining = session.Remaining;
            await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}
