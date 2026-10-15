# 验证记录

更新：2026-10-09。环境：Windows、.NET SDK 10.0.400、Node.js 22.22.0、原生 PostgreSQL 17.10、Chromium。


## 全局卡种目录与应用引用

2026-10-09：配套设计与九项实施任务已完成。全局卡种共享完整授权规则，应用独立引用并控制发行状态；直接发卡、订单、渠道定价、查询、报表与审计使用一致的目录。已发行卡密及已创建订单继续保留原有快照。管理权限、数据库写入边界、事务锁顺序、请求幂等及停用竞争均有回归覆盖。

| 验证 | 有效结果与证据 |
| --- | --- |
| 后端单元 | 59 项通过；[TRX](../test-results/shared-plans/backend/unit-final.trx) |
| PostgreSQL 集成 | 318 项通过；[完整运行](../test-results/shared-plans/backend/shared-plan-full.trx)的 317 项通过，旧版本迁移样本修正后的[单项复验](../test-results/shared-plans/backend/operations-migration-baseline-fixed.trx)通过 |
| C# 客户端 / 桌面逻辑 | 36 / 62 项通过；[客户端](../test-results/shared-plans/client/client.trx)、[桌面](../test-results/shared-plans/desktop/desktop.trx)；不含旧 roadmap 的 Windows GUI 布局验收 |
| Chromium 浏览器 | 59 个不同用例通过；[完整运行](../test-results/shared-plans/browser-final.json)54 项通过，3 项失败在[修正后复验](../test-results/shared-plans/browser-regression-fixes.json)通过，新增[草稿及范围权限](../test-results/shared-plans/browser-module-lifecycle.json)2 项通过 |
| 构建及迁移 | TypeScript / Vite 生产构建通过；最终 API Release 发布通过；EF 无待生成模型变更；两次迁移、数据保留及降级保护均通过真实数据库测试 |
| 界面 | 375 / 768 / 1024 / 1440px 无页面横向溢出，移动端表格内部滚动；保留现有工作区、表单草稿和试用策略入口 |

合计 **534 个不同自动化用例通过，最终有效结果无失败或跳过**。这是完整运行与必要复验的合并结果，不表示一次运行全部通过；原始失败证据保留。旧迁移测试移除了旧版本尚不存在的引用审计事件，再验证原有六条审计记录；浏览器测试按当前保存确认流程保存 CSV 后关闭，并将全局卡种审计断言改为平台级操作。修正仅影响相应测试样本与断言，因此复验这些用例，未重复全部慢速套件。机器可读的[验收清单](../test-results/shared-plans/verification-manifest.json)记录各运行、去重结果及源码 SHA-256。

本地 API 已更新为 backend/artifacts/shared-plan-final，恢复默认限流，地址为 http://127.0.0.1:5080；前端为 http://127.0.0.1:5173。OpenAPI 类型已重新生成。未增加运行时依赖，未创建 Git 提交（当前目录无 Git）。部署顺序及降级限制已写入 README，用户指南与 API 契约已同步。当前前端构建仍有既有的大包体积提示。

## 登录会话、页签与账号安全界面

按用户反馈调整双重验证、工作区切换和账号安全页，并将左侧分组改为手风琴。顶部页签采用浏览器风格，保留现有切换、关闭、键盘操作和页面状态功能。

双重验证在登录或首次绑定成功后随当前登录会话保留；普通业务操作和自行改密不再重复要求验证码。关闭双重验证与重新生成恢复码确认当前密码即可。原有 30 分钟空闲、8 小时绝对会话上限、安全戳撤销、验证码防重放与 5 分钟绑定/登录挑战时限继续生效。

