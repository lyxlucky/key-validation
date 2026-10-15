# 备份与隔离恢复

备份使用 PostgreSQL 原生 `pg_dump` 自定义格式，将数据库、Data Protection 密钥文件和 `Licensing`／`Offline` 配置放入同一个 AES-256-GCM 认证加密包。默认不启用定时备份，也没有默认备份目录或加密密钥。目标 RPO 为 24 小时；实际恢复耗时必须以当前数据量的演练记录为准。

## 配置与存储

ASP.NET Core 配置键和环境变量名称如下。敏感值通过现有秘密配置渠道注入，不写入仓库、命令行参数或作业日志。

| 配置键 | 环境变量 | 默认值／要求 |
| --- | --- | --- |
| `Backup:Path` | `Backup__Path` | 未配置；必须是绝对目录，进程可读写 |
| `Backup:EncryptionKey` | `Backup__EncryptionKey` | 未配置；32 字节随机值的 Base64 编码 |
| `Backup:PostgresBinPath` | `Backup__PostgresBinPath` | 未配置；含 `pg_dump` 和 `pg_restore` 的绝对目录 |
| `Backup:Enabled` | `Backup__Enabled` | `false`；仅控制调度，手动备份不要求开启 |
| `Backup:IntervalHours` | `Backup__IntervalHours` | `24`，范围 1–8760 |
| `Backup:RetentionCount` | `Backup__RetentionCount` | `7`，范围 1–365，仅计成功备份 |
| `Backup:MaxArchiveBytes` | `Backup__MaxArchiveBytes` | `268435456`（256 MiB），范围 1024–1073741824 |
| `DataProtection:Path` | `DataProtection__Path` | 创建备份时必须指向现有密钥目录 |
| `ConnectionStrings:Database` | `ConnectionStrings__Database` | 现有数据库连接配置；恢复时提供目标 PostgreSQL 服务器凭证 |

创建备份还要求有效的 `Licensing:CurrentKeyVersion` 和 `Licensing:Keys` 配置。保留所有仍在使用的授权密钥版本及离线签名配置。备份加密密钥本身不进入归档，应与备份副本分开保管；轮换后，旧归档仍需要创建时的加密密钥。本版归档没有密钥自动选择功能。

备份目录不能等于或位于 Data Protection 密钥目录内部，不能经过符号链接／重解析点。推荐分开的 `/app/backups` 和 `/app/keys`。Unix 下新建工作目录权限为 `0700`；已有目录和 Windows ACL 由部署方限制为运行账号访问。加密包在内存中完成，大小上限同时约束原生 dump、归档和解包数据；设置上限时应为加密／解密缓冲区留出额外内存。

调度每分钟检查一次到期时间，数据库锁在多个实例间串行化备份和验证。最近一次作业开始时间加 `IntervalHours` 是下一次执行时间；失败作业也会推迟下一次自动尝试，管理员可随时手动重试。状态查询会列出缺失配置和最近成功时间。

## 管理 API 和 CLI

管理 API 使用现有 Admin Cookie、CSRF 和敏感操作近期验证规则。创建和验证为同步操作，客户端应允许较长响应时间。

| 操作 | 接口 |
| --- | --- |
| 配置／调度状态 | `GET /api/admin/backups/status` |
| 分页作业列表 | `GET /api/admin/backups?page=1&pageSize=20` |
| 创建备份 | `POST /api/admin/backups` |
| 实际隔离恢复验证 | `POST /api/admin/backups/{id}/verify` |

创建返回 HTTP 201 和作业 DTO；应检查 `status` 是否为 `succeeded`，HTTP 201 本身不代表备份成功。失败作业保留 `errorCode`。验证返回 `status`、`migrationCount`、`keysReadable`、`durationMilliseconds` 和 `errorCode`，并更新作业的验证信息。普通查询不返回文件内容、密钥或数据库密码。

在发布目录中，使用与服务相同的安全配置来源执行：

```text
dotnet KeyValidation.Api.dll backup create
dotnet KeyValidation.Api.dll backup verify <backup-id>
dotnet KeyValidation.Api.dll backup restore <backup-id>
```

CLI 退出码：0 成功，1 操作失败，2 参数错误。标准输出为结果 JSON；错误输出仅包含错误分类。原生工具通过 `ProcessStartInfo.ArgumentList` 传参，数据库密码仅进入子进程环境变量，工具输出不会写入日志。每个原生进程限时 5 分钟，超时或取消会终止进程树。隔离数据库的创建和删除涉及检查点及文件系统维护，各自使用 120 秒的命令限时。

成功文件名固定为 `backup-{无连字符的作业 GUID}.kgb`。接口和 CLI 只接收 GUID，不能指定任意文件路径。归档具有 `KGBK0001` 格式头，使用认证的备份 ID／长度、随机 nonce 和 GCM tag；内部含 `database.dump`、`manifest.json`、`configuration.json` 和 `keys/`。验证会检查格式、长度、SHA-256、认证标签、ZIP 路径和大小，以及清单中的迁移版本。恢复时应使用兼容此格式的应用版本和支持备份源服务器版本的 PostgreSQL 工具。

## 灾难恢复与切换

`backup verify` 新建 `keyvalidation_restore_<随机 GUID>` 数据库，实际运行 `pg_restore`，检查关键表和全部迁移记录，并验证归档密钥能解开原 Data Protection 探针、授权配置可用；完成后删除隔离数据库和临时明文。它不会修改当前业务数据库。

`backup restore` 保留新数据库，并导出一个新的 `restored-keys-<随机 GUID>` 目录。该命令直接读取经过认证的归档，不依赖原业务数据库或原 `BackupJobs` 表，因此原数据库丢失时也可执行。它仍需要连接目标服务器的 `postgres` 管理数据库，并有新建、恢复和删除隔离数据库的权限。

