# 常见错误

失败响应为 application/problem+json，字段包括 type、title、status、code、traceId。根据 code 和 HTTP 状态处理；不要只检查是否有响应正文。

- 400 VALIDATION_ERROR：检查请求字段、长度和 JSON。CLIENT_VERSION_INVALID：使用三段数字版本。
- 401 SESSION_INVALID：令牌格式错误或不存在，停止受保护操作后重新按授权流程激活。
- 403 明确拒绝：APPLICATION_DISABLED、LICENSE_INVALID、LICENSE_DISABLED、LICENSE_EXPIRED、LICENSE_FROZEN、SESSION_REVOKED、SESSION_EXPIRED、BINDING_RELEASED 等；立即停止受保护操作。
- DEVICE_LIMIT_REACHED 与 SESSION_LIMIT_REACHED 分别表示绑定和在线额度；不要循环激活。
- TRIAL_DISABLED、TRIAL_EXPIRED、TRIAL_UPGRADED：当前试用不可用，不能通过更换设备标识重置。
- 426 CLIENT_VERSION_REQUIRED、CLIENT_UPGRADE_REQUIRED：按 version 提示升级，当前请求不能授予使用权。
- 429 RATE_LIMITED：读取 Retry-After。只能在先前有效租约内等待与有限重试。
- 413 REQUEST_TOO_LARGE：请求超过服务器大小限制，应减少负载；请求解析器拒绝超限正文时也可能返回 413 VALIDATION_ERROR，以 HTTP 状态为准。
- 暂时网络故障或 5xx：有限退避，不能跨过租约、授权或绝对会话截止时间。

日志保留 code、HTTP 状态、操作名称和 traceId 即可；不得记录卡密、会话令牌、完整签名文件或原始敏感请求。未知错误码意味着资料尚未收录，无法据此推断服务端状态；检查安全的错误字段并联系服务提供方，保持关闭功能门禁。