| 验证 | 本轮证据 |
| --- | --- |
| 后端相关回归 | **143 项通过，0 失败、0 跳过**；涵盖账号安全、TOTP、账号/RBAC、受保护业务、离线授权与日志；[TRX](../.local/test-results/auth-session/auth-session-final.trx) |
| 浏览器相关回归 | **65 项全部通过，3 分 32 秒，0 失败、0 跳过、0 重试**；涵盖动效、工作区页签、导航分组、编辑器生命周期、账号安全、响应式与登录/退出边界；[JSON](../.local/test-results/ux-session-browser-final.json) |
| 前端生产构建 | `npm --prefix frontend run build` 的 TypeScript 与 Vite 检查通过；3283 模块，Vite 13.38 秒；主包 1698.14 KB，保留现有体积提示 |
| 响应式与可操作性 | 375 / 768 / 1024 / 1440px 的文档与当前页面均无横向溢出；手机页签关闭及菜单按钮为 44px；检查了桌面、折叠侧栏和手机上下半屏 |
| 接口与本机运行版本 | 新 API 已生成前端接口类型；`ux-session-20261009` 中的 API DLL 与本次 Release 输出 SHA-256 一致 |
| 独立审查 | 后端会话逻辑与前端界面均完成审查；时间戳边界、溢出页签关闭按钮、可见滚动条和文字对比度问题修复后复审无剩余问题 |

动效回归检查切换后的第一帧：新页面不透明、没有位移、同时只显示一个页面。筛选、分页、深链接、草稿、滚动位置与弹窗归属继续验证；退出、换号、权限撤销和实际关闭会清除对应状态与秘密内容。导航覆盖桌面、手机、图标弹出菜单，以及页签和浏览器前进/后退时的展开状态。

溢出测试曾连续导航过快，导致中间页面未提交，实际只有六个页签。现按各目的页签的选中状态等待，确保七个页签全部打开后测量；浏览器测试显式移除 Chromium 无头模式的 `--hide-scrollbars` 默认参数，实际拖动原生滚动条并确认整个当前页签及关闭按钮可见。该项与退出换号场景分别连续四次通过；重复验证不另计入本轮总数。

预览截图位于 `.local/ux-review/`。未开启状态来自专用回归账号；已开启状态仅替换浏览器中的安全状态响应用于视觉检查，没有修改原开发管理员的双重验证配置。截图遮住账号名称，未采集认证器密钥或恢复码。使用说明和 HTTP 契约同步更新。

本机 API 当前使用 `backend/artifacts/ux-session-20261009`，前端位于 `http://127.0.0.1:5173`；完成回归后已重启并恢复默认限流（登录每分钟 10 次、全局每分钟 600 次），OpenAPI 和前端 HTTP 检查正常。本轮共 **208 项相关自动化测试通过**，前几轮全量结果分别保留在下方。

## 剩余路线图与管理体验验证

本轮实现剩余13项：F03、F09、F10、F11、F13、F14、F15、F16、F17、F19、F20、F21、F22。F18支付按用户要求暂缓。追加的标准多角色RBAC、可读且分开的登录/操作日志、分组与整栏收缩、保留状态的工作区标签、自然过渡和接入说明已一并通过最终回归与独立审查。完整C# Windows桌面demo从该验收通过后开始，另行记录证据。

| 验证 | 本轮证据 |
| --- | --- |
| 最终完整后端 | **59项单元 + 289项真实PostgreSQL HTTP集成测试通过**，0失败/跳过/清理错误；含所有前批功能、权限竞争、事务幂等与客户端扩展 |
| 最终完整C#客户端 | **36项通过**，0失败/跳过；含真实签名、设备/应用/期限/功能、回拨与持久化失败，以及5个真实控制台进程场景 |
| 离线网络隔离 | 五个真实离线/配置错误场景使用受控TCP探针，均为0连接；离线失败不会转为在线授权 |
| 最终完整浏览器 | **109项全部通过，7.15分钟**，0失败/跳过/重试/不稳定用例；每项仅一次通过结果，无附件；原100项全部保留并增加9项退出边界回归 |
| 最终前端构建 | TypeScript与Vite通过，12.63秒、3283模块；主包1694.69KB，保留体积提示；133个前端源码/测试/配置文件在构建和完整测试前后哈希一致 |
| 后端构建 | Release构建0警告、0错误，8.61秒；运行发布目录四个业务DLL与此构建逐一比对 |
| 迁移与OpenAPI | `20261009041239_ClientExtensions`已应用；EF无待迁移模型变化；真实OpenAPI导出通过，前端类型从运行API重新生成 |
| 兼容性 | 真实迁移回退/重升保留旧授权、绑定、会话；旧客户端在无强制最低版本时继续可用 |
| 备份恢复 | 实际`pg_dump/pg_restore`完成加密备份与隔离恢复；原数据库不可用时CLI恢复成功，恢复后的离线密钥能签名并由原公钥验签 |
| 依赖维护 | 最小固定`Microsoft.OpenApi 2.7.5`修复GHSA-v5pm-xwqc-g5wc；正常审计restore成功，后端传递依赖审计未报告漏洞 |
| 独立审查 | Task1/2/2a/3/4及最终前端/综合Spec与Quality均通过；退出请求入口、草稿保留和秘密DOM清理问题已关闭；最后退出边界复审无新增问题 |

