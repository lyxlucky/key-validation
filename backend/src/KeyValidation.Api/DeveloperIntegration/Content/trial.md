# 试用授权

操作 ID：trial-activate。POST /api/client/trials/activate。匿名；Content-Type: application/json；无需 licenseKey。

请求：appCode（必填，1–64 字符）、deviceId（必填，1–256 字符）、deviceLabel（可选，最多 120 字符）、clientVersion（可选）。
示例：

```json
{"appCode":"my-app","deviceId":"<persisted-random-device-id>","clientVersion":"1.0.0"}
```

200 返回与卡密激活相同的会话字段：sessionToken、serverTime、expiresAt、leaseExpiresAt、absoluteExpiresAt、heartbeatIntervalSeconds、licenseId、isTrial、features、version。试用同样使用心跳和功能门禁。

试用按应用和稳定设备标识认领，重复调用不会重置试用时间。已有正式授权或已升级的试用可能返回 TRIAL_UPGRADED；试用关闭返回 TRIAL_DISABLED；已结束返回 TRIAL_EXPIRED。还可能返回 APPLICATION_DISABLED、LICENSE_INVALID、LICENSE_DISABLED、LICENSE_FROZEN 及版本错误。明确拒绝立即停止受保护功能，不通过更换 deviceId 绕过试用限制。

