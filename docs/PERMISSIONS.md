# WhaleDeck 权限矩阵

2026-10-21范围覆盖：当前为[单工作站59项](SCOPE_SINGLE_WORKSTATION.md)，不建立企业tenant、节点角色、订阅或邀请体系。保留Authentik管理员/普通用户、本人门户，以及按Subject/稳定组ID授予的开发空间动作；Worker最小授权、二次确认、资源归属和跨用户拒绝不变。下面P5租户描述是旧范围，不作为新权限实施要求；开发期sudo不传播到产品。

正文搜索 **5e131e9/RPC1.29已安装签收**：真实管理员Cookie/CSRF/Origin与实际UDS读取、独立非管理员403、匿名401、缺CSRF400/外源403和保护项403通过；源码双根授权/返回前管理员复核测试保持。320/1440明暗真实网页通过且无管理写请求，见[最终证据及测试修正](qa/FILE_CONTENT_SEARCH.md)。以下“源码候选/未部署”为历史，不据此放宽其他文件功能或宣告全部76项完成。

正文搜索1.29源码候选（尚未部署）：匿名与普通用户拒绝；当前管理员通过BFF读前/返回前两次授权，身份过期或降权不返回片段。`POST /api/v1/files/content-search`是仅查询的POST，需要Cookie、CSRF和同源Origin，不生成写任务/幂等记录或自由命令。根目录必须同时声明`files-list`与`files-download`，不能靠仅列表/仅下载获得正文遍历。前端按登记动作显示正文页签，普通用户不挂载管理页；两类搜索切换/变更根目录/取消时清除迟到私有结果。源码能力不等于实机签收，见[证据](qa/FILE_CONTENT_SEARCH.md)。

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

数据库恢复已经纳入 P2-08，不再受旧 `HOLD` 排除；专用权限/计划/目标覆盖确认与安全实现完成前仍不得注册可调用恢复入口，不能借通用备份权限绕过恢复边界。

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

## 8. 消息中心（站内已签收，新快照与外部投递待签收）

| 操作 | 匿名 | 普通用户 | 当前管理员 |
| --- | --- | --- | --- |
| 消息列表/摘要 | 拒绝 | 本人 Subject 消息 | 本人 Subject 消息 + 管理员广播 |
| 单条/快照已读 | 拒绝 | 仅本人可见消息的本人读状态 | 仅本人读状态，不影响其他管理员 |
| 管理资源详情 | 拒绝 | 无管理跳转 | 仍由目标端点再次授权 |
| 任意发送/编辑事实 | 无入口 | 无入口 | 无入口 |
| 渠道/路由元数据及投递记录 | 拒绝 | 拒绝 | `/notifications/manage` 服务层再次校验 |

配置写入必须 CSRF、Origin、UUID 幂等键、二次确认和预期版本；凭据值及其引用不返回客户端。路由变化提升父渠道版本并作明确影响提示；历史投递阻止渠道删除。见 [通知配置契约](qa/NOTIFICATION_MANAGEMENT.md)。这些端点不能任意发送消息。

身份由服务端 Authentik 目录解析，不使用请求体角色。越权或过期 ID 与不存在 ID 统一 404；退出/降权清空前端，失效摘要不显示假零，未知/晚到响应不补回私有数据。详见 [消息中心](NOTIFICATIONS.md)。

## 2026-10-27 容器历史内容恢复权限

历史和恢复五路API仅当前Authentik管理员，服务额外检查原BFF登录存活；普通/匿名拒绝由HTTP集成覆盖，真实正常管理员全链已通过，更多真人拒绝/中断门禁继续。计划与确认绑定原subject/login、当前CID/原始纳秒instance和目标版本、完整来源捕获摘要。恢复专用状态/取消仅原登录；另一登录不可借原计划重放。现任管理员只能走已有任务取消或Unknown接管流程，不伪造原登录。

API/Worker只访问UDS和短时签名，minor42及history/source能力必需；Worker接收前、接收中、Ready前、发布前复核权限/取消/租约。来源必须经正常RPC EOF全验证，浏览器不能提供恢复内容，通用任务重试拒绝。核心/敏感路径/别名与实例保护继续由Agent/Root复核。

2026-10-27：允许同容器其他路径内容恢复，来源读取权限不扩大至其他CID。新目标仍执行原路径/当前实例/核心/敏感/链接与目标版本保护。实际普通OIDC登录对历史、恢复计划/登记/状态/取消均403，匿名历史401；原登录归属和进行中撤权/重启更广组合继续。

## 容器复制权限（2026-10-28 候选，实际身份验收待完成）

| 入口 | 当前身份要求 | 原身份/其他约束 |
| --- | --- | --- |
| 登记预检、按 ID/key 查询、确认、取消 | 当前 Authentik 管理员且登录有效 | 原 subject + login；容器/命令/计划不变；确认必须显式、未过期且版本匹配 |
| 后台扫描/Apply | 原用户仍为管理员，原登录有效 | 每6秒及最终等待后复核，期限与租约有效；发布保护先持久化，再签原执行 |
| 后台 Inspect/Discard 对账 | 可信 Worker 的原绑定 | 只观察/封闭原 ID；原用户撤权不抹去历史结果，也不授予新 Apply |
| Unknown 元数据/接管 | 当前管理员且登录有效，可与原用户不同 | 显式二次确认 + 并发版本 + 原 Root 永久栅栏；保留原 actor/owner/意图及可能存在的私有内容 |

普通用户不能调用上述管理员 API；匿名请求遵循现有 API 401 规则。Cookie 写请求保留全局 CSRF/Origin 及唯一幂等键。
当前已验证业务/模型/SQL，尚未把这些检查声明为本批真实身份网页验收；见 [边界及证据](qa/CONTAINER_COPY_PERSISTENCE.md)。