业务和管理体验验收共**493项自动化测试通过**：348后端、36客户端、109浏览器，51项定向验证和重复复测不叠加。[最终清单](../.superpowers/sdd/2026-10-08-roadmap-completion/final-verification-manifest.md)及JSON附件记录316个源码/配置/测试文件和证据的SHA-256；185个沿用的后端、客户端及部署文件与先前实测版本完全一致。完整浏览器运行前后133个前端文件哈希相同，9个运行模块与Vite实际提供的源码一致。退出前后普通请求均被拦截，旧Cookie不能恢复，新登录不受旧退出完成影响；独立[退出边界复审](../.superpowers/sdd/2026-10-08-roadmap-completion/task-6-residual-review.md)通过。完整证据在[本轮验收目录](../.superpowers/sdd/2026-10-08-roadmap-completion/)，功能映射见[验收矩阵](../.superpowers/sdd/2026-10-08-roadmap-completion/acceptance-matrix.md)。桌面demo单独验收，未计入本组结果。

浏览器覆盖真实发卡/禁用/冻结/换绑、订单交付与门户审批、渠道额度和丢失响应恢复、细分角色与越权拒绝、版本更新后旧心跳426、离线签发下载与撤销、可读审计、标签状态保留/关闭释放及登录退出动画。新增页面在375/768/1024/1440px验收；非秘密截图位于`.local/roadmap-preview/`，代表图片已人工查看。秘密界面关闭自动trace与截图。

本轮修复过授权提交前复查遗漏、风险扫描游标跨提交遗漏、门户退出与写入竞争、旧单角色适配的最后管理员保护、标签弹窗归属、React Activity重连焦点异常、重复组件key留下冻结弹窗和退出动画延迟移除秘密。最终修复还覆盖旧会话请求及响应消费的真正取消、退出后迟到Cookie响应、废弃响应body释放、表单草稿恢复、秘密DOM即时清理和快速标签点击与浏览器历史竞争。新增回归保留原有动画、深链接、networkidle及权限断言。

该轮验收时本机API使用`backend/artifacts/roadmap-task-4-preview-1`，当前运行版本与限流状态见本页最新记录。原开发管理员的MFA未变动，自动化使用单独回归账号。Docker不可用，未声称完成容器运行验证。未进行生产负载测量；离线撤销延迟和磁盘快照/本地篡改边界详见客户端说明。

## 历史授权策略验证

2026-10-07 完成 F02 冻结/解冻、F05 并发会话、F06 激活期限、F07 试用授权、F08 功能分级。范围和推荐规则均经用户确认。

