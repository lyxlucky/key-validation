# 设备解绑

操作 ID：device-release。POST /api/client/device-bindings/release。无请求体。
请求头 Authorization: Bearer <sessionToken>。

成功为 204，无响应正文。释放当前会话所属设备绑定并撤销该绑定会话。请求需要仍有效的会话及可用授权。成功后立即丢弃令牌并停止受保护操作；再次使用旧令牌不能证明设备仍有效。

解绑受授权设备策略的次数与冷却期限制；策略关闭时这些限制不生效。拒绝时按错误 code 处理，不盲目重试。可能出现 SESSION_INVALID（401）、SESSION_REVOKED、SESSION_EXPIRED、BINDING_RELEASED、APPLICATION_DISABLED、LICENSE_DISABLED、LICENSE_EXPIRED、LICENSE_FROZEN，以及 REBIND_LIMIT_REACHED、REBIND_COOLDOWN（409）。429 读取 Retry-After。注销只结束会话，不释放绑定。

