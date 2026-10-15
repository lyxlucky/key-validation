# 版本检查

操作 ID：version。GET /api/client/applications/{appCode}/version。匿名；无请求体。

200 返回 applicationCode、minimumVersion、latestVersion、enforceMinimum、updateUrl、releaseNotes、sha256。未配置的值可为 null，enforceMinimum 默认为 false。未知应用返回 NOT_FOUND（404）；禁用应用返回 APPLICATION_DISABLED。

clientVersion 使用严格三段数字，每段 0–9999。激活及试用在请求体发送 clientVersion；心跳用 X-Client-Version 更新，不发送则保留会话版本。策略在每次激活和心跳时重新检查。

成功激活/心跳以及 426 错误的 version 对象包含 clientVersion、minimumVersion、latestVersion、enforceMinimum、upgradeAvailable、updateUrl、releaseNotes、sha256。强制最低版本时缺少版本返回 CLIENT_VERSION_REQUIRED（426），版本过低返回 CLIENT_UPGRADE_REQUIRED（426），格式错误返回 CLIENT_VERSION_INVALID（400）。

可选升级不延长授权期限。updateUrl 是发布方配置的更新地址；接入客户端不要自动执行下载内容，安装前验证发布方签名及配置摘要。