| 验证 | 结果 |
| --- | --- |
| `dotnet test backend/KeyValidation.slnx -c Release --no-restore --verbosity quiet` | **40 项领域测试 + 127 项 PostgreSQL HTTP 集成测试全部通过，0 跳过**；集成部分 24 秒 |
| `dotnet test samples/KeyValidation.Client.Tests --no-restore --verbosity quiet` | **10 项全部通过**；含原 5 项租约测试和新增功能/试用协议测试 |
| `node .local/run-policy-browser.mjs` | **20 项浏览器测试全部通过，2.2 分钟**；本机助手注入测试账号与 Chromium 路径后运行完整 Playwright 套件，相当于配置环境后执行 `npm --prefix frontend run test:e2e` |
| `npm --prefix frontend run build` | TypeScript 与 Vite 通过，最终 Vite 8.14 秒 |
| `npm --prefix frontend run api:types` | 从新版本机 API 生成全部新增接口类型 |
| EF `migrations has-pending-model-changes`（Release、`--no-build`） | 模型与迁移一致；`20261006074346_AuthorizationPolicies` 已应用于本机数据库 |
| 旧数据兼容 | 独立 PostgreSQL 迁移测试确认旧卡/卡种在线上限回填为原设备上限，激活期限为空、功能为空，旧卡继续可激活 |
| 独立审查 | 后端事务/安全/迁移、前端契约及客户端功能检查均已审查，无待修问题 |

本轮共 **197 项自动化测试**：167 后端、10 客户端、20 浏览器。新增 34 后端、5 客户端、3 浏览器测试均已包含其中，不重复计数。

覆盖精确冻结剩余时间、幂等解冻、冻结与禁用优先级、冻结卡按项续期失败、冻结/心跳/激活竞争、租约与绝对到期名额释放、同设备会话替换、激活期限等值边界、权限快照、试用去重/重试/到期/策略关闭，以及正式激活和首次试用竞争。数据库触发器模拟升级末尾失败，确认正式卡激活与试用撤销整体回滚。独立冻结历史和领取记录不随审计清理失效。

浏览器通过真实 API 配置卡种在线上限、激活天数与功能，完成手机冻结/解冻和历史查看、开启试用、重复领取及正式升级。原发卡/操作断线恢复、账号安全、换绑和各页面回归均保留通过。卡种表单在 375/768/1024/1440px 验证无横向溢出，保存按钮始终可见；试用和冻结流程在 375px 完成。代表截图位于 `.local/policy-preview/`，已人工查看平板卡种表单、手机试用表单及冻结详情。

验证中修复了旧设备上限错误优先级、OpenAPI 新字段导出、混合批量续期行为，以及平板长表单遮住保存按钮的问题。第一次完整浏览器运行经历约 8.6 小时长中断，旧管理用例在登录前超时；保持用例和超时设置不变，完整复测 20 项通过（该用例 11.9 秒）。初次基线的数据库连接失败由启动现有本机 PostgreSQL 解决。

测试临时将本机登录限额设为 100/分钟，完成后已恢复默认 10 并重启最新版 API。发布目录 `backend/artifacts/publish/` 和前端 `frontend/dist/` 已更新。未执行本轮 Docker 部署、依赖漏洞审计或长时间客户端实测；已有相关环境限制见本文件末尾。

详细规则见 [授权策略规格](superpowers/specs/2026-10-06-authorization-policies-design.md)，执行过程见 [交付记录](superpowers/plans/2026-10-06-authorization-policies-progress.md)。

## 历史账号安全与换绑验证

2026-10-06 完成 roadmap 的 F01 自行改密、F04 设备换绑规则、F12 双重验证。

| 验证 | 结果 |
| --- | --- |
| `dotnet test backend/KeyValidation.slnx -c Release --no-restore` | **29 项领域测试 + 104 项 PostgreSQL HTTP 集成测试，全部通过，0 跳过**；集成测试约 31 秒 |
| `npm --prefix frontend run test:e2e` | **17 项全部通过，约 1.7 分钟**；保留原 15 项，新增账号安全生命周期与设备策略场景 |
| `npm --prefix frontend run build` | 生成最新接口类型后 TypeScript 与 Vite 通过；最终 Vite 12.44 秒 |
| `npm --prefix frontend run api:types` | 从已发布的本机 API 生成新增安全与换绑 DTO |
| EF `migrations has-pending-model-changes` | 无未迁移模型变更；新增 AccountSecurityRebinding 已应用 |
| 旧数据兼容 | 独立数据库迁移测试确认旧卡不限制、新发卡默认启用；现有账号不强制改密 |
| 视口 | 登录和八个工作台页面通过 375/768/1024/1440px 横向溢出检查；账号安全流程实际在 375px 操作 |