1. 准备隔离的 PostgreSQL 服务器、兼容版本的应用和原生客户端工具。将加密文件复制到配置的备份根目录，保持原文件名和 GUID；从独立保管位置注入对应备份加密密钥。
2. 将 `ConnectionStrings:Database` 的主机、端口、账号和密码指向恢复目标服务器。源业务数据库名可以不存在；恢复服务使用 `postgres` 创建全新的随机数据库。配置 `Backup:Path`、`EncryptionKey`、`PostgresBinPath` 和足够的大小上限。
3. 运行 `backup restore <backup-id>`。保存返回的 `databaseName`、`keysDirectory`、`migrationCount` 和 `durationMilliseconds`；命令不会自动切换业务服务。
4. 将输出目录内 `configuration.json` 的 `Licensing:*`／`Offline:*` 值导入新的受保护配置来源。它是以完整配置键为键名的 JSON 字典；环境变量部署需把键名中的 `:` 转为 `__`。将 `DataProtection:Path` 指向该目录下的 `data-protection` 子目录。此时导出的文件包含明文秘密，应保持账号访问限制。
5. 在维护窗口停止旧业务写入，把应用连接切换到返回的新数据库，并加载导出的密钥配置。验证管理员登录、原卡激活、离线凭证及业务数据；通过后再开放写入。数据库连接凭证、备份密钥、TLS 文件和其他部署配置不在归档中，需要单独恢复。

测试环境会自动清理演练数据库；CLI 成功恢复的数据库和密钥目录由操作人保留用于切换。不要把 `backup verify` 作为切换命令，也不要直接覆盖现有数据库或密钥目录。

## 部署

`deploy/backend.Dockerfile` 安装 PostgreSQL 17 客户端，运行账号仍为非 root 的 `app`。`deploy/compose.yaml` 新增 `backup-archives:/app/backups`，保留独立的 `protection-keys:/app/keys`。在 `.env` 中设置以下非秘密选项，并通过秘密配置提供 `BACKUP_ENCRYPTION_KEY`：

```dotenv
BACKUP_PATH=/app/backups
BACKUP_ENABLED=true
BACKUP_INTERVAL_HOURS=24
BACKUP_RETENTION_COUNT=7
BACKUP_MAX_ARCHIVE_BYTES=268435456
```

Compose 自动设置 `Backup__PostgresBinPath=/usr/lib/postgresql/17/bin`。先手动创建并验证一份备份，再启用调度。Nginx 的备份 API 超时为 660 秒。示例命令：

```text
docker compose --env-file .env -f deploy/compose.yaml exec api dotnet KeyValidation.Api.dll backup create
docker compose --env-file .env -f deploy/compose.yaml exec api dotnet KeyValidation.Api.dll backup verify <backup-id>
```

恢复时可以使用单独的应用发布目录执行 CLI，不要求原 API 进程正常运行。当前开发环境没有 Docker，本次仅修改了容器配置，没有执行镜像构建或容器恢复验收。

## 保留、失败与演练记录

每次成功备份后，系统只删除超过保留份数且数据库记录、格式头中的 ID、长度和 SHA-256 全部匹配的成功归档。未知文件和未通过归属校验的文件保持原样。保留策略不能替代异地副本；24 小时 RPO 还取决于调度成功和副本可用性。

| 错误分类 | 处理方向 |
| --- | --- |
| `BACKUP_PATH_UNCONFIGURED`／`BACKUP_PATH_INVALID`／`BACKUP_PATH_UNSAFE` | 核对绝对目录、密钥目录边界和链接 |
| `BACKUP_ENCRYPTION_KEY_INVALID` | 核对 Base64 和解码后的 32 字节长度 |
| `POSTGRES_TOOLS_UNAVAILABLE`／`PG_DUMP_FAILED`／`PG_RESTORE_FAILED` | 核对工具目录、版本和数据库权限 |
| `BACKUP_TOO_LARGE` | 核对数据量、磁盘和内存后调整上限 |
| `BACKUP_AUTHENTICATION_FAILED`／`BACKUP_CHECKSUM_MISMATCH`／`BACKUP_ARCHIVE_INVALID` | 保留证据，核对原文件和对应密钥，改用已验证副本 |
| `RESTORE_TABLES_MISSING`／`RESTORE_MIGRATIONS_MISMATCH`／`BACKUP_KEYS_INVALID` | 核对应用版本及归档完整性，不切换业务 |
| `POSTGRES_PROCESS_TIMEOUT`／`BACKUP_DATABASE_FAILED`／`BACKUP_IO_FAILED` | 核对连接、数据库状态和存储 |
| `PLAINTEXT_CLEANUP_FAILED`／`RETENTION_FAILED` | 检查目录权限和剩余文件，作业保留错误标记 |
| `BACKUP_INTERRUPTED`／`RESTORE_INTERRUPTED` | 下一次操作发现前一次未结束；重新执行并检查主机故障原因 |

正常失败、取消和超时路径都在 `finally` 清理临时明文。进程被强制终止或主机断电时无法执行 `finally`；发现 `.work-*` 残留应先确认没有运行中的备份，再核对配置根目录及目录归属后清理。原生工具标准错误不会落盘，因此失败分类不会包含连接凭证或工具响应正文。

2026-10-08 的真实 PostgreSQL 17.10 演练使用测试数据库、6 条迁移和真实密钥材料；原数据库不可用场景的 CLI 恢复成功，恢复后的授权摘要与归档配置计算结果一致。具体归档大小、恢复时间和最终测试证据见 [Task 2 报告](../.superpowers/sdd/2026-10-08-roadmap-completion/task-2-report.md)。这些是本机小型数据集的测量，不是生产 RTO 承诺。
