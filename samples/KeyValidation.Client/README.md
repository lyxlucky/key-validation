# C# 在线与离线客户端

此目录提供可复用的 `LicenseApi`、`LeaseTracker`、`LicenseCapabilities`、`OfflineLicenseVerifier`、`OfflineLicenseSession` 和 `FileTrustedTimeStore`，并附控制台入口。Windows 桌面演示将在全部 roadmap 规格实现、审查和验证之后另行制作。

## 在线运行

```powershell
$env:KEYVALIDATION_API_URL = 'http://127.0.0.1:5080'
$env:KEYVALIDATION_APP_CODE = '<应用标识>'
$env:KEYVALIDATION_LICENSE_KEY = '<卡密>'
$env:KEYVALIDATION_DEVICE_ID = '<与激活绑定完全相同的持久设备标识>'
$env:KEYVALIDATION_CLIENT_VERSION = '1.0.0'
$env:KEYVALIDATION_REQUIRED_FEATURE = 'export.pdf'
dotnet run --project samples/KeyValidation.Client
```

远程 API 地址必须使用 HTTPS。控制台默认版本为 `1.0.0`；每段只接受 0–9999 的 `major.minor.patch`，不接受前缀、前导零或预发行版本。试用可设置 `KEYVALIDATION_TRIAL=true`，此时无需卡密。正式激活与试用会发送版本，心跳发送 `X-Client-Version`。旧集成继续使用 `new LicenseApi(http)` 时不要求配置版本，未启用强制版本策略的服务继续接收旧客户端。

服务会在每次激活和心跳检查当前应用策略。`CLIENT_VERSION_REQUIRED`、`CLIENT_UPGRADE_REQUIRED` 均返回 HTTP 426；畸形版本返回 400 `CLIENT_VERSION_INVALID`。成功响应的 `Version` 和 `LicenseRejectedException.Version` 包含最低/最新版本、更新地址、发布说明和 SHA-256。示例显示更新提示，不下载或执行安装包；用户安装前须验证发布方签名及已提供的 SHA-256。提示不会延长现有会话、租约或卡密有效期。

在线受保护操作前调用 `capabilities.Allows("export.pdf")`。它同时检查功能和当前单调计时租约。缺少功能快照不会自动授予权限。

## 离线运行

管理员在离线授权页为正式、已激活的有效授权及其未释放绑定签发许可证，并分别下载许可证文件和受信公钥文件。签发时输入的原始设备标识必须与激活时完全一致。默认有效期 24 小时，可选 1–168 小时，并截断到卡密到期时间。只从已验证来源向设备分发公钥；替换许可证文件不应同时替换受信公钥配置。

```powershell
$env:KEYVALIDATION_OFFLINE_FILE = 'C:\KeyGate\authorization.kglicense'
$env:KEYVALIDATION_OFFLINE_KEYS_FILE = 'C:\KeyGate\trusted-public-keys.json'
$env:KEYVALIDATION_OFFLINE_STATE_FILE = 'C:\KeyGate\state\trusted-time.json'
$env:KEYVALIDATION_APP_CODE = '<应用标识>'
$env:KEYVALIDATION_DEVICE_ID = '<与激活绑定完全相同的持久设备标识>'
$env:KEYVALIDATION_REQUIRED_FEATURE = 'export.pdf'
dotnet run --project samples/KeyValidation.Client
```

`KEYVALIDATION_OFFLINE_FILE` 只要存在就选择离线分支，在读取任何在线设置或构造 HTTP 客户端之前生效。此模式完全不联网；文件缺失、密钥配置错误、时钟问题或持久化失败都会退出，绝不回退到在线激活。可显式设置 `KEYVALIDATION_OFFLINE_ENABLED=false` 禁用该模式。缺少必要环境变量退出码为 1；授权拒绝或到期退出码为 2。Ctrl+C 正常退出。

公钥文件是管理页面导出的平铺 JSON 字典，键名为 `kid`，值为 SPKI PEM：

