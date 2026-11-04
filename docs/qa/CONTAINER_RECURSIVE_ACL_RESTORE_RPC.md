# 递归 ACL 历史恢复：签名协议与受限传输候选

日期：2026-11-05。功能提交：`e066ec1dfa158576186b49450610a7a65cbe2fd0`。本批仅完成协议候选，未安装发布、未完成安装态签名 RPC 或真实身份页面验收；59 个主任务的完整完成数不增加。

## 实现与权限边界

- 新增独立 `ContainerRecursiveAclRestoreService` 的 Plan / Apply / Inspect / Discard，Agent 协议声明为 **1.55**，SDK 必须同时取得该版本和 `docker.files.recursive-acl-restore-intents`。旧普通递归 ACL 的最低版本仍为 1.53，旧只读历史仍为 1.54，旧接口不获得恢复动作。
- 每次调用使用独立 request / capability 签名域、完整 protobuf 字节和消息类型摘要；短时方法绑定、nonce 一次使用。通用旧 token、普通 ACL 与只读历史签名均不能授权恢复。Agent 再核对实际请求摘要，不接受 context 内嵌凭据。
- 请求只携带当前容器位置、当前版本、captureId 和预期来源摘要。最多 10000 个对象的逐 inode ACL 留在已验证的 HDD 元数据中，不进入 RPC 请求，不开放任意主机路径、模块路径或调用动作。
- 计划绑定当前根目录九字段身份、父目录/版本/所有者、完整清单摘要及计数；另绑定来源摘要十字段、来源文件九字段 frame、异构恢复目标摘要。来源捕获中的旧实例与当前目标实例分开，来源的原执行 changeCount 不替代本次恢复的 changeCount。
- 来源 frame 要求 root:root、普通文件 0600、单链接、有效 inode 和 1–16 MiB 大小；九个字段在 protobuf 中显式保留 presence，UID/GID=0、时间=0 不等于字段缺失。恢复只操作来源的 changedAclType，继续复用 [内核候选](CONTAINER_RECURSIVE_ACL_RESTORE_KERNEL.md) 的来源围栏、全树当前前像、Partial/Unknown 和禁止续做规则。
- 新固定 stdin 传输只允许四动作，独立 Root 状态目录为 `/var/lib/whaledeck-container-recursive-acl-restore`；安装配置为 root:root/0700、两个模块 root:root/0750。普通递归 ACL 的 Root 状态目录没有加入新 helper 的可写路径。
- helper 限制 AF_UNIX、只读系统、固定可写 apps/HDD/自身状态目录、资源与时间预算，仅保留既有 ACL 写入所需的 DAC/FOWNER/ptrace 能力；没有添加 CHOWN、SYS_ADMIN、NET_ADMIN 或扩大旧只读历史 helper。停止操作仍核对原 unit 的 InvocationID。
- Runner 的动作识别、固定 argv、错误帧分类/有限映射、EOF 和尾随输出检查均已接入。来源缺失与来源变化不会被误报为“原私有操作回执不存在”；仅精确 `CONTAINER_RECURSIVE_ACL_RESTORE_NOT_FOUND` 在 Inspect 中映射 null。

## 本批验证

| 检查 | 实际结果 | 证据边界 |
|---|---|---|
| Release 全解决方案构建 | 0 warning / 0 error，2.96 秒 | 使用 `WhaleDeck.slnx` / .NET 10 |
| 后端完整单元测试 | **4892 通过 / 0 跳过** | 新增 142 项恢复协议/签名/SDK/策略检查；SDK 使用隔离 HTTP/gRPC 响应夹具 |
| 后端完整集成测试 | **612 通过 / 7 默认原生依赖跳过** | 本批没有新增 SQL 表或迁移，不能据此声称真实 PG 恢复业务已验收 |
| Linux-root 容器文件回归 | **531 通过 / 0 跳过** | 含新增 10 项恢复传输检查；真实临时 inode/ACL 和 SIGTERM，容器、映射、HDD 配置显式为夹具 |
| 现有宿主运行时配置检查 | **12 通过** | 离线配置检查，不是实际安装 validate 回执 |
| 59 项活动范围检查 | **8 通过** | 未恢复 17 个移出任务，未改第 1 节目标范围 |
| Python 严格编译 / 三个宿主脚本语法 / diff | 通过 | 新模块并入 CI 编译与现有容器文件回归入口 |

