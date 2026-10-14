# 通知私有配置：宿主基础与后续管理闭环

2026-10-14。此模块仍在实现中；本次宿主基础不替代 API/WebUI、权限/审计、凭据暂存和跨存储一致性闭环。

## 当前代码

- Agent 1.17 新增 `notifications.private-configuration`，独立 `NotificationConfigurationService/Inspect/Apply`。UDS peer 与现有 60 秒单次 method capability 均保留；API/Worker 不挂载 Docker Socket，不获得私有文件访问权。新客户端检查主版本/能力，不降级为其他命令。
- Root helper 只接受枚举动作与 stdin JSON，路径固定为私有 registry；生产 Worker UID 固定 1654，目录 root 所有且不可被 Agent 修改，文件 0600。拒绝链接、非普通文件、硬链接、多余字段、重复键、超深/超大 JSON、非法地址/Provider/凭据组合。无网络访问、任意路径、命令或环境参数。
- 渠道派生 `wd-<UUID>` 独占别名，不覆盖旧手工别名。应用/删除仅修改自身绑定，凭据可保留/替换/移除。全局版本冲突拒绝；私有 nonce 不由 Secret 派生。
- 绑定与有限幂等回执在同一个 fsync/rename 文件中提交；写入已完成但目录 fsync/响应失败，重试可识别已提交记录。旧回执仍返回原版本，可在输入过期后查询；bindingCurrent 标记是否被后续变化取代。全局版本和绑定状态使用 HMAC，既捕获手工文件修改，也不暴露可猜测密码摘要。最多 64 条 24 小时回执，32 目标/凭据及 64 KiB 文件预算。后续 Application 永久幂等回执仍必须覆盖根回执过期，不能仅依赖宿主短时回执。
- Worker 接受新回执字段，但只对选定 target/credential 计算传输 revision，元数据变化不取消无关渠道；真正私有值改变仍取消旧意图。
- 私有字节不进入 Agent OperationStore、PG、审计、结果、命令参数或 stdout。Inspect 只返回批准别名、布尔状态与版本；RPC 根错误仅固定代码。

## 验证与剩余

本地 Release 553 Unit / 73 Integration 通过，6 个原生测试明确跳过；全方案构建 0 警告/错误。Python 本机只验收纯校验，Linux 文件系统测试不在 Windows 冒认通过。工作站源码 5b166ab 的全部 18 条 Python 测试在 Linux 临时目录实际通过，包括多进程互斥、重启、写后目录 fsync 故障、历史回执恢复、手工变更、链接和权限拒绝；不操作生产 registry。冻结 63cbdfb 的独立 PG/签名 HTTP/TLS SMTP 原生回归实际执行1/通过1且清理，报告 `dispatch-93288ae0bcd346b3ad0681f666a59b4f.json`，10:17:46.904541–10:19:43.280420 UTC。

5b166ab 首次覆盖更新成功、核心健康，但真实 UDS 验收发现 Inspect 失败；核对现有 Agent 的 ProtectSystem=strict 和 ReadWritePaths 未包含通知目录，固定 writer 继承只读挂载命名空间。71b0b43 改为独立 transient systemd unit，writer 只有该目录可写、ProtectSystem/ProtectHome/PrivateTmp/NoNewPrivileges 与 AF_UNIX 限制；没有扩大 Agent 写权限，也没有自动回滚。

71b0b43 最终修正运行时已验收：Job f4361e51-bc0e-4e24-9d79-ad67d839120f 调度，实际宿主 update journal 18:31:57 CST 成功终止，维护 healthy，11 核心 healthy。真实 UDS 正向 Inspect、缺 capability/错方法/单次 nonce 重放拒绝、缺请求 actor 与固定 root writer 危险目标拒绝均通过，生产 registry 指纹/不存在状态前后不变；无生产目标创建/外部发送。完整宿主 UDS/受限 helper/权限/动态入口验证通过，user sudo 保留。实际 OIDC 元数据管理与权限/OpenAPI 回归、8 个 HTTP 前端资源与 Gateway 相同；本次前端代码未变，不把资源检查冒充生产浏览器操作或新 WebUI 已完成。

下一步必须完成：管理员 API 与 WebUI、一次性私有输入暂存、确认与计划、Application/Worker 持久操作和原子审计、宿主回执与 PG 渠道版本的断线/重启一致性、目标切换取消旧投递、清理与真实批准厂商启用。不能以本次基础接口将 P4-06 标成整体 completed。

GLM `glm-5.3-flash` 仅只读定位：274356ms，输入 768781、缓存 678144、输出 7945 Tokens，成功 1 次/重试 0，费用未知。定位结果由 GPT 核对；成本偏高，后续缩小单次定位范围与工具预算。核心设计、敏感边界、实际代码和验收由 GPT 执行。
