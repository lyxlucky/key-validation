# 心跳与功能门禁

操作 ID：heartbeat。POST /api/client/sessions/heartbeat。无请求体。
请求头 Authorization: Bearer <sessionToken>；可选 X-Client-Version: 1.0.0，省略保留会话现有版本。

200 返回 serverTime、expiresAt、leaseExpiresAt、absoluteExpiresAt、heartbeatIntervalSeconds、licenseId、isTrial、features、version；不返回新的 sessionToken。按 heartbeatIntervalSeconds 调度，不写死频率。当前租约为 180 秒，绝对会话期限为 8 小时，两者均受卡密到期时间限制。

心跳到达时租约必须仍有效，恰好到期也无效。每次受保护操作检查单调计时下的剩余期限，并确认 features 含所需的精确规范标识。缺失或空 features 不授予任何命名功能。这是在线 TLS 响应，不能当作离线签名许可证。

SESSION_INVALID（401）、SESSION_REVOKED、SESSION_EXPIRED、BINDING_RELEASED、APPLICATION_DISABLED、LICENSE_DISABLED、LICENSE_EXPIRED、LICENSE_FROZEN 等明确拒绝立即关闭功能门禁。426 表示版本不符合当前策略。网络中断、429 或临时 5xx 可在上一有效租约内有限重试，遵守 Retry-After；等待不得越过最早截止时间，租约过期立即停止。