本轮共 **150 项自动化测试**。其中安全后端 45 项针对性测试和换绑 23 项 HTTP / 6 项领域测试均包含在完整后端测试数内，不重复相加。没有重跑历史 C# 客户端定时示例或依赖漏洞审计。

安全验证覆盖旧密码、首次改密门禁、当前会话保留/其他 Cookie 失效、RFC TOTP 向量、challenge 到期与单次使用、安全戳失效、TOTP 重放、恢复码并发单次消费、锁定、加密持久化、敏感操作证明过期、等待锁期间凭证失效，以及强制改密时 MFA 证明过期后的恢复路径。换绑验证覆盖次数/冷却的并发竞争、窗口/冷却边界、相同时刻重置代次、重复释放、审计清理不影响配额、客户端权限和会话撤销。

浏览器完成首次改密、认证器二维码/密钥设置、TOTP 登录、恢复码登录、关闭 MFA，以及设置单卡次数、客户端释放、后台重置。安全 E2E 关闭 trace 和自动失败截图，避免认证器密钥/恢复码落入跟踪文件；仅对没有秘密内容的安全页生成预览。新增截图在 `.local/security-preview/`，八页视口截图在 `.local/ui-preview/`；已人工查看桌面与手机安全页。

独立后端交叉审查和安全前端源码审查完成。修复的实际问题包括：等待账户锁期间旧安全戳仍可写入、CLI 非管理员遗漏首次改密要求、强制改密与 MFA 证明过期互相阻塞。浏览器用例补上退出完成等待，并区分恢复码单选按钮与文本框。

本机完整浏览器测试临时采用每分钟 100 次认证/安全操作限额，验证后恢复默认 10 次；生产默认未改变。详细实现报告见 [账号安全](superpowers/plans/2026-10-05-account-security-backend-report.md)、[换绑](superpowers/plans/2026-10-05-rebinding-backend-report.md) 及 [执行记录](superpowers/plans/2026-10-05-account-security-rebinding-progress.md)。

## 历史六项管理扩展验证

2026-10-05 完成账号管理、批次管理、续期延期、批量操作、卡密备注标签和 3 / 7 / 30 天到期列表。用户确认的续期规则已落实；其余 22 项功能仍保存在后续 spec。

| 验证 | 结果 |
| --- | --- |
| `dotnet test backend/KeyValidation.slnx -c Release --no-restore` | **23 项领域测试 + 54 项 PostgreSQL HTTP 集成测试通过，0 失败、0 跳过**；同时完成 Release 编译 |
| `npm --prefix frontend run build` | TypeScript 与 Vite 生产构建通过；最终 Vite 构建 12.09 秒 |
| `npm --prefix frontend run test:e2e` | **15 项全部通过，约 1.4 分钟**；7 项原业务、4 项管理扩展、4 项 UI 回归 |
| EF `migrations has-pending-model-changes`（Release、`--no-build`） | 模型与迁移一致；新增 `20261005092000_ManagementExpansion` 已应用于本机数据库，集成测试使用隔离数据库执行正式迁移 |
| OpenAPI 类型 | 运行中的 API 已生成 `frontend/src/lib/schema.d.ts`，新增前端 DTO 引用生成类型 |
| 响应式布局 | 登录及七个工作台页面在 375、768、1024、1440px 均无文档横向溢出；新增操作弹窗在 375px 可确认、查询结果并完成 |

本轮合计 **92 项自动化测试**。没有重跑下方历史客户端测试、定时客户端实测或依赖漏洞审计，不将其混入本轮结果。

