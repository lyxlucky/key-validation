# 卡密激活

操作 ID：activate。POST /api/client/licenses/activate。匿名；Content-Type: application/json。

请求：appCode（必填，1–64 字符）、licenseKey（必填）、deviceId（必填，1–256 字符）、deviceLabel（可选，最多 120 字符）、clientVersion（可选，严格三段版本号）。
示例：

```json
{"appCode":"my-app","licenseKey":"<your-license-key>","deviceId":"<persisted-random-device-id>","deviceLabel":"工作电脑","clientVersion":"1.0.0"}
```

200 响应：sessionToken、serverTime、expiresAt、leaseExpiresAt、absoluteExpiresAt、heartbeatIntervalSeconds、licenseId、isTrial、features、version。version 为版本提示对象（见版本检查主题）。响应禁止缓存；令牌只放内存。

重复激活同一绑定会撤销旧会话，首次授权到期时间不会重新开始。设备配额与并发会话配额独立；同设备替换不额外占用在线名额。

错误包括 APPLICATION_DISABLED、LICENSE_INVALID、LICENSE_DISABLED、LICENSE_EXPIRED、LICENSE_FROZEN、DEVICE_LIMIT_REACHED、SESSION_LIMIT_REACHED、ACTIVATION_DEADLINE_PASSED。版本错误 CLIENT_VERSION_INVALID 为 400；CLIENT_VERSION_REQUIRED 或 CLIENT_UPGRADE_REQUIRED 为 426。400 校验错误检查字段；403 明确拒绝时停止受保护功能；429 读取 Retry-After 并有界重试。

