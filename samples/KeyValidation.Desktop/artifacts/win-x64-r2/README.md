# Windows 授权测试台

.NET 10 WinForms 中文手动测试程序，复用 `../KeyValidation.Client`。测试覆盖在线租约、试用、功能、版本、退出与设备释放，以及离线签名和可信时间。配置载入后不会自动激活。

## 构建与运行

需要 Windows x64、.NET 10 SDK 和 Windows Desktop Runtime。仓库根目录执行：

```powershell
dotnet restore samples/KeyValidation.Desktop.Tests/KeyValidation.Desktop.Tests.csproj -p:RestorePackagesPath=D:/VSCODEWORKSPACE/key-validation/.local/nuget-packages -p:NuGetAudit=false
dotnet test samples/KeyValidation.Desktop.Tests/KeyValidation.Desktop.Tests.csproj -c Release --no-restore
dotnet build samples/KeyValidation.Desktop/KeyValidation.Desktop.csproj -c Release --no-restore
dotnet publish samples/KeyValidation.Desktop/KeyValidation.Desktop.csproj -c Release -r win-x64 --self-contained false -p:RestorePackagesPath=D:/VSCODEWORKSPACE/key-validation/.local/nuget-packages -p:NuGetAudit=false -o samples/KeyValidation.Desktop/artifacts/win-x64-r2
```

可运行程序：`samples/KeyValidation.Desktop/artifacts/win-x64-r2/KeyValidation.Desktop.exe`。保留同目录 DLL、运行配置和帮助文件。发布采用框架依赖方式，不会自动安装运行时。

```powershell
& 'samples/KeyValidation.Desktop/artifacts/win-x64-r2/KeyValidation.Desktop.exe'
& 'samples/KeyValidation.Desktop/artifacts/win-x64-r2/KeyValidation.Desktop.exe' --profile 'C:/private/demo-profile.json'
```

程序默认设备标识在 `%LOCALAPPDATA%/KeyValidation.Desktop/device.json` 中稳定保存，可信 UTC 下限独立保存在 `trusted-time.json`。只自动保存设备标识；卡密、令牌和当前配置不自动回写。`--state-directory <路径>` 可选择独立测试状态目录，不会重置已有损坏状态。

## 配置与按钮

复制 `demo-profile.example.json`，通过“载入测试配置”显式选择。JSON 字段为：`schemaVersion:1`、`mode:"online"|"offline"`、`apiUrl`、`appCode`、`clientVersion`、`deviceId`、`deviceLabel`；可选 `licenseKey`、`offlineLicensePath`、`trustedKeysPath`。路径可相对于配置文件；真实资料应只存放在私人目录，示例中只保留占位值。

版本可留空以测试旧客户端；填写时必须为无前导零的三段数字，每段 0–9999。远程 API 只接受 HTTPS，本机回环可用 HTTP；地址不能带凭据、路径、查询或片段。配置授权期间锁定；“清除本地授权”、模式切换或载入新配置会取消旧请求，迟到响应不会恢复权限。

“基础操作”真正检查当前授权后，对演示数据 `12、14、16` 求和并显示实际结果 `42`；“导出演示 CSV”另外要求 `export` 功能，在保存对话框关闭后、写文件之前再次检查。按钮启用状态只作提示。自动心跳采用服务端间隔，和手动心跳共用串行边界；请求最多等待 15 秒。暂停后原租约继续倒计时，网络失败、429 和 5xx 不续租。倒计时位于底部状态行，阅读授权与升级说明时不会逐秒重置滚动位置。

“退出会话”先结束本地权限，只在服务端返回 204 后显示远程退出成功。“释放当前设备”也先结束本地权限；冷却、额度或不确定网络结果会明确显示，绝不当成成功。清除本地授权不等于远程退出；可在后台核查残留会话或等待服务端租约到期。

## 后台准备与手动用例

使用后台已有管理员权限准备独立测试数据，不重置用户或 MFA。菜单名称可能因当前账号权限隐藏。每个场景使用自己的应用、卡种、卡密和设备标识。