新增覆盖包括：未激活/有效/已过期/禁用续期及上限；并发重复请求只续期一次；逐项失败与异常事务回滚；跨应用锁序；备注标签筛选；批次统计；到期边界及排序；账号角色、密码、启停、旧 Cookie 失效、自保护、最后管理员保护及锁后权限重查。

浏览器使用真实 API 完成账号创建（默认只读）、调整角色、重置密码、停用/恢复，批次定位、备注标签、单张/批量续期、批量启停和到期筛选；模拟响应丢失及后续 429，确认刷新后可查询原结果且只延长一次。分页回归使用可控响应验证列表从 21 条减少为 20 条时第二页自动回退。原发卡恢复测试保留并通过。

审查和浏览器发现的问题已修复：3 天窗口契约、账号表单在 StrictMode 下过早清空、账号及授权操作后的审计缓存失效、空页回退，以及原测试中未等待卡种选择/禁用请求完成的时序问题。详见 [审查记录](superpowers/plans/2026-10-05-management-expansion-review.md)。

完整套件登录频次超过默认 10 次/分钟，曾触发两项 429；最终运行将本机 API 进程临时设为 `RateLimiting__LoginPerMinute=100`，应用默认和生产配置保持 10。本机验证完成后恢复默认限额。NuGet 使用现有离线缓存还原且关闭此次审计获取，未变更依赖版本。

截图位于 `.local/ui-preview/` 与 `.local/management-preview/`。已人工查看新增页面的桌面/手机截图及手机操作结果弹窗。预览地址：`http://127.0.0.1:5173`。

## 历史 UI 重绘验证

2026-10-05 完成浅色专业工作台重绘，覆盖登录、概览、应用管理、卡密管理、操作审计及全部现有弹窗和抽屉。

| 验证 | 结果 |
| --- | --- |
| `npm --prefix frontend run build` | TypeScript 与 Vite 生产构建通过，13.74 秒 |
| `npm --prefix frontend run test:e2e` | **11 项通过，49.0 秒** |
| 视口布局 | 五个页面在 375、768、1024、1440px 均无文档横向溢出 |
| 移动导航 | 菜单/品牌跳转后关闭，Escape 恢复按钮焦点，目的页回到顶部并聚焦主内容 |
| 手机弹窗 | 创建应用、编辑卡种、生成卡密、卡密详情、禁用确认及生成结果均已检查；长正文滚动，底部操作可见 |
| 日期筛选 | 手机弹层完整位于视口内，月份和星期为中文；实际选取起止日期发出正确筛选请求，重置清空范围 |
| 业务回归 | 原有响应丢失恢复、429 重试、账号隔离、真实发卡/导出/激活/心跳/禁用/审计全部通过 |
| 独立代码审查 | 导航关闭、滚动焦点、日期弹层三项问题均已修复并通过回归 |

截图存放于 `.local/ui-preview/`，由 `frontend/e2e/ui.spec.ts` 和完整业务测试生成；生成结果截图对卡密明文做遮罩。已人工查看登录、概览、应用列表、卡密列表、审计、表单、详情及日期弹层的代表性截图。生产构建输出位于 `frontend/dist/`；本机预览地址为 `http://127.0.0.1:5173`。

该次 UI 重绘只修改前端，没有重跑下方历史后端/客户端基线，不将它们计入当次 11 项测试；最新管理扩展结果见上节。

## 原系统基线验证

