# 工作空间存储分配与恢复

2026-11-09，P3-13 工作空间的注册与签名 Agent 分配由 PostgreSQL 串联。代码为 `19cca738`，迁移为 `20261109105105_DevelopmentWorkspaceStorageAllocation`。

- `GET /api/v1/development/workspaces/{id}/storage-plan` 从已登记工作空间读取批准存储根、锁定环境摘要和磁盘预算，并返回绑定所有者的计划摘要。
- `POST /api/v1/development/workspaces/{id}/storage-allocation` 只接受计划摘要。BFF 校验登录、当前创建权限、所有者、批准的根、同源和 CSRF。调用方不能指定宿主路径、设备、环境内容或替换预算。
- PostgreSQL 短行锁先落盘 `StorageAllocating`、计划摘要和开始审计。事务结束后重新校验当前身份才签发 Agent 请求。收到匹配回执后在一个事务内完成 `StorageAllocated` 和成功审计。初始、分配中和已分配状态对应版本 1、2、3。
- Agent 未返回时保留 `StorageAllocating`。重新登录且当前有权的所有者可提交同一计划，由 Agent 核对原来的封存凭据。并发提交可走同一幂等分配，数据库只记录一次开始和一次完成。完成后的同计划重放从数据库返回状态。
- 新迁移保留旧注册行，并拒绝缺少有效计划摘要或时间戳的分配状态。存在分配历史时禁止降级，以保留可能已发生的宿主操作记录。

## 实际验证

隔离 PostgreSQL 和真实回环 HTTP 验证了旧注册行升级、非法分配状态约束、降级保护、计划篡改、路径参数拒绝、跨所有者访问、CSRF 和组权限撤销。在可控 Agent 边界注入分配成功后丢回执的故障，确认开始事务已提交。第二个 BFF 实例建立独立会话后并发重试，数据库保留原计划，最终各有一条开始和完成审计。完成后的重放未再次调用 Agent。此轮身份和 Agent 边界使用测试夹具。

Release 编译零警告零错误，模型快照无待生成变更。6216 项单元测试及 837 项默认集成测试通过，7 项默认跳过。独立隔离 PostgreSQL 原生回归通过，输出 113 个阶段标记，并确认容器与网络清理。首轮迁移生成工具需要显式指定本地 .NET 目录，生成的迁移随后按仓库规则改为文件范围命名空间。

本地证据在 `deploy/artifacts/development-workspace-storage-allocation-19cca738/`，`SHA256SUMS` 的 SHA-256 为 `5a0b3c4c8f36a3efd61957ed3f87272b53f0428a8fc5d0f464093910f7034272`。

`StorageAllocated` 表示目录分配已获确认。实际磁盘配额、Runner 接入、Vue 和工作站的标准安装与全链验收继续实施。当前会话 `NoNewPrivs=1`，标准宿主安装待验收。
