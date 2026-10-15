namespace KeyValidation.Desktop;

public sealed record ClientStatus(string Code, string Title, string Message, bool IsLoggedIn, bool IsWarning)
{
    public static ClientStatus From(SessionSnapshot snapshot)
    {
        if (snapshot.Authorized)
        {
            if (snapshot.LastResult.Code is "NETWORK_UNAVAILABLE" or "NETWORK_TIMEOUT" or "REQUEST_CANCELLED")
                return new(snapshot.LastResult.Code, "连接异常", "正在等待重新连接。当前登录仅在已有验证有效期内保持，验证超时后将返回登录页。", true, true);
            return new("AUTHORIZED", snapshot.IsTrial ? "试用中" : "授权正常", "已登录，应用正在自动验证授权状态。", true, false);
        }
        var code = snapshot.PermissionCode is "LEASE_EXPIRED" or "LICENSE_EXPIRED" or "SESSION_EXPIRED" ? snapshot.PermissionCode : snapshot.LastResult.Code;
        var (title, message) = code switch
        {
            "AUTHORIZATION_REQUIRED" or "CONFIGURATION_LOADED" or "LOCAL_CLEARED" => ("请登录", "输入该应用的卡密，验证成功后即可进入。"),
            "LICENSE_DISABLED" => ("卡密已禁用", "管理员已禁用此卡密，当前登录已结束。恢复卡密后可重新登录。"),
            "LICENSE_FROZEN" => ("卡密已冻结", "管理员已冻结此卡密，当前登录已结束。解冻后可重新登录。"),
            "LICENSE_EXPIRED" or "TRIAL_EXPIRED" => ("卡密已到期", "授权已到期，当前登录已结束。续期后可重新登录。"),
            "SESSION_REVOKED" or "SESSION_EXPIRED" or "SESSION_INVALID" => ("登录已失效", "当前会话已结束，请重新输入卡密登录。"),
            "BINDING_RELEASED" => ("设备已解绑", "当前设备绑定已被释放，请重新登录。"),
            "APPLICATION_DISABLED" => ("应用已停用", "管理员已停用该应用，当前登录已结束。恢复应用后可重新登录。"),
            "LEASE_EXPIRED" => ("验证已超时", "未能在验证有效期内确认授权，当前登录已结束。请检查连接后重新登录。"),
            "LOGOUT_CONFIRMED" or "LOGOUT_LOCAL_ONLY" => ("已退出登录", "当前登录已结束，可重新输入卡密登录。"),
            "LOGOUT_UNCONFIRMED" => ("已退出登录", "本机登录已结束。服务端退出尚未确认，原会话将按有效期结束。"),
            "NETWORK_UNAVAILABLE" or "NETWORK_TIMEOUT" => ("连接失败", "无法连接授权服务，请检查网络或确认服务已启动，然后重试。"),
            "LICENSE_KEY_REQUIRED" => ("请输入卡密", "填写完整卡密后再登录。"),
            "LICENSE_INVALID" => ("卡密无效", "请检查卡密是否完整，并确认它属于当前应用。"),
            "CLIENT_UPGRADE_REQUIRED" or "CLIENT_VERSION_REQUIRED" => ("需要更新应用", "客户端版本不符合要求，请更新后登录。"),
            "DEVICE_LIMIT_REACHED" => ("设备数量已达上限", "请在后台检查已有设备绑定后重试。"),
            "SESSION_LIMIT_REACHED" => ("在线数量已达上限", "请先退出其他设备，或在后台结束旧会话。"),
            _ => ("登录未完成", ErrorCatalog.Describe(code))
        };
        return new(code, title, message, false, code is "NETWORK_UNAVAILABLE" or "NETWORK_TIMEOUT" or "LEASE_EXPIRED");
    }
}
