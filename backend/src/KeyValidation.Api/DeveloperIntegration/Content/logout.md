# 退出会话

操作 ID：logout。POST /api/client/sessions/logout。无请求体。
请求头 Authorization: Bearer <sessionToken>。

成功为 204，无响应正文。注销撤销会话并保留设备绑定；释放设备额度请使用设备解绑接口。

正常关闭以及异常退出路径中尽力清理会话，并立即清除内存令牌、停止受保护操作。清理失败仍不能延长租约。令牌格式错误或不存在返回 SESSION_INVALID（401）；429 时遵守 Retry-After，清理采用有界等待。

