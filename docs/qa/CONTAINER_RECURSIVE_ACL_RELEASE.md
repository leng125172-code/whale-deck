# 递归 ACL 业务与页面候选的标准覆盖、生产迁移和安装核验

2026-11-04，发布冻结源码 `c020c245f954e7a5b8e3a4b6d022e929c35794d7`。承接[管理员 API/Vue 候选](CONTAINER_RECURSIVE_ACL_API_UI.md)、[持久化](CONTAINER_RECURSIVE_ACL_PERSISTENCE.md)及[1.53 安装态签名 RPC 分项](CONTAINER_RECURSIVE_ACL_AGENT.md)。**本批已完成标准覆盖和以下技术核验；尚未完成新递归 ACL 的真实 Authentik→Vue 预检/确认→Worker→Agent→文件/HDD 页面整链。** 不提升 59 项主任务或整个阶段的完成率。

## 准确 CI、来源与唯一发布

[CI 41762751191](https://github.com/leng125172-code/whale-deck/actions/runs/41762751191) 的 head SHA 逐字匹配 c020c24，主运行 completed/success；backend、installer、native-postgres、native-valkey、frontend_checks、六路 frontend-browser 与 frontend 聚合共 12 路均 completed/success。最初主运行查询有一次网络 EOF，未将失败当成成功；连接工具的 12 路回执与随后主运行成功回执均保留。未更换 TLS 或重复构建来掩盖失败。

开发仓库当时为干净的文档提交 `1a07455c540bbb87e09c017d5365c02062574e8d`。运行仓库由 user 从 28062da **仅向前快进至已取得完整 CI 的 c020c24**；没有 reset、回退或把尚在运行的 1a07455 CI 算作已通过。产品发布、验证工具版本和后续文档版本分别记录。

发布前直接检查：11 核心均 healthy，活动 Job/终端/宿主路径锁与所有旧文件写入屏障均 0；生产 `container_recursive_acl_intents` 尚不存在。保存 7 受保护配置、发布账本、11 容器身份与 StartedAt、两宿主身份、全部 153 普通 ACL 和 58 递归 ACL HDD 元数据的字节 SHA 与九字段。

请求先保存，维护入口观察器先就绪，再仅启动一次标准 update：

- unit：`whaledeck-dev-publish-c020c24.service`。
- InvocationID：`5739248fe78b4d26a20b2e59dd918081`。
- 实际开始/结束：2026-11-04 11:10:03–11:14:17 CST，约 254 秒。
- 终态：active/exited、MainPID 0、Result success、ExecMainCode 1、ExecMainStatus 0。

标准 helper 执行完整宿主发布安装、冻结四镜像构建、前向 Migrator 和三应用 force-recreate。包源下载等待期间没有重发发布；运行中的默认 Result success 不作为成功证据，取得进程结束的准确 Invocation 终态及完整 journal 后才签收。没有宿主重启或自动回滚。

## 实际安装、数据库与保护边界

安装后严格六组件来源核验通过：API/Worker 的实际镜像标签与内部 CLI、Gateway 实际 build-info、Migrator 无网络/只读/无 capability 的隔离 CLI，均为 c020c24；实际 HTTP API 和 Gateway 同源。两宿主实际 `/proc/PID/exe` 来源一致，并逐项核对当前 apphost 与 DLL 在运行进程中的 device/inode 映射，避免把磁盘上已替换的文件冒充已运行的新程序集。

Agent PID 为 3295429，MaintenanceHost PID 为 3295422。安装 validator 退出 0，验证 systemd、权限、UDS gRPC、受限 helper、Docker 访问、两自包含运行时及动态发现的有效 8080/8081 入口。协议仍沿用既有 1.53，本批不新增能力、权限或文件操作。

生产数据库只读查询确认：

| 实际检查 | 结果 |
| --- | --- |
| `20261103235841_ContainerRecursiveAclIntents` 迁移记录 | 1 条 |
| 新意图表 CHECK | 18 个 |
| `whaledeck_container_recursive_acl_fence()` 写入屏障触发器 | 11 个 |
| `whaledeck_container_recursive_acl_guard()` 不可变保护触发器 | 1 个 |
| 新递归 ACL 意图总数/活动数 | 0 / 0 |
| 其它活动 Job、终端及旧写入屏障 | 均 0 |

这证明迁移及对象已安装，不等于已执行真实 Worker 文件任务；新表为空不能算正常预检、执行或故障矩阵通过。数据库的拒绝/事务分项仍引用此前真实隔离 PG 证据，不向生产注入合成意图取得“实盘通过”。匿名新异常列表和原键查询两个 GET 实际返回 401；本批没有新普通用户、真实管理员或登录后的页面证据。

11 核心安装后均 healthy；仅三应用按正常覆盖替换，8 个数据库平台容器 ID/镜像/StartedAt 保持。7 受保护配置 hash、权限和所有者一致；全部 153/58 份旧 HDD 元数据字节与九字段保持，未重放旧 RPC/文件操作。发布账本 current=c020c24、previous=28062da、pending=null，允许正常记录本次覆盖；不能把账本预期变化说成“所有文件均未变”。user NOPASSWD:ALL 保留，产品 Agent 最小授权未扩大。

## 维护观察、证据与未验范围

观察器在发布前启动，覆盖实际发布及结束后，315.304 秒取得 157 个维护 HTTP 样本；全部 HTTP 200，非 200/连接错误 0，状态由 healthy 经 updating 回到 healthy/Ready。只签收本次采样事实，**不凭两秒采样推断瞬时零中断，不改写此前 159 次采样含一次连接失败的历史，也不签收整个 P0-05**。

准确归档两个自有 unit 的 Invocation/终态和发布 journal 后，仅停止已完成、MainPID=0 的发布/观察器 unit 残留；没有停止 Agent、维护宿主或其它进程。运行与开发检出均干净，索引保持 user 所有。

私有证据目录为 `deploy/artifacts/recursive-acl-release-c020c24-c6e37fb0/`：完整 CI、请求、前后快照、自有只读 preflight/postflight 脚本、准确 Invocation journal/终态、安装 validator、157 原始采样及观察器结果。实际验证的 52 份跟踪依赖源码冻结于 1a07455，并逐 blob/工作区比对；三个私有只读编排脚本另存 SHA，不冒称产品代码或已执行的 CI 测试。脚本及证据 SHA 保存在目录内；不把私密环境或凭据入 Git/日志。本批未重跑此前 4651/569/1161 的本地全量测试；那些属于 c020c24 候选记录，远端本次取得的准确 CI 另有回执。

下一步在全新自有容器/四对象树上完成完整真实 UI 常规批次：default 设置→access 两组完整目标→noop→mask→显式 ID 0/权限 0→default noop→default 清除→预检后取消；每轮先由独立物理检查读取全字节、身份、mtime、普通 xattr/非目标 ACL，再允许下一轮写入。核对原永久 Job/outbox/审计、Root 聚合/各子项、attested HDD、重载原引用与幂等、普通/匿名拒绝、同上下文正常双端退出和精确清理，最后独立只读复核。此前已完成的原生 RPC 场景不重放来冒充此批页面证据。随后进行中故障矩阵、递归 ACL 历史恢复、可恢复删除/长期清理、维护入口连续性及其余 59 项继续；无新增跨盘或物理冷启动验收。

后续接续：七类真实页面分项及三批原总失败已保留，取消/重载引用与原正常退出仍未签，见[实机部分记录及修复候选](CONTAINER_RECURSIVE_ACL_REAL_UI_PARTIAL.md)。本发布记录和原时间/安装证据保持，不将后续分项回填成完整页面通过。
