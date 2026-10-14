# 通知私有配置：宿主基础与后续管理闭环

2026-10-14。此模块仍在实现中；本次宿主基础不替代 API/WebUI、权限/审计、凭据暂存和跨存储一致性闭环。

## 当前代码

- Agent 1.17 新增 `notifications.private-configuration`，独立 `NotificationConfigurationService/Inspect/Apply`。UDS peer 与现有 60 秒单次 method capability 均保留；API/Worker 不挂载 Docker Socket，不获得私有文件访问权。新客户端检查主版本/能力，不降级为其他命令。
- Root helper 只接受枚举动作与 stdin JSON，路径固定为私有 registry；生产 Worker UID 固定 1654，目录 root 所有且不可被 Agent 修改，文件 0600。拒绝链接、非普通文件、硬链接、多余字段、重复键、超深/超大 JSON、非法地址/Provider/凭据组合。无网络访问、任意路径、命令或环境参数。
- 渠道派生 `wd-<UUID>` 独占别名，不覆盖旧手工别名。应用/删除仅修改自身绑定，凭据可保留/替换/移除。全局版本冲突拒绝；私有 nonce 不由 Secret 派生。
- 绑定与有限幂等回执在同一个 fsync/rename 文件中提交；写入已完成但目录 fsync/响应失败，重试可识别已提交记录。旧回执在后续修改后仍返回原版本，不伪装成最新结果。最多 64 条 24 小时回执，32 目标/凭据及 64 KiB 文件预算。后续 Application 永久幂等回执仍必须覆盖根回执过期，不能仅依赖宿主短时回执。
- Worker 接受新回执字段，但只对选定 target/credential 计算传输 revision，元数据变化不取消无关渠道；真正私有值改变仍取消旧意图。
- 私有字节不进入 Agent OperationStore、PG、审计、结果、命令参数或 stdout。Inspect 只返回批准别名、布尔状态与版本；RPC 根错误仅固定代码。

## 验证与剩余

本地 Release 553 Unit / 73 Integration 通过，6 个原生测试明确跳过；全方案构建 0 警告/错误。Python 本机 6 条纯校验通过，8 条 Linux 文件系统测试明确跳过，不能当原生通过。Linux 临时目录测试与上线 RPC 验收待完成；不自动创建生产目标或发消息。

下一步必须完成：管理员 API 与 WebUI、一次性私有输入暂存、确认与计划、Application/Worker 持久操作和原子审计、宿主回执与 PG 渠道版本的断线/重启一致性、目标切换取消旧投递、清理与真实批准厂商启用。不能以本次基础接口将 P4-06 标成整体 completed。

GLM `glm-5.3-flash` 仅只读定位：274356ms，输入 768781、缓存 678144、输出 7945 Tokens，成功 1 次/重试 0，费用未知。定位结果由 GPT 核对；成本偏高，后续缩小单次定位范围与工具预算。核心设计、敏感边界、实际代码和验收由 GPT 执行。
