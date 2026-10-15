# 快速开始

资料版本：2026-10-11。API 地址由开发者在本地配置；以下路径已包含 /api。生产环境使用 HTTPS，JSON 字段为 camelCase，时间为 ISO-8601 UTC。

1. 保存一个随机生成、稳定的 deviceId（1–256 字符）。不要每次启动重新生成。
2. 使用 appCode、licenseKey 和 deviceId 调用 POST /api/client/licenses/activate，或通过 POST /api/client/trials/activate 申请试用。
3. sessionToken 只保存在进程内存。按 heartbeatIntervalSeconds 调度心跳。
4. 每次受保护操作重新检查租约和 features。缺少所需功能时拒绝该操作。
5. 退出时尽力调用 POST /api/client/sessions/logout；不要让清理失败阻塞进程退出。

有效期同时受 expiresAt、leaseExpiresAt、absoluteExpiresAt 约束。用响应 serverTime 和最早截止时间计算剩余时长，再使用单调时钟扣减；系统时钟回拨、休眠恢复都不能延长授权。明确拒绝或期限到达立即停止受保护功能。网络失败只能在上次有效租约内有界重试。

客户端无需后台 Cookie、CSRF 或商家 API 凭证。不要把商家凭证嵌入软件。不要记录卡密、会话令牌或签名授权内容。公开资料接口只提供固定文档，不读取真实应用、客户、卡密或订单。

