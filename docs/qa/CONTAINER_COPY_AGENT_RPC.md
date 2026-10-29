# 容器复制 Agent RPC 候选与验证边界

2026-10-29 后续：冻结 `ab406eb` 已覆盖 Agent/MaintenanceHost，真实签名 UDS 的文件/目录复制与永久取消通过；独立 Root 原件、全字节/属性、准确清理及11核心健康通过。见[页面与安装态协议记录](CONTAINER_COPY_WEB_UI.md)。下文保留原候选阶段的历史边界，不把新独立协议验收等同于真实业务整链完成。

2026-10-28，产品源码提交 `6be0751`，Agent 协议候选 `1.45`。
本阶段实现协议、网关和固定宿主入口；**尚未安装候选，尚未通过安装态 UDS 验收**。
已安装版本仍以最近实际签收的 `5f3622d / minor44` 为准，本轮没有改动运行服务。
容器复制仍为进行中，不签完整文件管理或当前 59 项完成；17项移出不算完成。

## 已实现

- 独立 `whaledeck.agent.v1.ContainerCopyService` 的 `Plan/Apply/Inspect/Discard` 四方法。
  SDK 要求主版本 1、次版本至少 45 和 `docker.files.copy-intents` 能力，拒绝静默回落。
- protobuf 强类型计划和回执：精确容器 ID/实例、源与目标路径、原版本、完整源身份、
  源 ctime、父目录身份、目标证明、内核 UID/GID、指纹，以及文件或目录的 oneof 内层计划。
  UInt64 inode/device 和 Int64 纳秒不经浮点或文本数字转换。不把完整目录清单放进 RPC。
- 请求摘要覆盖完整 protobuf 和消息类型；独立 capability 域绑定方法、摘要、到期和单次 nonce。
  错误签名域、修改任意受测字段、nonce 重放均拒绝。普通 capability 不能授权新服务。
- Agent 与 SDK 校验固定逻辑根、内外路径/版本/父身份、普通对象类型及属性边界。
  只接受固定复制动作，不接受任意脚本、宿主路径或执行命令；Root 再核对实际内核所有者。
- 单次传输具有有界输入、输出、EOF 和期限；只有明确的 `CONTAINER_COPY_NOT_FOUND`
  可映射“回执不存在”，目标消失、版本冲突和未知故障不冒称没有执行过。
- Completed/Aborted/Unknown 回执保持原操作、实例、指纹与证明绑定。
  不确定结果保持私有内容可能存在的事实，不生成成功版本，不重新执行旧 Apply。
- 新增固定 `container-file-copies` helper：精确 action/32 位单元 ID/InvocationID，
  systemd 受限 profile、640 秒寿命、5 秒停止期限、控制组终止、384 MiB/1 CPU/128 FD/16 GiB 上限。
  不改变原上传或其他 helper 的预算。安装器提供 root 所有的固定模块和 0700 独立状态目录。
- 原生签名 UDS 客户端和独立 Root/实际内容验证脚本已生成并编译。
  CI 新增该客户端 Release 编译，现有 root 复制测试通配规则包含新增 profile 测试。

## 当前已执行的验证

| 检查 | 实际结果与范围 |
| --- | --- |
| .NET Release 构建 | 0 警告、0 错误 |
| UnitTests | 2882 通过；新增 80 项覆盖协议、签名和 SDK，包含真实 protobuf/gRPC 序列化边界 |
| IntegrationTests 默认配置 | 201 通过，7 项原生依赖夹具明确跳过；不能代替真实依赖验收 |
| TerminalProbe Release | 编译通过，0 警告、0 错误；没有执行安装态复制探针 |
| 复制 Python 专项 | 42 项被发现，2 通过、40 因非 root 跳过；不记录为 42 项全执行通过 |
| 宿主安装静态检查 | `tests/test_host_runtime.py` 12 通过 |
| 单工作站范围检查 | 8 通过；保留 59 项及 17 项移出决策 |
| Shell/Python 语法与 diff | 固定 helper、安装/验证脚本、原生验证器和 `git diff --check` 通过 |
| 文本编辑页面定向回归 | 系统 Chrome 下开发预览 18 通过，生产构建预览 18 通过；使用隔离接口夹具，不是真实身份验收 |
| Vue 类型检查与生产构建 | 通过 |

前序引擎提交的 40 项 Linux-root、真实 Docker 九项和七次 SIGKILL 证据，
仍在[独立复制回执记录](CONTAINER_COPY_INTENTS.md)。这些属于前序引擎验证，
不能替代新协议、helper profile 的安装态验证。

## CI 失败与修正

前序提交 `ad0101e` 的 GitHub CI `40638512705`：installer、backend、native-postgres、
native-valkey 成功；frontend 为 408 通过、1 失败。
失败为 1440px 暗色文本编辑用例在 Vue 抽屉进入视口前测量到屏幕外布局。
修正让该正向布局用例等待抽屉完整进入当前 clientWidth，随后仍执行原严格边界断言；
没有放宽像素边界、增加全局超时或改变产品布局，也不在 403 等应关闭抽屉的路径等待入场。
针对修正的开发/生产预览各 18 项通过，整条新提交 CI 仍需观察终态。

本阶段早期本地失败也保留其含义：一项篡改测试最初赋成原值，改为真正不同的值；
六项旧能力断言更新到实际次版本 45；新网关测试补齐缺失命名空间后重新全量通过。
这些不被写成安装态或生产故障。

## 当前不能签收的部分

当前命令进程 `NoNewPrivs=1`，普通 user 的 sudo 受到该进程限制，不能完成可信安装。
本轮没有通过其他入口绕过这个限制，也没有修改 sudoers、重启服务或部署产品镜像。
因此下列项目仍待执行：

1. 正常宿主权限环境下安装固定模块与 minor45 Agent，复核源码/二进制、权限、UDS、维护入口和核心健康。
2. 执行 `validate_container_copy_agent.py <完整提交> <可信 TerminalProbe>`：实际签名 UDS 文件/目录复制、
   原版本/属性/内核所有者、永久取消与重放/篡改拒绝、独立 Root 回执与精确资源清理。
3. 永久业务意图/EF migration、共享层级锁与发布屏障、Worker/Outbox/当前权限与取消、API 和真实身份页面。
4. 新业务链路的失联/重启/Unknown 管理员接管及完整失败测试，不能用 SDK 测试代替。

当前阶段保持 active。所有后续安装与发布继续采用用户确认的 dev 测试、修复后覆盖流程。
