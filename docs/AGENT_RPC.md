# WhaleDeck Agent RPC 契约

范围修订：2026-10-13 [P0–P5 基线](GOAL_P0_P5.md) 增加受控终端会话/文件/恢复/网络/网站/邮件/DNS/多节点适配。源码1.17通知私有配置、1.18宿主PTY基础，保留本地UDS限制，不得直接放宽成任意路径/可执行程序RPC；新增能力须独立设计、能力协商和安全测试。P5远程Agent采用专用mTLS通道，本地UDS不暴露TCP；远程写入须同时验证节点/资源/主体/短时capability。

本文定义 `WhaleDeck.Api/Worker` 与宿主机 `WhaleDeck.Agent` 的内部契约。Agent 是唯一可以访问 Docker Engine、systemd、journald 和批准宿主机资源的组件。

开发空间拟新增专用 `RPC-DEV-001..007`，只处理登记空间/环境/run/session ID，按能力协商启用。代码执行和调试发生在隔离空间内，不新增任意宿主命令/路径接口；调试适配器与 IDE 不持有 Docker/Agent Socket。契约与数据流见 [开发空间 Agent 契约](DEVELOPMENT_WORKSPACES.md#6-agentworker-契约)。此处是待实现设计，**源码协议 1.17 未包含开发空间能力**。

## 1. 传输与版本

- 协议：gRPC，包名 `whaledeck.agent.v1`。
- 传输：Unix Domain Socket `/run/whaledeck/agent.sock`，不监听 TCP。
- Socket：`whaledeck-agent:whaledeck`、`0660`，由非 root 的 Agent 服务创建；仅 Agent 与 API/Worker 的专用宿主机组可以连接。能力密钥与注册表仍由 root 所有且 Agent 只读。
- Agent 使用 Unix peer credentials 校验调用进程，并验证短时签名能力声明。
- `GetCapabilities` 返回 Agent 版本、协议版本和方法能力；未知主版本直接拒绝，次版本按能力协商。
- 浏览器不得连接 Agent。宿主机稳定入口只读取 Agent 原子写入的脱敏维护快照。
- 当前源码协议版本为 `1.17`，新增 `notifications.private-configuration`。历史 `1.4` 增加普通 Docker 网络与孤立卷管理，`1.5` 增加有界 Docker 事件缓冲与时间线能力 `docker.events`，`1.6` 将数据库、备份、Compose、主机与平台维护预检分流到 `ManagedResourceService.PlanAction`，`1.7` 增加只读进程能力 `host.processes`，`1.8` 扩展容器摘要（`docker.container-summary`），`1.9` 增加 `host.disk-io`，`1.10` 增加 `docker.usage`，`1.11` 增加兼容只读字段 `docker.relationships`，`1.12` 增加 `applications.configuration`，`1.13` 增加 `applications.backups`，`1.14` 增加 `applications.backup-retention`，`1.15` 增加 `backups.capacity`，`1.16` 增加 `host.load-average`；旧次版本按能力协商并安全降级。

### 通知私有配置（1.17）

`notifications.private-configuration` 增加独立 `NotificationConfigurationService/Inspect/Apply`。Inspect 接受渠道 UUID 和可选操作 UUID，只返回别名/布尔/不透明版本及原操作回执；`binding_current=false` 表示已被后续变化取代。Apply 的私有字节只经 UDS 和 helper stdin，在有界校验后送固定 root writer，不能进入 OperationStore/任务/审计或命令参数。根回执和绑定同一个原子文件提交，可在输入暂存过期后查询确认；最终 Application 永久幂等和 PG 一致性仍须实现。

固定 helper 用短时 systemd unit 逃离 Agent 只读挂载命名空间，writer 自身保持 ProtectSystem=strict，只有通知私有目录可写、无网络 socket；不扩大 Agent 的 ReadWritePaths。详见 [实现与验收状态](qa/NOTIFICATION_PRIVATE_CONFIGURATION.md)。源码与已部署运行时须按该证据区分；其他原有 1.16 能力保持兼容。

### 主机负载与状态（1.16）

现有 `HostService.StreamMetrics` 有界批次增加 `load.average.1/5/15`（tasks）和 `load.normalized`（percent）；后者是一分钟负载除以逻辑核心数，不是 CPU 使用率，允许超过 100%。缺失/非法 `/proc/loadavg` 返回 Unavailable 质量。沿用 6 秒采样、UDS/短时 capability、14 天指标保留；不新增宿主路径、命令或 TCP 方法。

`ListSystemdUnits` 仍仅注册单元；状态按 `ActiveState/SubState/LoadState/UnitFileState` 命名属性读取，不依赖输出次序。现有 Docker 原生 summary `Status` 与进程 State 共同用于健康判定；starting/未知/失联不能解除风险，未配置健康检查不等于健康检查通过。详见 [状态与负载验收](qa/HEALTH_ALERTS.md)。

### 备份目标容量（1.15）

`ManagedResourceService.GetBackupTargetCapacity(Empty)` 仅返回批准 HDD 目标的 logical ID、可用标志、总/已用/可用字节、采样 UTC 时间及稳定错误码，不接受任意路径。Unix peer credentials 与绑定方法的签名 capability 仍生效。固定 root helper 无参数只读查询，验证目录无 symlink、与 HDD 基目录同文件系统；Agent 缓存 30 秒以限制 sudo 日志，客户端拒绝超过 90 秒或非法容量样本。

Worker 按策略创建只读 `BackupStorageCapacity` 规则，告警去重、升级、静默到期及恢复写现有表。未知容量不能恢复或降低已有临界风险，低于 256 MiB 空闲触发临界；禁用策略可显式收尾告警。该规则不能由通用告警编辑入口覆盖。存储模型不变，本阶段无新 migration；真实隔离库仍通过完整 migration 验证。

### 应用归档保留（1.14）

`applications/cleanup-backups` 使用既有计划/执行 RPC，参数仅允许 slug、retentionDays（1–3650）和 retentionCount（1–365），计划绑定 root 所有的已校验归档集合。清理不会改变应用运行数据，始终保留最新一份；普通停止/删除保护不能通过清理入口绕过。清理也使用 API job UUID 对应的独立 systemd 单元，删除每对归档/元数据之前先持久化 intent；恢复仅收尾已经开始的一对，不擅自继续其余候选。终态结果携带真正删除的 backup ID，Worker 在成功、失败或取消时按原资源更新 Expired 历史，不删除审计记录。

应用策略强制 gzip/SHA256，启用需管理员确认，并持久化 live/quiesced 一致性。Worker 调度及备份完成后分别安排有幂等键的清理；签发前检查策略仍启用且版本一致，并生成新计划，不复用排队前的过期计划。

### 应用归档（1.13）

`applications/backup` 与 `applications/verify-backup` 使用既有 `ManagedResourceService.PlanAction/RunCompose`、短时 capability、确认、资源锁和单次计划。范围固定为批准的普通应用配置、data bind 与独占本地卷，禁止核心与共享资源；预检使用私有 HMAC，不返回 Secret 值或其公开散列。默认 quiesced 仅提供文件系统暂停，不保证数据库事务一致性；不提供恢复或下载。

备份由 UUID 绑定的独立、限时 systemd 服务执行。应用备份/校验的 Agent operation ID 固定等于 API job ID，Worker 在发送 RPC 前持久化它，防止响应丢失或并发取消保存冲突造成“尚未执行”的误判。Agent 保存恢复标记，重启后只观察原服务，不重启或复制任务。取消须等待原服务停止且暂停成员恢复；已完成归档可优先于迟到的取消。ExecStopPost 与定时恢复使用项目 flock，不能干扰存活备份。Worker 将归档结果写入 BackupRecord，校验任务只更新原记录的校验状态，不写应用更新历史。

### 应用参数编辑（1.12）

当前实现增加 `applications.configuration` 能力与 `ManagedResourceService.GetApplicationConfiguration`。快照只返回结构化资源、现有环境变量名称及命令覆盖存在标记，不返回环境值、Cmd 或 Entrypoint 原文。

`applications/configure` 预检由 API 所有者绑定短时票据提供候选；计划绑定源文件、服务成员、候选内容、已缓存镜像与批准网络。执行消费一次票据并再次核对指纹。仅保存不重启；保存并重部署禁止隐式 build/pull，数据卷不自动清理。核心资源、任意宿主路径与受保护网络不能通过此入口修改。配置预检使用清空继承环境后的固定 CLI 环境，文件权限 `0600`。

### 容器资源关联（1.11）

既有 `InspectContainer` 增加可选 `relationships`，包含实际 image ID、Compose project/service、network mode、结构化网络端点、挂载与端口映射。网络最多 128，挂载/端口各最多 256，单字符串最多 4096 字符，所有文本合计最多 262144 字符；超限标记 `is_truncated`。只投影固定字段，不返回 Env 值、driver options 或原始 Config。宿主机映射配置不代表正在监听；运行时已解析端口优先，停止容器可退回配置值，未指定 host port 展示自动分配。

容器摘要同时增加实际 image ID、named volume names、network IDs，供关联资源页精确反向筛选，无逐容器 inspect N+1 请求。旧 Agent 的可选消息为空，UI 明确提示不能伪造“没有关联”。Compose labels 仅供导航，不能赋予管理权；只有注册表认可的 ComposeApplication 才显示应用详情，保护规则仍由注册表与标签双重执行。

### 容器资源采样（1.10）

`DockerService.GetContainerUsage(Empty)` 返回 `ContainerUsageBatch`，只读缓存，不触发 Engine 采样。Agent 每 6 秒通过 Docker Engine `stream=false&one-shot=true` 采样；每轮总预算 5 秒、最多 8 个并发请求、最多 256 个容器，并返回总数、截断及跳过计数。超过 18 秒未更新标记 stale，前端同时检查每个容器自身时间戳。

CPU 用相邻原生计数器差值计算，100% 代表一个逻辑核心（可超过 100%）。首次、重启、计数器重置、无效或超长窗口返回 Warmup/null。内存扣除 cgroup 可回收文件缓存；缺失字段保持 null。停止容器返回 Stopped/null，读取失败返回 Unavailable/null。网络与块 I/O 是累计字节而非速率。RPC 不推进计数器基线，不启动逐用户/逐页面 Docker 统计流。

Worker 将同一缓存批次的 CPU、内存、累计网络/块 I/O 和 PID 指标批量写入现有指标存储；按资源、设备、类型和 PostgreSQL 微秒时间戳去重，权限仍限管理员。没有成功逐样本 Info 日志。

## 2. 通用消息

### 2.1 请求上下文

所有非健康检查 RPC 必须包含：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `request_id` | UUID | 每次调用唯一，用于日志关联 |
| `trace_id` | string | 贯穿 HTTP、任务、RPC 和审计 |
| `actor_subject` | string | Authentik Subject；系统任务使用固定系统主体 |
| `job_id` | UUID? | 异步任务 ID |
| `idempotency_key` | string? | 写操作必填，同动作/资源重复提交返回原结果 |
| `issued_at_utc` | timestamp | 签发时间 |
| `expires_at_utc` | timestamp | 最长 60 秒，过期拒绝 |
| `capability` | bytes | 签名后的动作、资源和权限声明 |

能力声明至少约束 `action`、`resource_id`、`job_id` 和有效期。Agent 不根据前端传来的角色字符串自行推断权限。

### 2.2 资源引用

```text
ResourceRef {
  resource_type
  resource_id
  expected_version?
}
```

`resource_id` 必须来自 Agent 的资源注册表。涉及宿主机路径时，API 只发送资源 ID 或挂载 ID，Agent 从本地只读注册表解析实际路径；不得接收任意绝对路径。

### 2.3 异步操作

写 RPC 返回：

```text
OperationRef {
  operation_id
  job_id
  state
  phase
  progress_percent?
  accepted_at_utc
}
```

状态为 `Queued`、`Running`、`WaitingForHealth`、`Succeeded`、`Failed`、`RollingBack`、`RolledBack` 或 `Canceled`。Agent 在 `/var/lib/whaledeck-agent/operations` 原子保存操作状态和非敏感恢复数据；Docker、API、PostgreSQL 或 Valkey 重启不能丢失操作。

### 2.4 错误码

| 代码 | 含义 |
| --- | --- |
| `INVALID_ARGUMENT` | 字段、枚举、资源组合或计划哈希无效 |
| `PERMISSION_DENIED` | 能力声明无权执行该动作 |
| `RESOURCE_PROTECTED` | 目标为受保护资源，普通流程禁止操作 |
| `RESOURCE_CONFLICT` | 资源锁、端口、名称、挂载或并发操作冲突 |
| `PRECONDITION_FAILED` | 健康、磁盘空间、版本或计划哈希不满足 |
| `NOT_FOUND` | 已批准资源不存在；不返回额外路径信息 |
| `UNAVAILABLE` | Docker/systemd/依赖服务暂不可用 |
| `OPERATION_NOT_CANCELABLE` | 当前阶段不能安全取消 |
| `ROLLBACK_FAILED` | 原操作和回滚均失败，需要人工介入 |

Agent 错误只返回安全摘要和诊断 ID，不返回完整环境变量、命令行、Secret 或配置文件内容。

## 3. Agent 与操作状态

| RPC ID | Service/Method | 类型 | 调用方 | 说明 |
| --- | --- | --- | --- | --- |
| `RPC-AGT-001` | `AgentService.GetCapabilities` | Unary | API/Worker | 版本、协议、平台和支持能力 |
| `RPC-AGT-002` | `AgentService.GetHealth` | Unary | API/Worker | Agent、Docker、systemd 和本地存储状态 |
| `RPC-AGT-003` | `AgentService.WatchHealth` | Server stream | Worker | 心跳和能力变化，低频发送 |
| `RPC-OPS-001` | `OperationService.GetOperation` | Unary | API/Worker | 查询持久操作状态 |
| `RPC-OPS-002` | `OperationService.GetMaintenanceStatus` | Unary | API | Docker/主机/自身维护阶段和结果 |
| `RPC-OPS-003` | `OperationService.CancelOperation` | Unary | API | 在声明为可取消的安全阶段取消 |
| `RPC-OPS-004` | `OperationService.WatchOperation` | Server stream | API/Worker | 任务阶段变化；断线后可按游标恢复 |

Agent 每次维护状态变化都原子写入 `/run/whaledeck/maintenance/status.json`。该文件只包含状态、阶段、时间和安全错误码，由宿主机稳定入口提供给维护加载页。

## 4. 主机、设备与指标

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-HOST-001` | `HostService.GetHostInfo` | Unary | 主机名、发行版、内核、架构、启动时间、CPU/内存摘要 |
| `RPC-HOST-002` | `HostService.StreamMetrics` | Server stream | 每 6 秒发送 CPU、内存、负载、网络和磁盘 I/O 采样 |
| `RPC-HOST-003` | `HostService.ListNetworkInterfaces` | Unary | 网卡、地址、链路、速率、路由摘要和累计流量 |
| `RPC-HOST-004` | `HostService.ListStorageDevices` | Unary | 磁盘、分区、文件系统、挂载、inode 和 SMART 摘要 |
| `RPC-HOST-005` | `HostService.RebootHost` | Operation | 管理员确认后重启；拒绝关机请求 |
| `RPC-HOST-006` | `HostService.ListProcesses` | Unary | 有界进程快照、CPU/内存/PID/名称排序、过滤及分页 |
| `RPC-HOST-007` | `HostService.GetProcess` | Unary | 通过 PID 与启动身份读取当前只读详情；PID 复用返回不存在 |

进程采样每 6 秒一次，不记录成功采样日志。CPU 根据单调时间和累计 CPU 时间差分，100% 为一个逻辑核心；首次采样或新进程返回 `null`，不伪造 0%。返回采样时间、窗口、18 秒过期标记、截断标记与跳过数量；采样上限 32768，每页 1–500，过滤最长 128。只返回 PID、父 PID、UID、名称、状态、线程、启动时间和 CPU/内存统计，不读取或返回 argv、环境变量及可执行路径。详情的 `instance_id` 为 `pid:启动时间UTC ticks`，不能用新进程替代已退出实例。API 在未知主版本或未声明能力时返回 503 `AGENT_CAPABILITY_UNSUPPORTED`，不降级为伪空列表。

名称与内存直接来自固定 status 字段，不使用会间接读取 cmdline 的框架名称/进程信息补全路径。名称为内核 `Name`，可能按内核规则截短；内存按 kB 转字节，内核线程缺失地址空间字段时为 0，格式错误或整数溢出时跳过而非伪造值。

指标使用单调计数器计算速率，包含采样起止时间和设备稳定 ID。Worker 负责聚合、降采样和 14 天清理，Agent 不为每次采样写 Info 日志。

`1.9` 的主机指标后台采样器独立于 RPC 查询：同一批次共享采样时间，多个页面/API/Worker 读取不会推进计数器基线。首次采样、计数器重置返回 `Warmup`；磁盘无完成 I/O 时延迟返回 `Idle`，API 将非 `Good` 值投影为 `null`，不把缺失或空闲画成零延迟。仅汇总实际块设备，不重复计算分区、device-mapper 和虚拟磁盘。

新增 `disk.read.iops`、`disk.write.iops`（operationsPerSecond）及 `disk.read.latency`、`disk.write.latency`（milliseconds）。磁盘速率按 sector 差值 × 512 / 单调时间差；延迟按读/写累计毫秒差 / 完成 I/O 数差，表示内核请求完成耗时而非磁盘设备自身纯服务耗时。历史延迟按匹配 IOPS 加权。字段依据 [Linux I/O statistics](https://docs.kernel.org/admin-guide/iostats.html)。

`GET /api/v1/metrics` 的 `window=live|1h|24h|7d` 为管理员只读接口；历史分别按 30/300/1800 秒聚合，返回 121/289/337 个对齐时间桶和 `windowStartUtc/windowEndUtc/resolutionSeconds`，当前桶可能未结束。无采样桶为 `Missing/null`。实时保持最多 600 个原始点的旧 `take` 兼容范围；Vue 使用固定 6 分钟 61 槽。数据库端有界聚合，不把 7 天原始样本全部加载到 API 内存。UTC 日分区及按分区 14 天保留已有专用 migration/SQL 与历史验收；分钟/小时与迟到回填见 [聚合证据](qa/METRIC_AGGREGATION.md)，不能把此子项已验证当作整个监控或 P0–P5 已完成。

## 5. systemd、日志与系统更新

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-SVC-001` | `SystemdService.ListUnits` | Unary | 白名单范围内 unit 状态和启动方式 |
| `RPC-SVC-002` | `SystemdService.ChangeUnitState` | Operation | 启动、停止、重启白名单 unit；核心 unit 使用专用流程 |
| `RPC-SVC-003` | `SystemdService.ListTimers` | Unary | WhaleDeck 注册 timer、上次/下次运行和结果 |
| `RPC-LOG-001` | `JournalService.Query` | Unary | 按 unit、级别、时间和游标查询，限制行数/响应大小 |
| `RPC-LOG-002` | `JournalService.Follow` | Server stream | 受限实时日志；断线和慢消费者主动结束 |
| `RPC-UPD-001` | `UpdateService.Check` | Operation | 检查可用普通系统更新和待重启状态 |
| `RPC-UPD-002` | `UpdateService.Install` | Operation | 执行普通更新，不自动重启主机 |
| `RPC-UPD-003` | `UpdateService.GetTimerStatus` | Unary | 周日 20:00 自动更新 timer 状态 |

Agent 不提供“执行命令”“执行脚本”或任意 unit 名称接口。

## 6. Docker 查询与容器操作

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-DKR-001` | `DockerService.ListContainers` | Unary | 状态、健康、镜像、项目、网络和保护标记 |
| `RPC-DKR-002` | `DockerService.InspectContainer` | Unary | 规范化详情，敏感环境变量值脱敏 |
| `RPC-DKR-003` | `DockerService.GetContainerLogs` | Unary | 时间、尾部行数和响应大小有上限 |
| `RPC-DKR-004` | `DockerService.GetContainerStats` | Unary | 单次读取 CPU、内存、网络、块 I/O 和进程数 |
| `RPC-DKR-005` | `DockerService.GetEngineInfo` | Unary | Engine/API 版本、存储和日志驱动 |
| `RPC-DKR-006` | `DockerService.PlanCreateContainer` | Unary | 只读校验，返回计划哈希、冲突和资源摘要 |
| `RPC-DKR-007` | `DockerService.CreateContainer` | Operation | 必须携带未过期计划哈希 |
| `RPC-DKR-008` | `DockerService.StartContainer` | Operation | 受保护策略校验 |
| `RPC-DKR-009` | `DockerService.StopContainer` | Operation | 二次确认后执行；核心容器拒绝 |
| `RPC-DKR-010` | `DockerService.RestartContainer` | Operation | 二次确认后执行；核心容器拒绝 |
| `RPC-DKR-011` | `DockerService.DeleteContainer` | Operation | 默认保留卷和镜像；核心容器拒绝 |
| `RPC-DKR-012` | `DockerService.PullImage` | Operation | 只允许批准 Registry/镜像加速配置 |
| `RPC-DKR-013` | `DockerService.PlanPrune` | Unary | 返回引用关系和预计回收空间 |
| `RPC-DKR-014` | `DockerService.Prune` | Operation | 必须携带计划哈希，跳过受保护资源 |
| `RPC-DKR-015` | `DockerService.ListImages` | Unary | 摘要、大小、标签和引用容器 |
| `RPC-DKR-016` | `DockerService.ListNetworks` | Unary | 网络、范围、子网和关联容器 |
| `RPC-DKR-017` | `DockerService.ListVolumes` | Unary | 卷、挂载、关联容器和孤立状态 |
| `RPC-DKR-018` | `DockerService.InspectImage` | Unary | 读取或按请求拉取 OCI 镜像声明，返回 Env、Ports、Volumes、Entrypoint、Cmd 与 Labels |
| `RPC-DKR-019` | `DockerService.ListEvents` | Unary | 返回 Agent 订阅并脱敏、去重、限长后的近期 Docker 事件 |

创建 DTO 使用类型化字段，不接受 Docker CLI 参数字符串。首期禁止通过创建页面启用 `privileged`、宿主机 PID/IPC namespace、任意设备或未批准的宿主机路径。

## 7. Docker 配置与 Compose

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-DCFG-001` | `DockerConfigService.Get` | Unary | 返回支持字段、脱敏配置摘要和配置版本 |
| `RPC-DCFG-002` | `DockerConfigService.Validate` | Unary | 合并未知字段、原生校验、返回差异/影响/计划哈希 |
| `RPC-DCFG-003` | `DockerConfigService.ApplyAndRestart` | Persistent operation | 备份、原子写入、重启、健康等待和自动回滚 |
| `RPC-CMP-001` | `ComposeService.ListProjects` | Unary | 识别已有项目、服务、配置来源和目录 ID |
| `RPC-CMP-002` | `ComposeService.PlanDeployment` | Unary | 模板解析、差异、依赖、卷和计划哈希 |
| `RPC-CMP-003` | `ComposeService.Deploy` | Operation | 使用现有模板部署或更新 |
| `RPC-CMP-004` | `ComposeService.ChangeProjectState` | Operation | 按依赖顺序启动、停止、重启 |
| `RPC-CMP-005` | `ComposeService.RemoveProject` | Operation | 明确保留/删除数据卷 |

`ApplyAndRestart` 必须在 API 消失后继续运行。核心容器更新不走普通 Compose RPC，而走平台维护服务。

## 8. 数据库与备份

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-MGD-001` | `ManagedResourceService.PlanAction` | Unary | 对注册资源执行只读预检并签发短时、单次计划哈希；不会解释为 Docker 容器创建 |
| `RPC-DB-001` | `DatabaseService.ListInstances` | Unary | 注册实例、引擎、版本、健康、容量和存储摘要 |
| `RPC-DB-002` | `DatabaseService.ListResources` | Unary | 数据库/Schema、账号/角色、权限和连接摘要 |
| `RPC-DB-003` | `DatabaseService.CreateDatabase` | Operation | 引擎适配器参数化创建 |
| `RPC-DB-004` | `DatabaseService.DeleteDatabase` | Operation | 二次确认，禁止系统数据库 |
| `RPC-DB-005` | `DatabaseService.CreatePrincipal` | Operation | 生成密码、最小权限；结果 Secret 只交付一次 |
| `RPC-DB-006` | `DatabaseService.ChangePrincipalState` | Operation | 禁用/启用/删除账号或角色 |
| `RPC-DB-007` | `DatabaseService.GrantPermissions` | Operation | 只接受引擎定义的权限枚举 |
| `RPC-DB-008` | `DatabaseService.RotateCredential` | Operation | 轮换并一次性交付新 Secret |
| `RPC-DB-009` | `DatabaseService.ListConnections` | Unary | 返回脱敏连接元数据 |
| `RPC-DB-010` | `DatabaseService.TerminateConnection` | Operation | 禁止终止系统/维护连接 |
| `RPC-BKP-001` | `BackupService.Run` | Operation | 调用注册备份实现，写入 HDD 临时文件后原子完成 |
| `RPC-BKP-002` | `BackupService.Verify` | Operation | 校验文件完整性和引擎可读性 |
| `RPC-BKP-003` | `BackupService.Cancel` | Unary | 仅在安全阶段取消并清理临时文件 |

数据库适配器映射到预注册实例与实现，不接受任意 SQL、JavaScript、Shell 或容器名。数据库恢复 RPC 保留编号空间但不注册服务。

## 9. 配置仓库与平台维护

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-CFG-001` | `ConfigRepositoryService.GetStatus` | Unary | 批准仓库、分支、提交、工作区和同步状态 |
| `RPC-CFG-002` | `ConfigRepositoryService.PlanSnapshot` | Unary | 文件清单、差异、敏感检查和计划哈希 |
| `RPC-CFG-003` | `ConfigRepositoryService.CommitAndPush` | Operation | 只提交批准文件，固定 Git 参数，记录提交 SHA |
| `RPC-MNT-001` | `PlatformMaintenanceService.RunDiagnostics` | Operation | 检查 Agent、Docker、核心服务、网络、磁盘和时间 |
| `RPC-MNT-002` | `PlatformMaintenanceService.PlanUpdate` | Unary | 镜像摘要、配置、迁移兼容、空间和回退能力 |
| `RPC-MNT-003` | `PlatformMaintenanceService.ApplyUpdate` | Persistent operation | 编排核心更新并始终保留稳定入口 |
| `RPC-MNT-004` | `PlatformMaintenanceService.Rollback` | Persistent operation | 回退镜像/配置；不执行不安全的数据库降级 |
| `RPC-MNT-005` | `PlatformMaintenanceService.BuildDiagnosticBundle` | Operation | 生成有大小/时效限制的脱敏诊断包 |

配置仓库 RPC 不接受任意仓库 URL、本地路径、提交参数或 Git 命令。仓库和允许目录由宿主机配置注册。

## 10. 超时、流控与日志

- 普通 Unary 查询默认 10 秒；慢查询必须分页或转任务。
- 流式日志和统计必须支持取消、最大持续时间、最大消息大小和慢消费者保护。
- Agent 只在状态变化、失败和管理操作时记录结构化日志；6 秒指标不写逐样本 Info 日志。
- 所有 Secret 字段在 protobuf 中使用明确类型，日志拦截器默认丢弃这些字段。
- Agent 重启后扫描未完成操作：可恢复则继续，不可恢复则标记失败并给出安全诊断码。

## 11. Compose 项目能力补充

现有 ManagedResourceService 宣告 compose.projects 能力，沿用协议 1.7 和现有 wire 消息：

- ListComposeProjects 同时发现 Docker Compose 项目与批准目录中的安装；未注册项目只读，核心资源按注册表和标签双重保护。
- PlanAction 使用 resource_type=compose-projects、resource_id=application:<slug>，动作允许 start/stop/restart/pull/redeploy。不接受客户端路径、命令、配置或额外参数。
- 预检比较 Compose 原生服务 hash 与现有容器标签，只输出服务级差异与受影响容器数量，不输出规范化配置、环境变量或 Secret。计划绑定配置输入及容器成员指纹，五分钟内一次性消费。
- RunComposeAction 执行前再次验证指纹、项目名称和受保护容器名称；启停/重启、拉取与重部署全部要求计划和管理员确认。与应用生命周期共享资源锁。
- pull 不重启服务；redeploy 不隐式拉取或构建镜像。孤立服务、配置和数据卷不自动删除；启动/重启/重部署等待所有目标服务健康。
- 当前提供服务级配置变化摘要，不提供原文编辑或逐字段 diff；项目移除和数据卷选择继续使用现有应用卸载用例。Git 同步属于独立后续能力。
