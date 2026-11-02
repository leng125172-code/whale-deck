# 容器递归普通权限：内部引擎与真实 HDD 候选验收

2026-11-02。候选冻结 `25281a1433b1139610989cf52894316091deedc2`，产品仍为最后记录的 `d1fcca8` / Agent 1.48。本批未安装新模块或增加产品权限，没有部署、自动回滚或修改核心服务。59 项继续部分实现，17 项移出不算完成。

## 已实现的内部能力

`deploy/host/whaledeck-container-recursive-permissions.py` 接受已固定的 ContainerRoot，显式指定目录、原公开版本、文件普通模式与目录普通模式。准确 CID/启动实例、targetProof、父 inode/版本、内核执行 UID/GID、整树路径、每项九字段原身份、属性摘要和模式均参与计划。遍历为有界乐观清单，构建子计划前后重新扫描；不声称获得容器进程不能并发修改的原子文件系统快照。

服务端硬上限为 10,000 项（包含根目录）、深度 32、整树元数据 16 MiB、单次内核阶段最长 300 秒，可向下收紧。目录和文件只接受 `0NNN` 普通模式，最深子项先执行，根目录最后；每项继续使用已有单对象权限引擎的原 inode、HDD 前置证明和永久回执。元数据操作不读取文件内容；验收器另行完整读取两个文件各 2,097,159 字节。

起始路径可以位于合法容器挂载中；后代只从选中根 FD 解析，禁止向下跨挂载。拒绝符号链接、硬链接文件、特殊对象、重复 inode、外来路径、受保护子项、特殊位、POSIX ACL 和 `security.*` 属性；不通过普通 chmod 隐式改 ACL mask。普通用户属性只保留摘要，值不进入计划/历史。递归所有者修改、ACL 编辑与特殊位不在本候选中，仍需完整实现。

## HDD 与不重做恢复

所有写入前，在固定 `container-recursive-permissions-v1` 留存完整整树元数据清单，经现有 HDD mount/UUID/udev/rotational、私有目录、容量和数量策略验证，写出后重读验证摘要。聚合历史、子项历史、计划和结果有独立摘要域；每批子项采用固定派生 operationId 和私有状态/历史命名空间。该历史是权限元数据，**不包含文件内容，不等于内容备份**。

聚合 Preparing/Applying 与 startedThrough/finishedThrough 在每次调用前后持久化。Completed、Partial、Aborted、Unknown 是永久终态：

- 全部子项经原回执和当前 inode 证明完成，才记 Completed。相同模式记录观察结果，避免无变化 chmod 导致 ctime 改变。
- 已知部分实际变更，且其余项可证实未开始，记 Partial；不自动继续剩余项，不自动回滚已变更项。
- 修改前中断且原项仍完全匹配，封存 Aborted；即使调用者随后重发 Apply，也不能开始。
- 原件/目录成员/实例/历史缺失、损坏或发生不能证明的外来变化，封存 Unknown；聚合回执丢失但发现原 HDD 或子项状态时也不从头执行。
- Inspect 和晚到 Apply 只观察原子操作回执；没有自动恢复写入。已完成历史终态不改写；当前对象漂移或 HDD 损坏使 bindingCurrent/historyAvailable 失效。

清单除摘要外还严格核对结构、排序/唯一项、父子路径/CID/实例/执行身份/目标模式、同设备与唯一 inode、根身份及计数。修复只校验 digest 后提前解引用错误类型的漏洞；对结构损坏按 Unknown 封存，不让 TypeError/KeyError 逃出恢复。

## 自动化验证与失败保留

20 项实际 Linux-root 专项全部执行通过，包含全树/中文路径/混合模式、完整内容/原 inode/UID/GID/mtime、无变化与 0000、取消后 Partial 不继续、三类实际子进程 SIGKILL（首项前、首项完成后、实际 fchmod 后但子项回执前）、真实 ACL 拒绝不变、符号链接/硬链接/FIFO、条目/深度限制、只读与挂载证明变化、容量不足、历史篡改、重新计算摘要的结构坏清单、聚合/子项回执丢失、外来成员和完成后模式漂移。该专项的 HDD 证明和会话明确采用隔离夹具，不能当真实 HDD、Docker、身份或 UI 验收。

关联 `test_container_file*.py` 共 **382 项通过、0 跳过**；8 项范围测试通过；新增模块与两份实盘脚本的 `python3 -W error -m py_compile` 和 `git diff --check` 通过。已接 CI discovery 与语法检查；没有据此宣称 GitHub CI 已全绿或本批重跑了后端/前端全量。