1. **正式授权与功能**：应用管理中创建应用；在应用详情“卡种”创建包含 `export` 和不包含它的两个卡种；卡密管理中分别生成卡密。载入配置，点“卡密激活”。两者基础操作均成功，只有有 `export` 的卡种可导出 CSV。状态显示授权 ID、期限、功能与租约。
2. **试用升级**：应用详情“试用策略”开启试用，功能留空。用从未绑定正式授权的设备点“领取试用”；期限不因重复领取而增长，CSV 不可用。输入正式卡密并点“卡密激活”，同设备成为正式授权并取得卡种功能；旧试用不能重领。
3. **禁用、冻结和撤销**：在卡密管理详情禁用或冻结已激活卡密，然后点“立即心跳”。显示明确原因并立即停用基础与导出。启用/解冻后重新激活；旧会话不会自行恢复。后台结束当前会话后，心跳显示撤销。
4. **版本门禁**：应用详情“版本策略”设置最低 `2.1.0`、最新 `2.10.0` 并强制最低版本。填写 `2.0.9` 激活应收到 426、无权限且显示升级信息。`2.1.0` 可用。留空测试 `CLIENT_VERSION_REQUIRED`；关闭强制后可验证旧客户端兼容。升级信息仅显示，不自动下载或执行程序。
5. **设备与并发**：设置卡种设备数、并发数，用另一独立状态目录或设备标识激活同卡。达到设备限制显示 `DEVICE_LIMIT_REACHED`；设备尚有额度但在线并发满时显示 `SESSION_LIMIT_REACHED`。失败不会取得本地权限。
6. **释放与换绑**：卡密详情“设备 / 换绑策略”设置额度与冷却。当前设备点“释放当前设备”，确认 204 后重新激活新设备。冷却内或额度耗尽再次释放，分别显示 `REBIND_COOLDOWN` / `REBIND_LIMIT_REACHED`；本地授权已结束，后台绑定不得被误认为已释放。
7. **暂停心跳与精确到期**：激活后关闭“自动心跳”，等待倒计时归零。基础与 CSV 均停止。也可先打开 CSV 保存对话框，等待租约到期再选择路径；程序拒绝写入。临时停止网络不延长租约。恢复网络后需在租约内成功心跳，已到期则重新激活。
8. **离线验证与功能**：先在后台对正式授权的活动绑定签发 `.kglicense`，并从独立可信渠道导出平面 `kid → SPKI PEM` 公钥 JSON。切换离线模式，分别选择两文件后验证。应用与原始设备标识必须一致；基础与 `export` 分别检查。后台禁用/撤销不会即时传播到离线设备，可知延迟最长为文件剩余期限（服务器最多签发 168 小时，且不超过卡密期限）。
9. **离线错误**：对副本改一个签名字节，或使用错设备、错应用、错公钥，应验证失败且无网络回退。过期文件无法使用。持久时间文件不可读写、损坏或检测到回拨均拒绝，不自动修复。回拨测试使用自动化中的注入时钟和独立存储，勿修改系统时钟或删除交互用户可信时间文件。
10. **生命周期与诊断**：请求中点清除、换模式或关闭；旧响应不能恢复授权。打开“诊断与帮助”，清除或导出最多 200 条元数据；没有正文、卡密、令牌、认证头、原始设备 ID 或完整许可证。用 Tab 检查原生焦点顺序，在 1024×768、1440×900 和系统缩放下检查主要按钮与滚动布局。

## 可重复真实 GUI 冒烟

仅在控制器已准备全新、独立的真实 API 测试资料后运行。试用升级和设备释放会消耗这些专用资料；重跑使用新资料。程序参数只接受路径，不接受卡密或令牌值：

```powershell
& 'samples/KeyValidation.Desktop/artifacts/win-x64-r2/KeyValidation.Desktop.exe' --smoke-manifest 'C:/private/fixture-manifest.json' --smoke-output 'C:/test-evidence/fresh-run' --state-directory 'C:/private/isolated-smoke-state'
```

清单包含 `profiles` 对象，映射 `online`、`basic`、`trial`、`outdated`、`offline` 到绝对配置路径；可附带安全应用/授权 ID 和离线到期时间。在线、基础、试用资料使用不同卡密/设备，在线与离线卡种授予 `export`，试用不授予。版本策略为最低 `2.1.0` / 最新 `2.10.0`；`outdated` 发送 `2.0.9`。离线资料必须为真实服务器签发文件和独立公钥。

入口运行同一个 STA WinForms 消息循环和实际按钮事件，测试窗口透明且不进入任务栏；文件选择器只在冒烟入口由明确路径队列替代，文件确实从磁盘读取，CSV 确实写出。会等待一次服务端自动心跳间隔，通常约 60 秒；整体建议允许 180 秒。测试完成或失败后关闭窗体并退出。前置资料缺失会失败，不跳过。

输出 `smoke-results.json`、`smoke-progress.log`、`transport-evidence.csv`、安全诊断、演示 CSV 和两种尺寸的遮蔽截图。真实 API/窗体证据与 xUnit 的受控时钟/传输证据分开记录。该程序不运行管理员 API，不创建测试卡，不重启服务。接入协议见发布目录 `help/api-contract.md`。

这是手动接入示例，离线防回拨能力依赖本机可信状态不被具有本机文件权限的人删除或替换，不提供硬件级防篡改保障。
