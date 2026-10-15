# KeyGate Windows 授权客户端

默认界面为正常的卡密登录客户端：输入卡密、登录、查看当前授权、自动验证和退出登录。复用 KeyValidation.Client 与 SessionCoordinator，界面状态来自实际服务端响应和本地有效租约。

## 直接使用

本机交付目录：`artifacts/client-v1-standalone/`。运行其中的 `KeyValidation.Desktop.exe`，或者使用目录中的启动入口。此发布包含 Windows x64 运行时，无需另装 .NET；请保留整个目录。

服务端默认是 `http://127.0.0.1:5080`，应用标识是 `keygate-desktop-client`。后台对应应用名称为「KeyGate 授权客户端」。手动测试卡密在交付目录中的 `测试卡密.txt`，包括两张 30 天卡和两张 3 分钟卡，均从首次激活开始计时。卡密只属于这个应用。

## 状态行为

| 后台操作或客户端情况 | 客户端表现 |
| --- | --- |
| 禁用卡密 | 下一次验证显示卡密已禁用，结束登录并返回登录页 |
| 冻结卡密 | 下一次验证显示卡密已冻结，结束登录 |
| 到期 | 在确认的授权期限结束时显示卡密已到期，结束登录 |
| 续期 | 有效会话在下一次成功验证时更新时间；已到期卡密需重新登录 |
| 恢复、解冻 | 重新输入卡密登录，旧会话不会自动恢复 |
| 撤销会话 | 下一次验证显示登录已失效，返回登录页 |
| 停用应用 | 下一次验证显示应用已停用，返回登录页 |
| 断网、超时 | 提示连接异常，保留已有租约；验证有效期结束后退出 |
| 点击退出、关闭窗口 | 先结束本机授权，尝试结束服务端会话；关闭最多等候 3 秒 |

服务端当前心跳间隔为 5 秒，租约最多 180 秒。客户端使用服务端返回的间隔；后台修改不会在本地凭空即时生效。可点击「立即验证」提前检查。验证有效期和最近成功验证时间均可见。

## 配置与本地资料

`client-settings.json` 只配置应用名称、API 地址、应用标识和客户端版本。远程服务使用 HTTPS，本机回环支持 HTTP。修改配置后重启应用。

设备标识默认稳定保存在 `%LOCALAPPDATA%/KeyValidation.Desktop/device.json`。卡密登录成功后清空输入框；卡密和会话令牌不会自动写入本地配置。不同卡密在同一设备使用的是同一设备标识。

保留 `--state-directory <路径>` 用于隔离测试设备状态，以及 `--profile <路径>` 用于显式载入原有在线配置。既有高级测试台仅供 `--smoke-manifest` 自动验收使用，不是默认使用界面。

## 构建与验证

```powershell
dotnet test samples/KeyValidation.Desktop.Tests/KeyValidation.Desktop.Tests.csproj -c Release
dotnet test samples/KeyValidation.Client.Tests/KeyValidation.Client.Tests.csproj -c Release
dotnet publish samples/KeyValidation.Desktop/KeyValidation.Desktop.csproj -c Release -r win-x64 --self-contained true -o samples/KeyValidation.Desktop/artifacts/client-v1-standalone
```

桌面测试覆盖实际窗口的登录、输入清理、请求期间禁用、冻结后退出，以及租约和错误状态。真实后台联动测试为 ClientLiveTests，需要由本地验收脚本提供新建的独立应用、测试卡密和临时管理员会话；不提供这些资料时明确跳过。管理员资料不会进入客户端发布包。
