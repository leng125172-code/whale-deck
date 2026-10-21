# WhaleDeck 设计文档索引

本目录拆分产品、设计、开发与验收文档。2026-10-21用户授权的[SCOPE_SINGLE_WORKSTATION.md](SCOPE_SINGLE_WORKSTATION.md)及[GOAL_P0_P5.md](GOAL_P0_P5.md)定义当前59项范围，`FEATURES.md`同步产品定位；其他文档不得自行恢复已移出的17项或扩大目标。10月13日76项和更早HOLD为历史，不作为当前要求。

2026-10-13 已确认扩大为 [P0–P5 完整目标](GOAL_P0_P5.md)，原 HOLD/预留不再排除任务。旧页面/用例/RPC/模型/权限表是历史实现契约；新增能力须按任务 ID 同步专用设计后实现，不因解除预留而直接放开权限。用户已恢复开发，不设独立原型阶段。

## 文档导航

| 文档 | 责任 |
| --- | --- |
| [SCOPE_SINGLE_WORKSTATION.md](SCOPE_SINGLE_WORKSTATION.md) | 用户授权的单工作站精简决策、17个移出原ID、保留59项与安全底线 |
| [GOAL_P0_P5.md](GOAL_P0_P5.md) | 最新完整目标、范围变更、P0–P5 任务/依赖/完成标准 |
| [FEATURES.md](FEATURES.md) | 产品范围、已确认决策、功能状态和边界 |
| [DEVELOPMENT_WORKSPACES.md](DEVELOPMENT_WORKSPACES.md) | P3-13–P3-18：多语言开发空间、代码导入、远程调试/测试、报告、隔离及验收 |
| [PAGES.md](PAGES.md) | 页面、路由、受众、主要状态和操作入口 |
| [USE_CASES.md](USE_CASES.md) | 后端业务用例、输入、结果、审计和失败处理 |
| [AGENT_RPC.md](AGENT_RPC.md) | 宿主机 Agent 的 gRPC/Unix Socket 契约和安全边界 |
| [PERMISSIONS.md](PERMISSIONS.md) | Authentik 管理员组、普通用户、字段可见性和操作矩阵 |
| [DATA_MODEL.md](DATA_MODEL.md) | PostgreSQL/Valkey 数据模型、关系、索引和保留策略 |
| [ROADMAP.md](ROADMAP.md) | 分阶段开发任务、依赖、验收条件和测试要求 |

## 标识约定

| 前缀 | 含义 | 示例 |
| --- | --- | --- |
| `PAGE-*` | 页面或全屏状态 | `PAGE-CON-001` |
| `UC-*` | 后端用例 | `UC-CON-003` |
| `RPC-*` | Agent RPC | `RPC-DKR-007` |
| `PERM-*` | 权限动作 | `PERM-CONTAINER-START` |
| `DM-*` | 数据实体 | `DM-JOB` |
| `DEV-*` | 开发任务 | `DEV-M2-014` |

旧 `DONE/DEMO/MVP/P1/P2/HOLD` 保留为历史标记。新版统一使用“已验证/部分实现/待实现/待验收/外部启用待条件”，以 P0–P5 任务为实施顺序。未实现能力不得伪造可用路由或按钮；解除预留后仍须完成契约/权限/校验，不能直接放宽旧安全边界。

## 变更规则

1. 产品范围变化先修改 `FEATURES.md` 和“已确认决策”。
2. 页面变化同步 `PAGES.md`；业务行为同步 `USE_CASES.md`。
3. 涉及宿主机或 Docker 的行为必须同步 `AGENT_RPC.md` 和 `PERMISSIONS.md`。
4. 需要持久化时同步 `DATA_MODEL.md` 并创建 EF Core migration。
5. 最后把实现顺序和验收条件同步到 `ROADMAP.md`。
6. 不在文档中记录密码、Token、产品密钥、私钥或真实 Secret 值。
