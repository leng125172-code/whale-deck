# 容器递归普通权限：签名 RPC / 固定 helper / SDK 候选

> 后续2026-11-02：宿主95c0787/1.49已安装，真实签名四RPC/HDD与旧协议兼容及独立核验通过，见[安装态证据](CONTAINER_RECURSIVE_PERMISSIONS_AGENT_RPC.md)。下文保留aead344候选阶段的原始测试与未安装边界；永久业务/API/Vue仍待。

2026-11-02。本批接通递归普通模式的 Agent/Contracts/Application DTO/Infrastructure SDK 与安装候选，**尚未安装或通过真实签名 UDS 验收**。候选源码 `aead344ffa9a05e62220e0a51532e6b20c750c0a`。最后已记录运行产品仍 `d1fcca8` / Agent 1.48；本批协议候选为 1.49。59 项继续部分实现，17 项移出不算完成；没有新公开 HTTP/Vue 入口，没有部署、自动回滚或扩大开发 user NOPASSWD:ALL 的使用范围。

## 独立协议和回执

新增 `ContainerRecursivePermissionService` 的 Plan/Apply/Inspect/Discard，使用独立 request/capability 摘要域，绑定准确方法和完整 protobuf 请求：CID/启动实例/容器路径、文件与目录普通模式、原公开版本、根九字段、父身份/版本、执行 UID/GID、整树 manifestDigest、全部计数、原 operation/owner/Actor/Trace/Job/幂等键。原单对象普通权限和所有者协议不扩展为递归，旧域/通用 token 不可授权新方法；短时 capability 和单次 nonce 规则继续，服务还从请求本体重算 SHA，拒绝只改 body 保留旧 header。

清单留在 root 私有 HDD，不通过 RPC 传回万条子项或文件内容。计划只返回密封摘要和有界计数，单次 driver 输出仍保持 64 KiB 预算。Application/Agent 两层核对目录身份、普通模式、1–10,000 项及文件/目录/变更计数；SDK 要求协议 major1、minor≥49 且明确 `docker.files.recursive-permission-intents`，旧 Agent 不发送新计划。

回执显式区分 Completed/Partial/Aborted/Unknown，逐项保留 completed/changed/aborted/notStarted/unknown 计数和 outcomeDigest；总数必须等于原计划，changed 不得超过已完成项或原计划变更数。Completed 必须全部完成且已验证历史；Partial 至少一项确已改变、尚未全部完成、没有未证明项；Aborted 不允许变更或未证明项；Unknown 必须有未证明项。只有 Completed 且历史当前可读才能声称 bindingCurrent。

历史验证事实 historyVerified 与当前可用 historyAvailable 分开：HDD 后来损坏或对象漂移不改写已完成历史，但不能再声称当前树/HDD 符合。精确 `CONTAINER_RECURSIVE_PERMISSION_NOT_FOUND` 只表示原私有回执不存在；SDK 只将该精确 NotFound 映射 null。容器消失、实例重建、HDD/Socket/注册表故障不能混成“未执行”或空结果。未知错误文字不进入 RPC，有限已知错误按类别映射。

## 固定最小权限入口

新增 `container-file-recursive-permissions` / `-stop` 固定 helper 分支和私有 stdin transport。命令行仅四种动作及规范 UUID，不携带 JSON、凭据、宿主路径或命令。模块文件 root 所有、不可组/其他写、单链接并受固定名单/大小限制。独立 root:root0700 状态目录、0750 模块安装和只读安装校验已接；与旧单对象权限状态分开。

临时 unit 固定 ProtectSystem/ProtectHome/PrivateDevices/NoNewPrivileges/AF_UNIX/禁命名空间、绑定 Agent、无自动重启、control-group 停止，capability 仅 DAC_OVERRIDE/DAC_READ_SEARCH/FOWNER/SYS_PTRACE，**没有 CHOWN、SYS_ADMIN 或 shell 执行**。写入路径固定为自身状态、已管理 apps 和原 HDD 历史路径。资源预算为 CPU50%、384 MiB、8 task、128 FD、单文件16 MiB；单对象旧 helper 的70秒/64KiB预算保持。

内核阶段软预算300秒，transport alarm310秒、Agent runner320秒、RPC330秒、unit硬截止340秒。启动输入仍10秒。正常/取消/异常清理使用原 driver ID 加准确 InvocationID 停止，只能停止自己原先启动的 unit；不按前缀停止其他进程。