| 验证 | 命令 / 方法 | 结果 |
| --- | --- | --- |
| 后端 Release 构建 | `dotnet build backend/KeyValidation.slnx -c Release --no-restore` | 0 错误、0 警告 |
| 领域及密钥规则 | `dotnet test backend/KeyValidation.slnx -c Release --no-build --no-restore` | 11 项通过 |
| PostgreSQL HTTP 集成测试 | 同上，测试自动创建独立数据库并执行正式迁移 | 26 项通过，0 跳过 |
| 客户端租约计时 | `dotnet test samples/KeyValidation.Client.Tests --no-restore` | 5 项通过 |
| OpenAPI 类型生成 | `npm --prefix frontend run api:types` | 从运行中的 API 成功生成 |
| 前端生产构建 | `npm --prefix frontend run build` | TypeScript 检查、Vite 构建通过 |
| 浏览器端到端测试 | `npm --prefix frontend run test:e2e` | 7 项通过，约 29 秒 |
| 前端生产依赖审计 | 在 frontend 执行 `npm audit --omit=dev --audit-level=moderate` | 0 个已报告漏洞 |
| C# 示例实际运行 | 针对 65 秒授权启动示例进程，并检查数据库状态 | 激活、约 60 秒后的实际心跳、到期退出码 2、会话撤销均通过 |
| 数据库迁移一致性 | `dotnet-ef migrations has-pending-model-changes` | 模型与迁移一致 |
| 启动脚本 | Node 语法检查、PowerShell AST 解析 | 通过 |

自动化测试合计 **49 项：37 项后端、5 项客户端、7 项浏览器**。另执行了一次完整的 C# 客户端定时运行验证。后端测试与客户端测试没有跳过项。

## 浏览器覆盖

1. 登录页必填校验。
2. 卡种在提交前被停用，发卡明确拒绝后可修改表单。
3. 生成响应在服务端提交后丢失，恢复时仅发现一个批次；禁用该批次后两张卡均禁用。
4. 当前账号不会加载其他账号或旧版未区分账号的待恢复请求。
5. 响应丢失后的重试再次遇到 429，原请求标识仍保留并可找回原批次。
6. 登录 → 创建应用 → 创建卡种 → 生成与下载 → 客户端激活/心跳 → 后台禁用 → 心跳拒绝 → 审计查询。
7. 390 像素宽度下概览页没有页面级横向溢出。

## 核心后端覆盖

- 首次激活计时、重复激活不延期、卡种参数快照。
- 两台设备并发激活时设备上限保持有效；并发重复发卡只产生一个批次。
- 租约和绝对会话有效期边界；禁用、恢复、解绑、登出和撤销。
- Cookie、CSRF、角色权限、管理员空闲/绝对超时、密码重置后的会话失效。
- 生产 Secure/HttpOnly Cookie、不信任未配置的代理头。
- 非对象激活 JSON 返回 400；异常请求也受全局限流；应用标识空白和 JSON 字段大小写不能绕过应用限流。
- 正式迁移、审计/会话保留期、CLI 账号操作、查询响应不暴露卡密明文。

## 测试与审查过程

领域规则先运行失败测试，再实现行为。客户端租约测试从 4 项预期失败修正为全部通过。浏览器测试先验证空界面缺少登录表单；后续捕获并修复了表单 ID 重复、移动端布局溢出和发卡恢复状态问题。

独立审查发现的四项问题及一项修复回归均已关闭：首次拒绝导致表单锁定、跨账号恢复请求复用、异常 JSON 返回 500、应用限流归一化不一致，以及恢复重试被拒绝时丢失原请求标识。相关情况均有回归测试。

## 执行限制与本机配置

- 本机没有 Docker，**未执行容器镜像构建或 Compose 启动**。交付了 Dockerfile、TLS Nginx 配置及部署步骤；需要在目标 Docker 环境验证证书挂载和网络。
- NuGet 在本沙箱中遇到 Windows TLS 错误；使用只监听本机的 Node 转发服务下载依赖，仍验证上游 TLS。此临时配置位于被忽略的 `.local`，项目未关闭证书校验。正常环境可以直接使用 NuGet。
- NuGet 还原的审计获取在临时环境中关闭，因此这里不声称完成了后端依赖漏洞审计；前端表中的审计仅针对生产 npm 依赖。
- Playwright 使用本机已有 Chromium，通过 `PLAYWRIGHT_CHROMIUM_EXECUTABLE` 指定位置；其他环境可运行 `npx playwright install chromium`。
- 本机数据库、随机账号、授权密钥、测试截图/跟踪和包缓存均位于被忽略的目录或 `.env` 中，未写入交付源码。测试日志出现的 Node `NO_COLOR/FORCE_COLOR` 提示仅影响控制台着色。
- 当前工作区没有 Git 仓库，因此没有创建提交或远程发布。