保留初始失败：marker-only 中断原先将明确 `CONTAINER_PERMISSION_NOT_FOUND` 错判 Unknown，现仅对这个精确错误检查原计划、再永久 discard；其余失败不吞掉。补充容量测试第一次在目标命名空间创建前 stat 导致 FileNotFoundError，是测试夹具错误，已修；验证器正则字符串的 -W error 转义错误已修；最终元数据漏写“17项”的“项”导致范围守卫失败，现修正文案并保留守卫。日志分别为 `/tmp/wd-recursive-engine-recovery.log`、`/tmp/wd-recursive-engine-hardened.log`，修后专项 `/tmp/wd-recursive-engine-hardened-fixed.log`，关联 `/tmp/wd-recursive-all-container-files.log`。不删除失败或追改成初次通过。

## 实际容器 / HDD 门禁

冻结候选 `25281a1` 在一个准确标记、无网络、只读根、非 root、限资源的自有测试容器中执行，唯一合法 HDD bind 内新建固定四项树（2 目录、2 文件）。真实 ContainerRoot/Docker/proc/挂载证明、内核执行身份 `1000:1000` 与源对象属主 `2100:3100` 分开验证，HDD 配置及物理盘证明没有模拟。

| 原操作 | 实际结果 | 证明 |
| --- | --- | --- |
| 文件 0600 / 目录 0700 整树修改 | Completed，4 项完成、2 项实际改变 | 原 inode/属主/完整内容/mtime/用户属性保持，目标模式正确 |
| 相同模式的新操作 | Completed，4 项完成、0 改变 | 全部九字段含 ctime 保持 |
| 另一目标模式，首项完成后真实 SIGKILL | Partial，1 项改变、3 未开始 | 首项实际变更，其他项全九字段不变；重放不继续 |
| 原开始标记写出、首项调用前真实 SIGKILL | Aborted，0 改变 | 全树九字段不变，永久拒绝晚到执行 |

这两个 SIGKILL 来自**冻结内部候选的可信独占测试 checkpoint**，不是安装态产品 helper、签名 RPC、Worker 故障或真实页面证据。单测另覆盖实际 fchmod 后/子回执前，不能把单测切点称为本表实盘场景。

父验收器逐操作完整读回两份文件、核对属性和九字段时间线，重放原请求前后全部源指纹与 Root 原件 SHA 不变。4 份聚合清单、9 份已执行子项元数据留存在实际 HDD，Root 原件保留。完整证明后才逐项清理准确源 inode，随后原夹具核对并清理准确 CID 与 original.bin；没有泛用递归删除。

独立只读进程再次从实际 HDD 重读聚合/子项原件，重算计划/清单/历史/结果摘要和派生 ID，核对 4 个原操作与前后时间线、9 份子项历史、准确源/CID 不存在及 11 核心 ID/镜像/状态/StartedAt 保持且 healthy，任务/终端/写入预检通过。它不修改权限、不重放执行、不创建 HDD 历史，不在源删除后冒称重新读取物理字节。本批来源和历史同在 HDD，没有签收 NVMe 来源、跨盘或 10,000 项大树的真实容量/性能。

证据位于私有忽略目录 `deploy/artifacts/container-recursive-permissions-c446b6e49052482dbfc41535e430b1c9/`：baseline/sourceHashes、4 个 results、root-state、4 份 HDD 清单归档、physical-final、verified、independent-final。实际门禁首次通过，日志 `/tmp/wd-recursive-native-first.log` 与 `/tmp/wd-recursive-native-independent.log`；无该门禁遗留的准确测试资源。

## 尚待完整接通及验收

新增内部引擎尚未安装；没有公开 helper/签名 RPC/C# SDK、永久意图表/EF 迁移、确认与审计、各类发布互锁/路径锁/预算、专用 Worker/API/Vue。后续须完整接入当前管理员/实时 Authentik/原会话复核、取消/自然租约/Unknown 处置和进程恢复，再正常覆盖并跑真实身份→Vue→API/Worker→Agent→实际树/HDD 的整链及明暗小屏。

还需真实大树与设备/挂载边界、元数据额度/清理、超时、目录及实例变化、更多坏元数据/回执/进程/撤权组合；递归所有者、ACL、可恢复删除及长期清理没有因本批而完成。继续原所有者剩余故障与其余 59 项、UI/C# 酒馆/集中供应商/AI Live2D；master 尚未正式交付，开发 user NOPASSWD:ALL 保留，产品 Agent 权限未扩大。
