# 开发工作空间的持久运行与恢复

2026-11-09，代码 `74e39886`。P3-13/P3-16 接入 BFF 运行入口、PostgreSQL 任务意图与 Worker 调度。已分配空间的所有者提交 `runtime` 和 `task`，当前支持 Node 测试以及 .NET 构建和测试。服务端固定参数，.NET 任务使用 `--no-restore`。启动、查询和取消均复核当前登录、Authentik 权限、空间归属和批准存储根。一个空间同时保留一个未结算运行。

入队事务保存任务、运行意图、首个事件和审计，按主体与 `Idempotency-Key` 重放。运行意图保存空间版本、环境与预算、固定命令和原登录绑定。Worker 按 40 秒租约接管，提交派发标记后重新查权限和取消状态，再向 Agent 发起 Start。后续 Worker 用同一运行 ID 查询、取消或回收，使用入队时锁定的命令。已入队的运行超过 10 分钟即失去派发资格，Host 运行期限固定为 180 秒。派发标记已提交而 Agent 一直查无运行时，待 330 秒加运行期限后记为 `ResultUnknown`，保留原记录。任务历史存在时拒绝执行会删除历史的降级迁移。

## 分项验收

- 隔离的真实 PostgreSQL、Kestrel 回环 HTTP 和实际 `WorkspaceRunWorker` 测试通过。验证了并发提交幂等、异规格重放冲突、同空间并发门禁、其他主体无法读取、缺失 CSRF 与自定义 argv 拒绝、事件和审计唯一、迁移与非空降级保护。
- 使用身份与 Agent 故障注入夹具测试两个 Worker 同时领取、Start 回执丢失、重建 Worker 后接管回收、排队及运行中取消、派发前与运行中撤权，以及派发记录已提交但 Agent 没收到请求。撤权后的控制 RPC 始终绑定原运行，已提交派发记录的任务始终保持单次 Start 上限。
- .NET 构建 0 警告、0 错误，EF 无模型漂移，Agent 回归 3195 项、范围基线 8 项通过。Compose 验证无文件时正常解析，有合成配置时只有 API 和 Worker 读到相同开发策略。隔离数据库容器与网络已清理。

上批 Agent 1.65 使用标准宿主发布包在离线 KVM 内验证了真实 cgroup 取消与回收。当前批次的 PostgreSQL、HTTP 和 Worker 已联调，Agent 与 Authentik 使用故障注入夹具。真实 Valkey/Authentik 与 Agent 端到端、Vue 页面和运行工件自动采集仍待验证或接入。当前会话的 `NoNewPrivs=1` 阻止物理工作站的标准宿主安装。[Agent 运行控制验收](DEVELOPMENT_WORKSPACE_RUNNER_CONTROL.md) · [部署配置](../../deploy/install/README.md)

私有证据位于 `deploy/artifacts/development-workspace-runner-queue-74e39886/`，清单 SHA-256 为 `b643202954f294d72954f01cc880866da2f60eac2c28b8aaef869709780de886`。
