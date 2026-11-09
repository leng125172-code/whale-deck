# HDD 归档退役内部执行器与中断恢复

2026-11-09。本批在 `e77394f8` 基线上增加内部候选 `whaledeck-container-recycle-archive-retirements.py`，没有 CLI、RPC、标准安装、生产配置或自动留存策略入口。只对自有临时目录执行真实 unlink/fsync；未删除实际 HDD 备份、未修改生产应用或身份、未签自然到期或真人整链。

## 执行与证书

- 保持 retirement journal → archive → purge → original source → HDD 的锁序，HDD 只获取已有 `history.lock`。不通过观察创建旧来源锁或修复原 claim。
- 必须先显式 Reserved。Apply 从受保护的原 copy/purge/source/marker/claim/历史重新生成计划；除 `fingerprint/retentionElapsed/retirementEligible` 外，所有事实必须与原预留计划按规范 JSON 字节相同。允许到期前预留、到期后执行；重新计算摘要不能伪造到期或有效副本。
- 只有服务端当前时钟到期且完整 Completed 副本仍有效才进入执行。再次验证实例、权限、目标、存储配置、可写 HDD attestation、目录身份、原历史摘要、精确数据 FD/名称身份、完整哈希、root:root/0600/单链接、无意外属性和 partial，以及副本实际完整字节/属性/路径。
- schema 2 `Removing(completion=null)` 先持久化，确认原 Reserved marker 仍存在且匹配后才写 `Retiring` marker，重新校验后仅 unlink 原来源的 `.data`。原 `.json`、historyDigest、source/purge 生命周期和归档 claim 都不改；不清理 stage、其他历史或其他数据文件。
- unlink 后对 HDD 目录 fsync；通过持有的原 FD 验证 nlink=0、身份/字节/属性稳定、名称和 partial 缺席及副本仍有效，再生成证书。顺序为 `Removed(证书)` → `Retired marker` → 末次校验 → `Completed`。证书复用已有版本2格式，`unlinkedIdentity` 绑定 unlink 前的原九字段身份。
- `Removing/Removed` 纳入活动扫描和只读不确定分类。所有中间态重启后只转 Unknown；即便 Removed 有证书或 marker 已 Retired，也不会补做删除或推断 Completed。孤立 Retiring/Retired/Unknown marker 只重建版本2 Unknown。丢失 marker 仍由活动日志阻止竞争来源登记。
- 执行异常对仍属于本操作的 marker 和日志分别尽力标记 Unknown，不覆盖外来 marker、不重建丢失 marker。特别覆盖了 Completed 原子替换后目录 fsync 报错，不能仅因内存 state 已是 Completed 而把失败当成功。存储持续不可写时不能保证补偿记录成功，错误仍抛出；该候选不对外开放。

`Retired` 读取仍要求原链、唯一 Completed 证书、精确终态 marker、存储和实际数据缺席完整 join。旧 `history.verify/read/restore` 仍要求实际字节存在并校验，不把 Retired 误报为可恢复。

## 副本路径绑定修正与并发边界

只读审查发现旧 `copies.binding()` 对打开的 FD 完成哈希后未再次解析原路径。文件/父目录可能在哈希期间移走，但持有 FD 仍能校验成功。本批在哈希后重新解析父目录并核对叶子九字段身份；执行器也在每次完整副本读取后重核路径。两项回归分别使用实际叶子移走、实际嵌套父目录替换；后者只注入 claim，不把改造计划提交 Apply。

这不是对容器进程写入的原子排他保证：advisory 日志锁只约束合作工作流，容器可在最后检查后继续修改可写命名空间。检查后的外部变化仍可能导致写后 Unknown；不能据此声称副本永久存活或已完成并发安全产品验收。HDD unlink 限定于 root 私有目录和合作 history.lock，不能复用为任意可写父目录的 stat-then-unlink 删除原语。永久授权、共享写入围栏、运维处置及协议入口仍待单独实现/验收。

## 本轮验证

- 新增 **26 项执行器专项**：正向真实临时 unlink/fsync/生成证书与 Retired 读取；未预留、取消、年轻/无会话/伪造计划、副本/claim 丢失、数据替换、partial、只读存储、unlink 错误、HDD fsync 错误、属性、时钟回退/实时失权、写后副本丢失/数据重现、marker 故障、终态日志目录 fsync 错误、丢失/外来 marker、严格状态格式等。
- 真实子进程 SIGKILL 切点：Removing、RetiringFenced、UnlinkedNamespace（目录 fsync 前）、Unlinked（目录 fsync 后）、Removed、RetiredFenced，以及 Completed；前六个切点另外逐一组合丢 marker，验证竞争拒绝且不修复/不重删。
- 副本路径新增 **2 项回归**；原25项复制测试保持，合计27项通过。
- 最终完整回收 root 套件：**736 项通过，零跳过，74.478 秒**。命令：`sudo -n env PYTHONDONTWRITEBYTECODE=1 python3 -W error::ResourceWarning -m unittest discover -s deploy/validation -p 'test_container_recycle*.py'`。原 GitHub CI 的 discovery 已覆盖新测试，不需要安装入口。
- **8 项范围检查、14 项前端 CI 合同及12项宿主运行时合同检查通过**；6个改动 Python 文件内存编译通过（不生成 root-owned bytecode），Git diff 检查通过。未运行全量 .NET/前端构建或外部 CI，不沿用旧批次绿灯。

首轮20专项有7个失败：测试对全局 `os.unlink` 的禁止误拦了 Root 原子日志自身 `.tmp` 清理（1个孤立重建、6个强杀子场景），不是数据重删。已把断言精确限定到负载 `.data/.partial`，允许既有日志临时文件维护；未放宽任何 payload 重删断言。修正后扩为26项通过，随后两次完整回收套件735/736项通过（后一次增加嵌套父目录回归）。首轮失败事实保留，不追认首次通过。

提交 `5e94ce45` 后复查发现 marker 切换前还有一个窄切点：Removing 落盘后若 marker 丢失/变为外来 owner，原代码会直接写 Retiring。已在首次 marker 转换前再次执行完整 guard，并显式跟踪预期 marker 状态。用提交 `5e94ce45` 的原代码与增强测试在临时资源上复现两项预期失败（Removing切点未拒绝），证明回归能捕捉原缺口；当前代码在 Removing/RetiringFenced 两侧的丢失/外来 marker 场景均拒绝，数据保留，不修复丢失 marker或覆盖外来 marker。修正后的完整736项再通过；未安装有缺口的候选。

正向年龄由测试时钟注入，硬件/容器 namespace attestation 为明确替身；原历史时间戳不回写、系统时钟不调整。真实文件I/O和SIGKILL并不等于实际 HDD/自然到期/安装态认证验收。第二轮独立审查工具两次连接失败，无可用审查结论，不标记独立终审通过。

## 续跑

先补独立安全复核与全新自有实际 HDD 的精确放行/只读基线/执行后独立证据，使用真实自然到期，不回写年龄或降低最小留存天数。随后接专用协议与版本门禁、标准安装、永久意图/共享保护/Worker/API/Vue/Jobs和真人整链，再推进stage/来源复用、维护连续性、大文件以及59项其余工作。生产状态本轮未重新采集，不能把上一批计数当本轮现场证明。整项0/59、阶段0/6保持；17项移出不算完成。
