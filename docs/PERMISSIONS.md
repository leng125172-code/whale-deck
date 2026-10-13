# WhaleDeck 权限矩阵

范围修订：2026-10-13 [P0–P5 基线](GOAL_P0_P5.md) 解除了旧 HOLD，但没有解除核心资源/默认拒绝策略。旧矩阵继续保护当前实现；新增恢复/文件/网络/终端等动作使用专用权限，P5 团队/租户委派显式授权，普通用户不会自动获得管理员能力。开发期 user 的 NOPASSWD:ALL 属于用户授权的宿主机临时环境，不授予 API/Worker/System 主体全量 sudo，也不取消产品权限校验。其撤销与密码更换由用户手动负责。

本文定义身份来源、权限动作、字段可见性、资源保护和确认等级。Authentik 是身份与用户组唯一事实来源，WhaleDeck 不维护本地密码或重复用户资料。

新增开发空间的 `PERM-DEV-*` 见 [资源级开发权限](DEVELOPMENT_WORKSPACES.md#7-权限与隔离)。管理员可配置资源和委派权限，显式授权的开发者只管理本人/被授权空间；默认 `User` 不自动变为 Developer。本人空间终端不等于宿主终端，访问个人报告也必须验证空间与 run 归属。开发期宿主 `user` 的 sudo 授权不会传播到工作负载。

## 1. 主体类型

| 主体 | 来源 | 能力 |
| --- | --- | --- |
| `Anonymous` | 无有效会话 | 登录、健康检查、Docker 维护加载页 |
| `User` | 已登录且不属于管理员组 | 普通首页、公共门户、本人门户 CRUD、本人会话与退出 |
| `Administrator` | 属于配置的 Authentik 管理员组 | 全部已启用管理功能，但仍受资源保护策略限制 |
| `System` | Worker/计划任务专用主体 | 只能执行任务类型声明的最小能力，不继承管理员全部权限 |

管理员组通过稳定组 ID 或名称配置。部署必须验证该组存在，不能把任意第一个组当作管理员组。

## 2. 权限解析与失效

1. 登录时读取 Authentik Subject、账户状态和用户组。
2. BFF 建立 HttpOnly Cookie，只保存最小会话标识和权限版本，不向浏览器保存 OIDC Token。
3. 权限快照最长缓存 2 小时；到期后重新向 Authentik 校验。
4. 用户被禁用时会话失效；移出管理员组时降为 `User`。
5. 权限下降后拒绝后续管理请求，并取消该用户尚未开始的管理任务；已进入不可取消阶段的任务按安全策略完成或回滚。
6. Authentik 不可用且缓存仍有效时可沿用旧快照；超过 2 小时后默认拒绝管理能力。
7. 每个管理 API、后台任务入队和 Agent 能力签发都必须服务端授权，不能依赖前端菜单隐藏。

2 小时窗口是已确认的可用性/即时撤权折中。需要紧急撤权时，管理员必须同时撤销 WhaleDeck 会话或停止对应用户的会话记录。

## 3. 权限动作

### 3.1 首页、门户与会话

| 权限 ID | User | Administrator | 说明 |
| --- | --- | --- | --- |
| `PERM-SESSION-READ-OWN` | 是 | 是 | 查看本人最小会话信息 |
| `PERM-SESSION-LOGOUT-OWN` | 是 | 是 | 退出本人会话 |
| `PERM-OVERVIEW-PUBLIC` | 是 | 是 | 平台聚合状态，不含敏感详情 |
| `PERM-OVERVIEW-ADMIN` | 否 | 是 | 完整主机、容器、数据库和监控概览 |
| `PERM-PORTAL-READ-OWN` | 是 | 是 | 本人和公共门户 |
| `PERM-PORTAL-WRITE-OWN` | 是 | 是 | 本人门户 CRUD/排序 |
| `PERM-PORTAL-MANAGE-PUBLIC` | 否 | 是 | 发布、修改、撤下公共门户 |

### 3.2 容器、镜像与 Docker

| 权限 ID | User | Administrator | 保护策略 |
| --- | --- | --- | --- |
| `PERM-CONTAINER-READ` | 否 | 是 | 敏感环境变量始终脱敏 |
| `PERM-CONTAINER-CREATE` | 否 | 是 | 禁止高危 runtime 参数 |
| `PERM-CONTAINER-START` | 否 | 是 | 控制面核心容器只允许维护流程 |
| `PERM-CONTAINER-STOP` | 否 | 是 | 二次确认；核心容器拒绝 |
| `PERM-CONTAINER-RESTART` | 否 | 是 | 二次确认；核心容器拒绝 |
| `PERM-CONTAINER-DELETE` | 否 | 是 | 二次确认；核心容器拒绝 |
| `PERM-CONTAINER-LOGS` | 否 | 是 | 行数、时间和大小受限 |
| `PERM-IMAGE-PULL` | 否 | 是 | 只允许批准 Registry/镜像加速 |
| `PERM-IMAGE-PRUNE` | 否 | 是 | 计划哈希、引用检查、二次确认 |
| `PERM-COMPOSE-MANAGE` | 否 | 是 | 批准目录和现有模板 |
| `PERM-DOCKER-SETTINGS` | 否 | 是 | 白名单字段、配置备份和回滚 |
| `PERM-DOCKER-RESTART` | 否 | 是 | 二次确认、Agent 持久任务、维护加载页 |

### 3.3 数据库、账号与备份

| 权限 ID | User | Administrator | 保护策略 |
| --- | --- | --- | --- |
| `PERM-DATABASE-READ` | 否 | 是 | 不返回完整凭据 |
| `PERM-DATABASE-LIFECYCLE` | 否 | 是 | 停止/重启二次确认 |
| `PERM-DATABASE-CREATE` | 否 | 是 | 参数化引擎适配器 |
| `PERM-DATABASE-DELETE` | 否 | 是 | 二次确认；系统数据库拒绝 |
| `PERM-DB-PRINCIPAL-MANAGE` | 否 | 是 | 禁止普通界面授予服务器超级管理员 |
| `PERM-DB-PERMISSION-MANAGE` | 否 | 是 | 仅引擎声明的权限模板 |
| `PERM-DB-CREDENTIAL-ROTATE` | 否 | 是 | Secret 只展示一次，不进审计 |
| `PERM-DB-CONNECTION-TERMINATE` | 否 | 是 | 系统和维护连接拒绝 |
| `PERM-BACKUP-READ` | 否 | 是 | 备份路径仅显示批准目录内相对信息 |
| `PERM-BACKUP-RUN` | 否 | 是 | 资源锁、容量检查、异步任务 |
| `PERM-BACKUP-POLICY-MANAGE` | 否 | 是 | 目标目录来自批准列表 |

数据库恢复权限不注册，直至 `HOLD` 状态被正式解除。

### 3.4 应用、主机与平台

| 权限 ID | User | Administrator | 保护策略 |
| --- | --- | --- | --- |
| `PERM-APPLICATION-READ` | 仅公共入口 | 是 | 普通用户不见安装配置 |
| `PERM-APPLICATION-INSTALL` | 否 | 是 | 现有模板、计划哈希、异步任务 |
| `PERM-APPLICATION-LIFECYCLE` | 否 | 是 | 停止/重启二次确认 |
| `PERM-APPLICATION-UPDATE` | 否 | 是 | 备份、维护窗口、失败回滚 |
| `PERM-APPLICATION-UNINSTALL` | 否 | 是 | 二次确认，明确卷保留策略 |
| `PERM-SERVICE-READ` | 否 | 是 | 普通用户只通过首页门户访问已发布入口 |
| `PERM-HOST-READ` | 否 | 是 | IP、设备、日志仅管理员 |
| `PERM-HOST-PROCESS-READ` | 否 | 是 | 列表/详情只读；不暴露命令行、环境变量或可执行路径；无结束进程动作 |
| `PERM-SYSTEMD-MANAGE` | 否 | 是 | 仅白名单 unit |
| `PERM-SYSTEM-UPDATE` | 否 | 是 | 普通更新，不自动重启 |
| `PERM-HOST-REBOOT` | 否 | 是 | 二次确认、任务排空、无关机能力 |
| `PERM-PLATFORM-DIAGNOSE` | 否 | 是 | 脱敏输出 |
| `PERM-PLATFORM-UPDATE` | 否 | 是 | 核心维护流程和回滚 |
| `PERM-CONFIG-REPOSITORY` | 否 | 是 | 批准仓库/路径、敏感检查、固定 Git 参数 |

### 3.5 任务、计划、告警与身份

| 权限 ID | User | Administrator | 说明 |
| --- | --- | --- | --- |
| `PERM-JOB-READ` | 否 | 是 | 普通用户门户操作不产生可见后台任务 |
| `PERM-JOB-CANCEL` | 否 | 是 | 仅可取消阶段 |
| `PERM-SCHEDULE-MANAGE` | 否 | 是 | 仅预定义任务类型 |
| `PERM-AUDIT-READ` | 否 | 是 | P1 支持导出 |
| `PERM-ALERT-MANAGE` | 否 | 是 | 查询、确认、静默和历史 |
| `PERM-IDENTITY-MANAGE` | 否 | 是 | 通过 Authentik API，不保存本地密码 |
| `PERM-SSO-MANAGE` | 否 | 是 | 管理批准的 Provider/应用 |
| `PERM-SETTINGS-MANAGE` | 否 | 是 | Secret 只显示配置状态 |

## 4. 字段级可见性

| 数据 | User | Administrator | 日志/审计 |
| --- | --- | --- | --- |
| 平台在线/异常/维护状态 | 可见 | 可见 | 可记录状态变化 |
| 资源数量 | 不返回 | 可见 | 聚合记录 |
| CPU/内存/磁盘/网络原始指标 | 不返回 | 可见 | 不记录逐样本日志 |
| 主机 IP、内核、设备和挂载 | 不返回 | 可见 | 错误中脱敏路径 |
| 容器名、镜像、网络、日志 | 不返回 | 可见 | 环境变量值脱敏 |
| 数据库名、账号和连接 | 不返回 | 可见 | 密码永不记录 |
| 个人门户 | 仅本人 | 本人；公共项可管理 | 记录变更，不记录访问历史默认值 |
| Authentik 姓名/邮箱 | 当前用户可见本人 | 按身份页面需要显示 | 不复制成本地资料表 |
| Secret、Token、私钥、产品密钥 | 不返回 | 仅配置状态或一次性交付 | 禁止记录 |

DTO 必须按权限构建，不得先序列化完整对象再让前端隐藏字段。

## 5. 资源保护级别

| 级别 | 示例 | 普通管理 API | 允许流程 |
| --- | --- | --- | --- |
| `ControlPlane` | WhaleDeck Gateway/API/Worker、Agent、宿主机稳定入口 | 禁止停止、删除、重建、普通自动更新 | 平台维护流程 |
| `IdentityCore` | Authentik Server/Worker 及其关键依赖 | 禁止从普通容器页破坏 | 专用应用维护流程 |
| `CriticalData` | PostgreSQL、Valkey、SQL Server、MariaDB、MongoDB 数据实例 | 禁止删除卷/绕过备份；允许数据库页面受控生命周期 | 数据库管理流程 |
| `Managed` | 普通应用和容器 | 按管理员权限操作 | 容器/应用流程 |

保护状态来自宿主机资源注册表和平台标签双重判断。建议标签为 `io.whaledeck.protected=true`，但管理员删除标签不能绕过注册表保护。`autoupdate=true` 不能覆盖保护级别。

## 6. 确认与执行等级

| 等级 | 示例 | UI | 服务端 |
| --- | --- | --- | --- |
| `L0` | 查询 | 无确认 | 权限和字段裁剪 |
| `L1` | 创建门户、启动容器、立即备份 | 明确成功/失败 | 幂等、审计 |
| `L2` | 停止/重启、应用更新、密码轮换 | 二次确认并展示影响 | 资源锁、审计、可回滚信息 |
| `L3` | 删除、Docker 设置、主机重启、平台更新、配置推送 | 二次确认、计划差异和回退方案 | 短时计划哈希、Agent 能力、持久任务、完整审计 |

二次确认不等于多人审批。首期不实现审批流或单独审批角色。

## 7. 历史 `HOLD` 权限门禁（新版已解除）

2026-10-13 之前以上能力不注册处理器；新版按完整目标实现专用权限。新增开发空间按 `PERM-DEV-*` 显式委派，不把普通用户放宽为管理员。仅保留命名空间/设计注释仍不能使 API、菜单或 Agent RPC 可访问，须待对应服务端授权与安全契约实现。

## 8. 消息中心（开发中，尚未发布）

| 操作 | 匿名 | 普通用户 | 当前管理员 |
| --- | --- | --- | --- |
| 消息列表/摘要 | 拒绝 | 本人 Subject 消息 | 本人 Subject 消息 + 管理员广播 |
| 单条/快照已读 | 拒绝 | 仅本人可见消息的本人读状态 | 仅本人读状态，不影响其他管理员 |
| 管理资源详情 | 拒绝 | 无管理跳转 | 仍由目标端点再次授权 |
| 任意发送/编辑事实 | 无入口 | 无入口 | 无入口 |

身份由服务端 Authentik 目录解析，不使用请求体角色。越权或过期 ID 与不存在 ID 统一 404；退出/降权清空前端，失效摘要不显示假零，未知/晚到响应不补回私有数据。详见 [消息中心](NOTIFICATIONS.md)。