SIGTERM 设置内部取消标记，不由调用者提供任意 callback；引擎在子项间检查并对已发生事实恢复。首项前再次检查标记。正在进行的单项写入仍可能已经完成，不能把取消返回直接解释为零变更。若系统调用/整树对账未在5秒停止宽限内完成，unit硬停止兜底；随后必须用原操作 Inspect 确认 Partial/Completed/Aborted/Unknown，不能新 Apply 补做或自动回滚。

## 已运行的检查

- Release 全方案构建：0 warning、0 error。
- 93 项新增 C# 专项纳入 3,678 项 Unit，全部通过；包含两个目标模式/根身份/manifest/计数/原任务等完整内容签名、重放 nonce/过期/错方法/错域拒绝、固定 argv/准确停止参数、DTO/SDK 投影、Partial/Unknown/损坏与不一致计数拒绝、HDD 当前不可用的历史终态、旧版本和错误 capability 不发请求。
- Integration 258 项通过、7 项默认原生依赖测试跳过。未把跳过项算原生 PostgreSQL/Valkey 验证；本批没有新 EF schema 或业务仓储，不冒称做了新永久业务/隔离 PG 验收。
- 递归相关 Linux-root 专项33项、全部容器文件395项全部执行通过、0跳过。新增11项传输测试和2项权限配置测试，含实际子进程 SIGTERM 后1项变更/3项未开始的 Partial、原请求重放不继续、目标丢失/重建只观察、正常四动作/原文件完整内容、丢失回执与HDD/socket故障区分、敏感错误脱敏；无会话的新 Apply 不建立执行JSON，不能无目标创建“成功/中止”事实。
- 非法动作/额外宿主路径/非法 unit/InvocationID 真实调用候选 helper，仅返回2且无输出，不进入 systemd。静态配置门禁检查固定权限/写路径与预算，不代表已执行合法安装态 unit。
- 12项宿主运行时 artifact 门禁、8项范围门禁、shell语法/Python -W error、修改MD链接和 git diff --check 通过。原子项取消与三类真实 SIGKILL 引擎单测仍保留。

C# SDK 测试使用明确内存 HTTP handler，Linux-root 传输测试的 HDD/会话使用隔离夹具；它们不是实际 UDS/安装态 sudo/systemd、Authentik、Worker 或 UI 的证据。前批[25281a1 内部引擎实盘](CONTAINER_RECURSIVE_PERMISSIONS_ENGINE.md)的4操作/2次候选SIGKILL、实际HDD与独立核验保持原来源，本批 public 字段/取消/边界修订不能借用该证据冒称新签名 RPC 已通过。

复核补强了两点：原候选停止只靠 systemd 终止，没有接入逐项取消标记，现接 SIGTERM 并用真实信号验证；两层回执曾允许 Unknown 却 unknownCount=0，现与 Root 永久回执一起拒绝并新增回归。是审阅发现后补强，不声称这两例在旧产品实机故障中发生或曾跑修前失败测试。原协议降级测试保持原 minor47/48 门槛，只更新当前 Agent 广告精确值为49。

日志 `/tmp/wd-recursive-rpc-build-reviewed.log`、`/tmp/wd-recursive-rpc-unit-reviewed.log`、`/tmp/wd-recursive-rpc-integration-reviewed.log`、`/tmp/wd-recursive-rpc-python-reviewed.log`、`/tmp/wd-recursive-rpc-signal.log`；此前阶段与首轮检查日志保留。新检查接 CI discovery/语法门禁，不能将本地通过报成远端 CI 全绿。本批无真实操作资源需要清理，Root/HDD 实盘证据没有改写。

## 接下来必须完成

冻结候选、正常覆盖宿主 Agent/helper/安装校验，用独占真实容器/实际HDD验证原生签名四RPC、篡改/重放/错误域/核心保护/只读拒绝、真实停止/超时/重启与Partial/Unknown，独立核对原回执/清单/物理树和准确清理。再接原会话与实时 Authentik 管理员、永久业务意图/EF迁移、共享发布屏障/路径和预算、确认/取消/审计/Worker/API/Vue、当前管理员恢复及真实明暗小屏页面链路；完整递归功能仍未验收。

继续真实大树/设备/挂载与长期元数据策略、递归所有者/ACL、可恢复删除/长期清理、剩余所有者故障和其余59项/UI/C#酒馆/统一供应商/AI Live2D。不得以本批候选注册或有限测试代替完整功能；17项移出不算完成，master 尚未正式交付。