## Developer MCP proxy verification (2026-10-11)

Before adding the proxy: `powershell -NoProfile -File scripts/verify-developer-mcp.ps1 -BaseUrl http://127.0.0.1:5173` returned HTTP 404, exit 1 (RED).

Published the existing API with `dotnet publish backend/src/KeyValidation.Api/KeyValidation.Api.csproj -c Release --no-restore -o .local/mcp-api` (exit 0). A hidden Node helper loaded existing local secrets directly into environment variables and launched only owned processes, API on unused 5188 and Vite on 5189 with `API_PROXY_TARGET=http://127.0.0.1:5188` and the exact 5189 Origin allowlist. It ran no migration or administrator bootstrap and left the existing 5080 runtime untouched.

`powershell -NoProfile -File scripts/verify-developer-mcp.ps1 -BaseUrl http://127.0.0.1:5189` and the same command with `-TrailingSlash` both passed, exit 0: legacy 2025-11-25 initialization, current 2026-07-28 discovery, JSON/SSE protocol parsing, exact five tools and nine public resources, and same-origin public HTTP documentation/MCP Markdown/version parity. Owned helper processes were stopped after verification.

`Get-Command docker,nginx -ErrorAction SilentlyContinue` found neither executable. Consequently `nginx -t`, Compose runtime and public HTTPS/TLS verification were not performed; production configuration is supplied but must be validated on the deployment host. Backend full suite, sample verifier and frontend build/Playwright results are recorded by their respective task reports/final verification; this proxy task did not rerun the broad suites.

HTML fallback negative check: `node .local/check-mcp-html.mjs` served a synthetic HTML 200 response on owned port 5190; the PowerShell probe reported unexpected text/html and exited 1. After the final trusted-forwarding adjustment both canonical and trailing-slash live probes were rerun and passed. The helper only terminated PIDs recorded from its own spawn operations.

After the Vite configuration change, `npm --prefix frontend run build` passed (exit 0, TypeScript and Vite, 3292 transformed modules); the existing large-bundle warning remains. Final full backend verification is coordinated separately and must not be inferred from these proxy checks.

## Developer MCP 最终验收（2026-10-11）

- `dotnet test backend/KeyValidation.slnx --no-restore --results-directory .superpowers/sdd/developer-mcp/final-tests --logger trx`：退出码 0，领域 59/59、集成 343/343，合计 402 项通过，无失败或跳过。TRX 保存于上述目录。
- 首轮完整回归曾有两项 MCP 发现请求返回 500；测试密钥保存路径改为工作区并加入不记录正文和参数的诊断后，完整回归通过。该结果不证明首轮失败的具体原因，未修改生产代码或放宽协议超时来消除失败。
- `python scripts/verify-developer-examples.py`：C#、Python、JavaScript 可执行示例验证通过，包括跨站重定向拒绝、授权截止、网络故障和时钟场景；示例目录测试 7 项通过。
- 指定本机 Chrome 后运行 `npm --prefix frontend run test:e2e -- developer-mcp.spec.ts`：2 项通过；前端生产构建通过，保留既有包体积警告。额外完整前端测试被取消，取消前两项既有实时登录测试失败，因此不声称完整前端套件通过。
- 分任务审查及整体独立审查通过；文档客户端描述、HTTP DTO 字段、九个资源精确清单和 Nginx 安全头继承问题已修正。
- 已启动当前后端，前端 `http://127.0.0.1:5173`、API `/health/ready`、同源公开资料均返回 200；对该前端 origin 运行 MCP 探测脚本通过，五个工具、九个资源及 HTTP/MCP 文档内容和版本一致。
- 本机未安装 Docker/Nginx，生产配置语法、容器运行和公共 HTTPS 仍须在部署主机验证。Python 示例的 socket 超时是读取空闲超时，持续缓慢响应可能推迟退出，但阻塞期间不运行受保护操作且过期响应不能恢复授权。
