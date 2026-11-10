# 工作空间 Git 更新的 BFF 与 Worker 验收

2026-11-10，P3-14，代码提交 `282caa37b1a5f9ffa7f2e260fe95c8c2b2a97ec3`。

所有者通过 BFF 查询当前已发布来源，并在登记更新时指定该记录。数据库保留前驱关系及历史，限制同一前驱只能有一条成功后继。Worker 用上一条发布计划的内容摘要约束宿主暂存。确认事务绑定计划摘要和用户的冲突策略。干净策略遇到脏树时拒绝，保留后替换须显式确认。同一确认可重试，已入库的策略不可改写。发布回执未知时，后续 Worker 查询宿主状态并沿用该策略，确认后才推进发布基线。

## 验收

- 使用真实 PostgreSQL、Kestrel HTTP 和生产 Gateway 的 Unix Socket gRPC 链路。受控宿主服务注入迟到工作树修改和交换后丢失回执。验证缺少策略、脏树拒绝、迟到写入失败保留原基线、重复确认策略一致、恢复后仅一次交换、历史基线不可用于新更新，以及暂存块清理。
- 使用真实数据库验证有更新历史时迁移拒绝降级。EF 模型与最新迁移一致。
- 构建零警告零错误。单测 6,229 通过，常规集成测试 837 通过、7 个原生用例按默认配置跳过。启用隔离 PostgreSQL 后单独执行的原生整链用例通过。仓库范围检查 8 项通过。隔离 PostgreSQL 已清理。
- 宿主真实 Git TLS 和 ext4 原子交换证据见[前一批 Agent 验收](DEVELOPMENT_WORKSPACE_SOURCE_GIT_UPDATE_RPC.md)。本批单独覆盖控制面。Vue 的更新与冲突选择、旧树取回、发布状态未知时撤权后的恢复和物理工作站验收仍待验证。

本机验收包 `deploy/artifacts/development-workspace-source-git-update-workflow-282caa37b1a5f9ffa7f2e260fe95c8c2b2a97ec3/`。包内 `SHA256SUMS` 校验通过，清单摘要为 `3c3fb1ef30c8b10a5d81a6a3435df8feccac0df26eb03b82a654db526316f93e`。