新增检查覆盖：全部来源摘要十字段和 frame 九字段的逐字段签名篡改；旧签名域、错误方法、过期/超期、nonce 重放；缺少零值 presence；来源与当前清单不一致；额外逐项 ACL 数组；错误回执、计数、状态及历史证明；四个固定 argv/InvocationID 停止入口及未知动作拒绝。临时文件测试检查 access/default 异构恢复、字节与非目标元数据保持、来源删除后的已完成结果只观察、目标丢失、取消围栏、真实 SIGTERM 后 Partial 不续做，以及 root/最终子项的映射守卫。

这些测试没有真实 Authentik 身份、已安装 UDS、实际工作站 HDD 挂载认证、永久任务 Worker 或 Vue 页面证据。前序内核实盘七场景保留自己的冻结来源和失败边界，不能转签为本批新 RPC 的安装验收。

## 修正与原失败

- 首次构建误写 `WhaleDeck.sln`，返回 MSB1009；改用仓库现有 `WhaleDeck.slnx` 后构建通过。
- 新传输测试首次 10 项中 1 项因读取不存在的快照 `content` 字段而 KeyError。夹具原 snapshot 已读取并验证全部文件字节；修正为调用其真实 `preserved()` 契约，保持 inode/所有者/mtime/非目标 ACL 检查，再经完整 531 项回归通过。不是产品 ACL 写入失败，也没有重做前序实机恢复。
- 复核发现来源 reader 的 `HISTORY_VERSION_CONFLICT` 应分类为 `SOURCE_CHANGED`，已用有限映射修正并加入回归；来源替换报告 Aborted，不再落入一般来源无效分类。
- 文档元数据更新曾把固定范围断言“17项移出不算完成”写成“17项移出不计完成”，8项范围复检中1项失败；恢复原固定措辞后复验，范围本身未变。
- 末尾空行的 diff 检查问题已修正。原失败工具回执在本轮会话中保留，没有将失败结果改写为成功。

## 同步与运行边界

功能候选已推送 origin/dev。准确提交 e066ec1 的 CI `41869390182` 已启动，2026-11-05 本批查询时为 in_progress，未取得成功终态；不能据本地回归直接宣布发布门禁通过。项目图谱以 `whale-deck.container-acl.e066ec1.recursive-restore-rpc` 写入 active，服务回执的 projectId/key/eventId 一致，本地 pending 已转 synced。没有安装动作、dev 自动回滚或真实身份操作。

## 下一步与交付门禁

1. 补独立原生签名探针，冻结来源与产物；通过准确 CI 后走标准宿主安装，实际核对 capability、安装模块 SHA、Root 状态权限、固定 helper 和 UDS。
2. 使用全新独占容器及来源捕获验证签名恢复、拒绝/重放、HDD 原件与独立读盘、准确清理；分别保留旧普通 ACL/历史兼容与维护入口观测，不能把入口发布后可达当作完整连续性通过。
3. 接入独立永久业务意图、EF 持久化、共享写入保护、Worker、管理员 API/Vue 与实际 Authentik → 页面 → SQL/Worker → Agent → 文件/HDD 整链，再执行故障矩阵。尚无这些公开恢复入口，不签收“完整恢复功能”。
4. 继续可恢复删除/清理、其余阶段功能及 59 项正式验收；保持 dev 覆盖发布与不自动回滚约束，user NOPASSWD 按授权保留。
