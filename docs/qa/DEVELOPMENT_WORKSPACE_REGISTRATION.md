# 开发空间注册与运行时锁定

2026-11-09，代码 `ce6612e9` 和列名修正 `6da6caa4`，迁移 `20261109101257_DevelopmentWorkspaceRegistry`。

`/api/v1/development/workspaces` 提供注册、按 ID 读取和按 slug 续页，`/catalog` 返回管理员批准的运行时、存储根引用和预算。注册只接受当前管理员或部署配置授权的 Authentik 组。普通读取按所有者筛选，管理员可读取全量注册记录。写入使用 BFF 同源、CSRF 和幂等键。

部署端通过 `DevelopmentWorkspaces` 配置 `StorageRootIds`、`CreateGroupIds`、`Capacity` 和 `Runtimes`。运行时条目含 `Version` 和以 SHA256 固定的 `Image`。注册请求只提交目录键、已批准的存储根 ID、slug 与资源预算。没有配置允许的存储根及运行时就无法注册。预算单位为 CPU 毫核、内存 MiB 和磁盘 MiB。注册事务串行检查全局登记预算，并保存规范运行时清单、摘要、原请求引用和审计。

注册状态为 `Registered`。已注册空间可在首份报告产生前设置报告读取授权，旧报告继续按其原空间记录工作。

## 实际验证

- 真实 HTTP 与 PostgreSQL 通过同键并发重试、重放后时间及环境摘要一致、同键改请求和重复 slug 拒绝。两个新请求争用最后一份登记预算，只成功一个。验证了批准的运行时与存储根、同源 CSRF、所有者隔离、管理员分页、首份报告前的读取授权，以及非空注册历史阻止降级。
- 首轮真实迁移因 MiB 字段的 PostgreSQL 映射名与约束引用不同而停止。修正约束后，使用全新的隔离 PostgreSQL 重跑通过注册专项。
- Release 6,214 项单元、837 项默认集成通过，另有 7 项按原生依赖条件跳过。独立真实 PostgreSQL 全链通过，输出 112 个阶段标记，耗时约 2 分 31 秒。测试数据库、网络和运行时容器均已清理。8 项范围、14 项前端 CI 合同、模型快照和 Release 编译通过。

19 份本地私有证据位于 `deploy/artifacts/development-workspace-registration-6da6caa4`，清单 SHA256 为 `a60538b1a3045760f838de2cd3a7f45d802717dc0acb4d940b5f40f865979e2f`。

## 验收边界

使用 PostgreSQL、回环 HTTP、测试签发的会话和受控身份目录。锁定的 .NET SDK 10.0.402 与 Node 24.15.0 在两个无网络的非特权容器内读取同一份只读样本，均得到 `workspace-sum=5`。首轮 .NET 夹具的临时目录权限导致 SDK 初始化失败，调整仅限隔离容器的临时目录后通过。存储根是本轮部署策略中的不透明 ID。Agent 仍需实际校验设备并创建空间目录，随后接入 Runner、测试服务、Vue 和真实工作站的完整验收。
