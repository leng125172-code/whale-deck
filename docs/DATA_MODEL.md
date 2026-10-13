# WhaleDeck 数据模型

范围修订：2026-10-13 [P0–P5 基线](GOAL_P0_P5.md) 为新增网站/证书/运行环境、恢复/异地存储、邮件/DNS、通知渠道、Git/AI、节点/团队/订阅/配额/扩展记录增加建模工作。下方现有实体与旧预留仍是历史实现说明，新增任务实施时须补全实体、索引、版本/租户隔离/保留和 migration，不能只加 JSONB 占位；人类身份继续引用 Authentik，不创建平台本地密码表。

本文定义 WhaleDeck 自有 PostgreSQL 数据和 Valkey 临时数据。Authentik 是用户、凭据、MFA、资料和用户组的事实来源，不创建本地密码表或完整用户资料副本。

新增设计实体 `DM-DEV-WORKSPACE/ENVIRONMENT/RUNTIME/SOURCE/RUN/STEP/REPORT/SESSION/GRANT/ARTIFACT` 详见 [开发空间模型](DEVELOPMENT_WORKSPACES.md#8-数据库与存储模型)。一个空间对应多 runtime，报告绑定不可变源码/环境版本与执行记录；稳定字段关系化建模并用 EF migration 落地，Secret 只存引用。当前尚未新增这些生产表，不以文档或 JSONB 占位判为实现。

## 1. 设计约束

- PostgreSQL 保存平台配置、个人门户、资源注册、任务、审计、指标、告警、备份与同步历史。
- Valkey 保存可重建的权限快照、概览缓存、心跳、短期资源状态、锁和任务进度。
- 长期 Secret、OIDC Token、现有数据库密码、私钥、SQL Server 产品密钥和 Git 凭据不得进入 PostgreSQL、Valkey、审计或日志。数据库账号创建/轮换后生成的新密码只允许进入 5 分钟、单次消费的 Valkey Secret，读取后原子删除。
- 所有时间使用 UTC `timestamptz`，ID 默认使用 UUID。
- 每次 schema 变化使用 EF Core migration；生产环境禁止 `EnsureCreated`。
- JSONB 只用于结构变化频繁的快照/详情，不用来逃避稳定业务字段建模。

## 2. 已有实体

### `DM-AUDIT` `audit_events`

当前代码已有 `AuditEvent`。后续 migration 在保留历史数据的前提下扩展：

| 字段 | 类型 | 约束/说明 |
| --- | --- | --- |
| `id` | uuid | PK |
| `actor_subject` | varchar(256) | Authentik Subject 或系统主体；由现有 `actor_id` 演进 |
| `action` | varchar(128) | 稳定动作码 |
| `target_type` | varchar(64) | 容器、数据库、任务、设置等 |
| `target_id` | varchar(512) | 资源 ID；由现有 `target` 拆分/演进 |
| `result` | varchar(32) | `Succeeded/Failed/Denied/Accepted` |
| `source_ip` | inet | 可空，系统任务无来源 IP |
| `job_id` | uuid | 可空，关联异步任务 |
| `trace_id` | varchar(64) | HTTP/RPC 关联 |
| `detail_json` | jsonb | 只存脱敏摘要 |
| `occurred_at_utc` | timestamptz | 不可变 |

索引：`occurred_at_utc desc`、`actor_subject + occurred_at_utc`、`target_type + target_id + occurred_at_utc`、`job_id`。普通业务代码不提供更新和删除审计事件的方法。

### `DM-SETTING` `platform_settings`

当前代码已有 `PlatformSetting`。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `key` | varchar(256) | PK，使用分区命名如 `metrics.retention_days` |
| `value_json` | jsonb | 非敏感动态设置 |
| `version` | bigint | 并发控制，新增字段 |
| `updated_by_subject` | varchar(256) | 最后修改人，新增字段 |
| `updated_at_utc` | timestamptz | 最后修改时间 |

设置只保存 Secret 是否已配置和 Secret 引用 ID，不保存 Secret 值。

## 3. 门户与权限映射

### `DM-PORTAL` `portal_items`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | uuid | PK |
| `owner_subject` | varchar(256) | 个人项为 Authentik Subject；公共项为空 |
| `scope` | varchar(16) | `Personal/Public` |
| `name` | varchar(80) | 必填 |
| `description` | varchar(240) | 可空 |
| `url` | varchar(2048) | 仅 http/https |
| `icon_kind` | varchar(24) | `BuiltIn/RemoteImage` |
| `icon_value` | varchar(2048) | 图标 ID 或受控 URL |
| `color` | varchar(32) | 主题安全颜色 token |
| `sort_order` | integer | 用户范围内排序 |
| `is_enabled` | boolean | 默认 true |
| `version` | bigint | 乐观并发 |
| `created_at_utc`/`updated_at_utc` | timestamptz | 时间戳 |

约束：`Personal` 必须有 `owner_subject`，`Public` 必须为空。索引：`owner_subject + is_enabled + sort_order`、`scope + is_enabled + sort_order`。

### `DM-PUBLIC-PORTAL-PREF` `public_portal_preferences`

保存用户对公共门户项的隐藏偏好：`subject`、`portal_item_id`、`is_hidden`、`updated_at_utc`，联合主键为 `subject + portal_item_id`。

### `DM-ROLE-MAP` `role_mappings`

首期仅保存管理员组映射：`id`、`authentik_group_id`、`authentik_group_name_snapshot`、`role=Administrator`、`is_enabled`、`version`、`updated_at_utc`。组名快照只用于显示，授权优先使用稳定组 ID。

不创建 `users` 表。权限快照只进入 Valkey，最长 TTL 2 小时。

## 4. 资源注册与应用

### `DM-RESOURCE` `managed_resources`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | uuid | WhaleDeck 稳定资源 ID |
| `resource_type` | varchar(48) | Container/Image/Network/Volume/Database/Application/Host 等 |
| `external_id` | varchar(512) | Docker ID、实例注册 ID 等，不存任意路径 |
| `display_name` | varchar(256) | 显示名 |
| `protection_level` | varchar(32) | `ControlPlane/IdentityCore/CriticalData/Managed` |
| `labels_json` | jsonb | 规范化标签，不含 Secret |
| `source` | varchar(32) | Agent/Template/Discovery |
| `last_seen_at_utc` | timestamptz | 最近发现 |
| `version` | bigint | 并发控制 |

唯一约束：`resource_type + external_id`。对 `labels_json` 建 GIN 索引，对 `protection_level` 建普通索引。

### `DM-APP` `application_installations`

字段：`id`、`catalog_app_id`、`template_id`、`template_version`、`display_name`、`installed_version`、`desired_version`、`state`、`auto_update_enabled`、`config_summary_json`、`installed_by_subject`、`installed_at_utc`、`updated_at_utc`、`version`。

`config_summary_json` 只存非敏感参数与 Secret 引用。关联资源使用 `application_resources(application_id, resource_id, role)`。

### `DM-APP-UPDATE` `application_update_runs`

保存旧/新镜像摘要、版本策略、计划哈希、维护窗口、任务 ID、开始/结束时间、结果、是否回滚和脱敏错误摘要。

## 5. 异步任务与计划

### `DM-JOB` `operation_jobs`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | uuid | PK |
| `job_type` | varchar(64) | 类型化任务 |
| `actor_subject` | varchar(256) | 发起人或系统主体 |
| `resource_id` | uuid | 可空，关联资源 |
| `state` | varchar(32) | Queued/Running/Waiting/Succeeded/Failed/RollingBack/RolledBack/Canceled |
| `phase` | varchar(64) | 当前稳定阶段码 |
| `progress_percent` | smallint | 可空，0–100 |
| `idempotency_key` | varchar(128) | 写入任务必填 |
| `request_json` | jsonb | 脱敏且可重试的参数 |
| `result_json` | jsonb | 脱敏结果摘要 |
| `error_code` | varchar(64) | 稳定错误码 |
| `agent_operation_id` | uuid | 可空 |
| `cancel_requested_at_utc` | timestamptz | 可空 |
| `created/started/completed_at_utc` | timestamptz | 生命周期时间 |
| `version` | bigint | 并发控制 |

唯一索引：`actor_subject + idempotency_key`（有效窗口内）；索引：`state + created_at_utc`、`resource_id + created_at_utc`。

### `DM-JOB-EVENT` `operation_job_events`

追加式记录 `job_id`、`sequence`、`state`、`phase`、`progress_percent`、`message_code`、`detail_json` 和 `occurred_at_utc`。联合主键 `job_id + sequence`，用于断线恢复任务进度。

### `DM-SCHEDULE` `scheduled_tasks`

字段：`id`、`task_type`、`name`、`schedule_kind`、`schedule_expression`、`timezone`、`parameters_json`、`concurrency_policy`、`timeout_seconds`、`is_enabled`、`next_run_at_utc`、`created/updated_by_subject`、`version` 和时间戳。

`task_type` 是预定义枚举，`parameters_json` 按类型验证，禁止 Shell 字段。运行历史存入 `scheduled_task_runs(schedule_id, job_id, scheduled_for_utc, started_at_utc, completed_at_utc, result)`。

### `DM-OUTBOX` `outbox_messages`

保存同事务产生的后台事件：`id`、`message_type`、`payload_json`、`occurred_at_utc`、`available_at_utc`、`attempts`、`processed_at_utc` 和 `last_error_code`。Worker 使用租约处理，防止数据库提交成功但任务未发布。

### `DM-RESOURCE-LEASE` `resource_operation_leases`

持久化同一逻辑资源上的写操作互斥：`lock_key`、`job_id`、`owner_id`、`acquired_at_utc`、`lease_expires_at_utc` 和 `version`。`lock_key` 为操作区域与资源稳定标识的 SHA-256 派生值，不保存敏感连接信息。Worker 每 6 秒续租，租约 30 秒失效，任务进入终态时主动释放；Worker 异常退出后其他实例可接管。PostgreSQL 是资源锁的权威存储，Valkey 清空不会解除尚未过期的写操作互斥。

## 6. 指标与资源状态

### `DM-METRIC-SERIES` `metric_series`

字段：`id`、`resource_id`、`metric_kind`、`unit`、`dimensions_json`、`created_at_utc`、`last_seen_at_utc`。唯一约束为资源、指标类型与规范化维度组合。

### `DM-METRIC-SAMPLE` `metric_samples`

字段：`series_id`、`sampled_at_utc`、`value_double`、`quality`。联合主键 `series_id + sampled_at_utc`。

- 每 6 秒采集。
- 基于 `sampled_at_utc` 使用 UTC 日范围分区；`MetricDayPartitions` 迁移保留原键、数据与读权限，事务内逐行核对后替换旧堆表。
- 对时间使用 BRIN 索引，对 `series_id + sampled_at_utc desc` 使用 B-tree。
- 原始样本保留滚动 14 天：整日过期分区直接删除，跨越截止线的唯一边界日使用时间索引做局部清理，不对整个历史父表逐行删除。此边界处理保证精确截止线，与原文“完全不逐行”的字面描述不同；仅整日删除会额外保留不足一天的数据。
- Worker 启动与每小时维护当前/未来两天分区；真实范围、名称和归属校验失败时停止维护，函数仅用调用者权限，无 PUBLIC 执行权限，无任意表名参数。
- 采集失败不写 0，使用缺口或 `quality=Unavailable`，避免图表误判。

### `DM-METRIC-ROLLUP` `metric_rollups`

保存 1 分钟/1 小时窗口的 `min/max/avg/sum/first/last/sample_count`，用于长时间图表。首期同样保留 14 天；以后延长历史时只延长 rollup，不无限保留原始样本。

当前落地范围：`metric_rollups` 已按 `window_start_utc` UTC 日分区并采用相同滚动保留策略，存储 `minimum/maximum/average/sum/sample_count` 的 1 分钟聚合。持久化 1 小时聚合与 `first/last` 扩展仍未实现；API 的 1 小时/24 小时/7 天图表当前从原始样本有界聚合，不代表这两个存储细项已经完成。时间 BRIN 由独立 `MetricTimeBrin` migration 和 EF snapshot 管理，父索引自动继承到未来分区。

即时资源状态不必每 6 秒全部写数据库；Worker 可以批量落库，Valkey 保存最近快照。

## 7. 备份

### `DM-BACKUP-POLICY` `backup_policies`

字段：`id`、`instance_resource_id`、`is_enabled`、`schedule_expression`、`timezone`、`retention_count`、`retention_days`、`target_directory_id`、`compression`、`verify_after_backup`、`capacity_warning_percent`、`capacity_critical_percent`、`version` 和更新时间。

`target_directory_id` 引用 Agent 注册目录，不保存任意绝对路径。

### `DM-BACKUP` `backup_records`

字段：`id`、`instance_resource_id`、`policy_id`、`job_id`、`status`、`relative_path`、`size_bytes`、`checksum_algorithm`、`checksum`、`started/completed_at_utc`、`verified_at_utc`、`expires_at_utc` 和 `error_code`。

恢复相关字段可以预留 migration 设计，但 `HOLD` 期间不创建可调用业务入口。

## 8. 告警

### `DM-ALERT-RULE` `alert_rules`

字段：`id`、`rule_type`、`resource_selector_json`、`threshold_json`、`evaluation_window_seconds`、`severity`、`is_enabled`、`version` 和更新时间。

### `DM-ALERT` `alert_events`

字段：`id`、`rule_id`、`resource_id`、`fingerprint`、`state`、`severity`、`occurrence_count`、`first/last_occurred_at_utc`、`recovered_at_utc`、`acknowledged_by_subject`、`acknowledged_at_utc`、`silenced_until_utc`、`related_job_id`、`summary_code` 和 `detail_json`。

`fingerprint + state=Active` 保证同一活动告警去重。状态为 `Active/Recovered/Acknowledged/Silenced`，历史状态变化写入 `alert_event_history`。

## 9. 配置仓库与外部同步

### `DM-CONFIG-SNAPSHOT` `config_snapshots`

字段：`id`、`repository_id`、`branch`、`base_commit_sha`、`result_commit_sha`、`file_manifest_json`、`diff_summary_json`、`sensitive_scan_result`、`job_id`、`created_by_subject` 和时间戳。只保存文件相对路径和摘要，不保存 Secret 文件内容。

### `DM-CONFIG-SYNC` `config_sync_runs`

字段：`id`、`repository_id`、`direction`、`before/after_commit_sha`、`state`、`job_id`、`error_code`、开始/结束时间。仓库 URL 与本地目录来自宿主机注册配置，不从数据库动态接受任意值。

### `DM-EXTERNAL-SYNC` `external_sync_runs`

用于 Authentik/GitLab 同步历史。GitLab 功能为 `HOLD`，可以延后创建此表，避免空模型提前固化。

## 10. Valkey 键空间

Valkey 使用 `noeviction`，所有临时键必须设置 TTL，并对写失败进入数据库/Agent 降级路径。

| 键模式 | TTL | 用途 |
| --- | --- | --- |
| `whaledeck:authz:{subject}` | 最长 2 小时 | 账户状态、组哈希、管理员判断和权限版本 |
| `whaledeck:overview:public` | 12 秒 | 普通用户聚合首页 |
| `whaledeck:overview:admin` | 12 秒 | 管理员完整概览 |
| `whaledeck:agent:heartbeat` | 20 秒 | Agent 心跳与能力摘要 |
| `whaledeck:resource:{id}:state` | 30 秒 | 最近容器/数据库状态 |
| `whaledeck:job:{id}:progress` | 24 小时 | 实时任务进度，最终结果仍写 PostgreSQL |
| `whaledeck:secret:{subjectHash}:{token}` | 5 分钟 | 新生成凭据的一次性交付；`GETDEL` 原子读取后删除 |
| `whaledeck:catalog:xuanyuan` | 1 小时 | 热门应用缓存 |
| `whaledeck:ratelimit:*` | 按窗口 | 登录回调和高成本查询限流 |

Valkey 清空后系统必须能从 PostgreSQL、Agent 和外部服务重建，不得影响审计和任务最终状态。

## 11. 数据保留与清理

| 数据 | 默认保留 |
| --- | --- |
| 原始指标/聚合指标 | 14 天 |
| 任务事件 | 90 天，任务主记录按策略保留 |
| 告警历史 | 180 天 |
| 审计事件 | 默认 1 年，后续由管理员配置但不能被普通界面任意清空 |
| 配置同步记录 | 1 年 |
| 备份记录 | 至少覆盖备份文件生命周期，删除文件后仍保留审计摘要 |
| 开发报告/诊断工件（待实现） | 默认 14 天，可配置且受配额；源码不按报告 TTL 删除，会话票据短时一次性 |

清理任务按小批次或分区执行，产生一条汇总日志和审计，不逐行输出日志。

## 12. 用例追踪

| 数据模型 | 主要用例 |
| --- | --- |
| `DM-AUDIT` | 所有管理写操作、`UC-AUD-*` |
| `DM-SETTING`、`DM-ROLE-MAP` | `UC-SET-*`、`UC-AUTH-004/006` |
| `DM-PORTAL`、`DM-PUBLIC-PORTAL-PREF` | `UC-PORTAL-*`、`UC-OV-001` |
| `DM-RESOURCE` | `UC-CON-*`、`UC-DB-*`、`UC-APP-*`、权限保护 |
| `DM-APP`、`DM-APP-UPDATE` | `UC-APP-*` |
| `DM-JOB`、`DM-JOB-EVENT`、`DM-OUTBOX` | 所有异步用例、`UC-JOB-*` |
| `DM-SCHEDULE` | `UC-SCH-*`、`UC-APP-008` |
| `DM-METRIC-*` | `UC-OV-002`、`UC-HOST-001` |
| `DM-BACKUP-POLICY`、`DM-BACKUP` | `UC-BKP-*` |
| `DM-ALERT-*` | `UC-ALT-*` |
| `DM-CONFIG-*` | `UC-CFG-*` |
| `DM-DEV-*`（待实现） | `UC-DEV-001..010`，具体实体/约束以开发空间专用设计为准 |
