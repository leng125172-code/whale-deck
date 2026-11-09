# 开发空间归档续传与发布流程

2026-11-09，代码 `4cd75eef`，迁移 `20261109150125_DevelopmentWorkspaceSourceImports`。BFF 登记导入、接收归档分块、读取状态、确认发布和取消。Worker 从 PostgreSQL 领取有时限的导入租约，通过签名 Agent 暂存、查询和提交。用户通过原计划摘要确认首次发布。授权沿用空间所有者和 Authentik 当前权限，分块和发布确认还绑定发起时的登录会话。Worker 派发前复核当前空间、预算及权限。

每个空间只允许一个未结束的导入。归档上限 128 MiB，HTTP 每块 1 MiB，最后一块为剩余长度。按字节偏移顺序提交，同偏移的相同内容可重传。所有工作空间合计预留上限为 512 MiB，上传与确认窗口为登记后一小时。数据块进入 PostgreSQL 前使用 AES-GCM 加密，认证范围包括导入 ID 和偏移。BFF 与 Worker 从只读挂载的 Agent 能力密钥派生专用密钥。宿主暂存确认或取消时删除数据库块，保留元数据和审计。

## HTTP 入口

以 `/api/v1/development/workspaces/{workspaceId}/source-imports/{importId}` 为基路径。写请求继续通过 Origin 与 CSRF 验证。

所有者也可以 GET `/api/v1/development/workspaces/{workspaceId}/source-imports` 查询当前导入。优先返回进行中的导入，否则返回最近终态，无记录返回 204。Vue 刷新后通过该接口取得导入 ID 和续传偏移。[页面验收](DEVELOPMENT_WORKSPACE_SOURCE_VUE.md)

| 方法和相对路径 | 请求和结果 |
| --- | --- |
| `PUT` | JSON `format`、`archiveBytes`、`archiveSha256` 登记导入。同 ID 同参数可重试 |
| `GET` | 返回当前状态、已提交字节数和计划。连接中断后以该偏移恢复上传 |
| `PUT /chunks/{offset}` | 发送有明确 Content-Length 的 `application/octet-stream`。最后一块提交后进入 `Ready` |
| `POST /publish` | 以 `planSha256` 确认 `Staged` 计划，查询 `Publishing` 或 `Published` 回执 |
| `POST /cancel` | 在发布确认前请求取消。Worker 核对宿主回执后结束并释放资源 |

Worker 先查询原导入，接管已成功的暂存或发布回执。撤权后，Worker 清理尚未发布的暂存。已经原子发布但未确认回执且权限已撤销时，保留未决状态。Agent 1.69 的 `Abort` 对未形成计划的导入清理半成品并写入持久取消标记。迟到上传会被拒绝。已有计划的取消使用原计划摘要废弃。

## 验收范围

- 回环 Kestrel 和隔离 PostgreSQL 执行完整迁移与真实 HTTP，验证跨所有者、匿名、Origin、CSRF、跨登录确认、并发同 ID 登记、分块重复和冲突、数据库密文与清理、原计划确认、撤权取消和回执丢失恢复。租约切点由测试夹具在写完宿主回执后保留旧租约，再用新 DI scope 接管。业务 HTTP 测试使用身份和 Agent 故障注入夹具。
- 离线 KVM 通过生产 AgentGateway、真实非 root Agent、Unix Socket、sudo、systemd 和 ext4 项目配额执行归档导入。SIGKILL 留下真实半截归档后，签名 Abort 清理成功并拦住迟到上传。已有暂存计划拒绝无计划 Abort，随后签名查询和发布成功。框架依赖及标准自包含发布物均通过，标准包回读源码版本和 SHA-256 清单一致。来宾与业务 HTTP 分开验证。
- EF 模型与迁移一致，已有导入历史时迁移拒绝降级。开发相关 .NET 8 项、归档 3 项和范围 8 项通过。

继续 Vue 上传与续传操作、Git 公私仓库/ref、现有工作树变更策略、运行源码指纹绑定、实体工作站安装及联合验收。

私有证据位于 `deploy/artifacts/development-workspace-source-workflow-4cd75eef/`，清单 SHA-256 为 `2d09d9a4815d2fa3aadb127e3d7a5bd829e294c818534435b589523ce1610ced`。
