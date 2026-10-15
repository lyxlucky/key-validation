# 离线验证

操作 ID：offline-verify。POST /api/client/offline-licenses/verify。匿名；Content-Type: application/json。

请求：licenseContent（完整签名授权文件）、appCode、deviceId（与签发时相同的稳定原始标识）。
200 返回 serial、applicationCode、licenseId、bindingId、features、issuedAt、notBefore、expiresAt、serverTime、status、limitations。此 HTTP 接口需要联网，验证签名并检查当前授权、应用、绑定和撤销状态。

错误包括 OFFLINE_LICENSE_INVALID、OFFLINE_APPLICATION_MISMATCH、OFFLINE_DEVICE_MISMATCH、OFFLINE_LICENSE_NOT_YET_VALID、OFFLINE_LICENSE_EXPIRED、OFFLINE_LICENSE_REVOKED、OFFLINE_LICENSE_SUPERSEDED、BINDING_RELEASED，以及应用和授权状态拒绝。Offline 功能关闭或缺少可信密钥时不可验证；明确拒绝不得继续受保护操作。

完全离线模式参考仓库 samples/KeyValidation.Client/README.md 及现有 C# 验证器。文件为 ES256 compact JWS，header 包含 alg=ES256、typ=KG-OFFLINE、kid；签名采用 IEEE P1363。可信公钥由独立可信渠道配置为 kid 到 SPKI PEM 的映射；不要信任授权文件中嵌入的密钥。

本地验证须检查签名、header、formatVersion、应用与设备身份、有效时间及所需 features，每次受保护操作再次检查。deviceHash 为 UTF-8 normalizedAppCode + 换行 + rawDeviceId 的 SHA-256。使用单调进程时钟及跨重启持久化可信 UTC 下限，超过五分钟的时钟回拨或状态写入失败拒绝授权。

断网设备不能立即知道撤销、替换、冻结、禁用或解绑，最长剩余访问等于文件剩余有效期。没有可信硬件或联网时间时，本地快照回滚与程序篡改不在保护保证范围内。首版三种语言模板覆盖在线流程；完整离线实现参考现有 C# 示例。