```json
{
  "local-v1": "-----BEGIN PUBLIC KEY-----\n<SPKI base64>\n-----END PUBLIC KEY-----"
}
```

用 `TrustedOfflineKeys.Load(path)` 加载它，再构造 `OfflineLicenseVerifier(keys, new FileTrustedTimeStore(statePath))`。调用 `Open(content, applicationCode, deviceId, requiredFeatures)`，得到的 `OfflineLicenseSession` 必须在每个受保护操作前调用 `Demand("export.pdf")`。返回的功能数组是副本，不能替代即时授权检查。应用标识会 trim 并转小写；设备标识保留原始字节，包含空格。离线 `deviceHash` 是 `SHA256(normalizedAppCode + "\n" + rawDeviceId)` 的大写十六进制；它与服务端原有应用 GUID 范围内的绑定摘要不同。

验证器固定要求 ES256、P-256/SHA-256、64 字节 IEEE P1363 签名、`typ=KG-OFFLINE`、已配置 `kid`、格式版本 1、完整且唯一的载荷字段、正确应用/设备、有效起止时间及功能。它不会读取或信任许可证内的公钥，也不接受私钥配置。

进程内使用单调时间及最新可信 UTC 的较大值。每次检查在授权前持久化最新 UTC，包括已经观察到的到期时间；跨重启拒绝超过五分钟的回拨，容差内也沿用保存的较晚时间。状态文件通过同目录临时文件、磁盘 flush 和原子替换写入，并以独占文件锁串行更新。状态损坏、无法读取或保存都不授予离线能力；运行中持久化失败会终止该授权对象，需要修复配置后重新打开许可证。

离线设备无法即时获知后续撤销、冻结、禁用或解绑；最长延迟为许可证剩余有效期。重新签发同绑定会替代旧记录，在线 `POST /api/client/offline-licenses/verify` 立即拒绝撤销/替代记录及失效的应用、授权或绑定，但本地离线验证器不调用该接口。没有可信硬件或网络时间时，软件状态文件无法保证抵抗整盘快照恢复、状态文件删除/修改、设备标识复制或本地程序补丁；文件权限仅减少误操作，不构成硬件安全边界。

## 服务端签名配置

```text
Offline:Enabled = true                      # 缺省 true；false 显式禁用
Offline:CurrentKeyId = local-v1
Offline:Keys:local-v1 = <PKCS8 P-256 私钥 PEM>
Offline:Keys:old-v1 = <仍受信的旧 SPKI P-256 公钥 PEM>
```

环境变量使用双下划线，例如 `Offline__CurrentKeyId`。密钥由运维提供；服务不会自动创建生产签名秘密。当前 key 必须有私钥；旧 key 可以只保留公钥。私钥只部署到服务端。`/api/admin/offline-licenses/configuration` 仅导出公钥、状态和限值。缺少配置返回 `OFFLINE_UNCONFIGURED`，禁用返回 `OFFLINE_DISABLED`，密钥/曲线配置不正确返回 `OFFLINE_KEY_CONFIGURATION_INVALID`；签发操作为 HTTP 503。既有加密备份包含全部 `Offline:*` 与 `Licensing:*` 配置，恢复后应验证签名能力，再切换服务。

轮换时先通过可信渠道向客户端发布包含新旧公钥的配置，再将服务端当前 `kid` 指向新私钥。已有离线许可证只在其 `kid` 仍被客户端信任时有效；从服务端移除旧公钥并不能远程修改离线设备已保存的信任列表。

应用更新链接限 HTTPS、最多 2048 字符，说明最多 4000 字符，摘要为 64 位十六进制。仅在 Development/Testing 环境且显式配置 `ClientVersions:AllowLoopbackHttp=true` 时允许 HTTP loopback 更新链接；生产环境不接受此例外。

```powershell
dotnet test samples/KeyValidation.Client.Tests -c Release --no-restore
```
