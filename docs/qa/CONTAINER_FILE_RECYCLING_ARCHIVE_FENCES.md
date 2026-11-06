# 归档副本恢复：独立日志与共享来源预留

2026-11-06，候选 `cb531a5605ee63d7204dbf4ad5ef75067bc377c5` 在[归档只读预检](CONTAINER_FILE_RECYCLING_ARCHIVE_PREFLIGHT.md)之上完成独立来源预留、明确取消和中断保护。已通过全新自有容器/实际配置 HDD 分项与清理后的独立、第三次重读。生产仍 `9694af1c`，未安装或发布；没有归档复制、暂存、新 inode 发布、签名 RPC、永久业务或真人页面验收，本批不计归档恢复完成。

## 日志与保护

新增独立 archive journal，目录不得与 source、purge、既有 restore 或 claims 同 inode，也不得互为祖先/后代。新 operation ID 不得等于原回收或清理 ID；日志绑定完整 archive 计划、owner 和独立 archive-plan 指纹。锁顺序为 archive journal→已完成 purge journal→原来源→预检时的 HDD gate。重构预检只增加内部持锁入口；公共计划仍拒绝任何 claim，完整预检和规范字节重核保留，只有持锁的预留操作可按其完整匹配 claim 重核。

共享 claims 使用既有严格 source mutex 格式，由原 `read_restore_claim` 读取验证：schemaVersion/source/restoreOperationId/restoreOwner/restoreFingerprint。这里的 ID 和指纹属于新 archive journal，不是旧的同 inode 恢复，也不提供旧恢复成功回执。不改旧解析规则，不覆盖或清理别人的 claim；旧恢复、清理及新归档计划继续受来源互斥和原 Purged 围栏约束。原 source Purged、purge Completed、Purged marker 和 HDD Purged 生命周期/字节/身份全部保持。

首次预留在两个原日志锁内重做完整预检，确认原计划仍匹配且无 claim，再依次持久化 Preparing→写入并 fsync 来源 claim→持久化 Reserved。重复 reserve/inspect 只读已有预留，不重新取得写入授权。基类 Apply 已明确拒绝 RESERVATION_ONLY，不让目录创建逻辑成为归档入口；所有当前回执 archiveCopied=false。

明确取消需按精确本操作 claim 重做完整预检，再先持久化 Aborted，最后只删除自己的 claim 并 fsync。新 ID 随后可预留；旧 ID 的晚到 reserve/discard 保持 Aborted，不释放新 ID 的保护。HDD 或命名空间变化、session 缺失时不释放 Reserved 保护。

准备日志尚无 claim 可落 Aborted；准备日志已有本操作 claim 而尚未提交预留时为 Unknown，不能改成已预留或成功。Reserved 的 claim 丢失转 Unknown，只重建本操作已知保护，不复制或发布；Unknown 再次丢 claim 同样处理。孤儿 claim、冲突 claim 保持未知或拒绝，绝不覆盖对方。已持久化 Aborted 之后中断，只完成自己的释放；Unknown 不通过 discard 自动释放。原清理与归档负载不被调和改写。

## 检查

26 项新专项及此前 114 项关联 Linux root 检查共 140 项零跳过，14 项前端 CI 合同、8 项范围及 5 个改动/新增 Python 模块语法通过。专项包含五个真实 SIGKILL 切点：Preparing、SourceClaimed、Reserved、AbortedSaved、Released；分别证明没有晚到预留、Unknown 保持、持久预留可读、只完成明确释放及取消不重入。

另验旧格式 claim 竞争、准备日志配外来 claim、Unknown 再次丢 claim、孤儿/冲突/不安全 claim、所有者与计划不匹配、来源/purge 锁争用、状态目录别名或嵌套拒绝、归档变化时不释放，以及原 Purged marker 的字节和完整身份保持。临时测试有真实文件、flock、fsync、fork/pidfd/SIGKILL；命名空间和 HDD 硬件证明使用明确替身，不冒充安装态或实机故障矩阵。

## 全新容器与实际 HDD 分项

unit `whaledeck-qa-archive-fences-cb531a56`，Invocation `ae7fb7853a474a13bf3847986e757484`，原 journal 明确业务 passed 和 Deactivated successfully，耗时 56.072 秒；不依赖 collect 回收后 systemctl 的默认结果。冻结 15 个宿主模块和 116 个验证闭包源码，在本批私有日志目录使用候选，没有替换安装态 helper、状态或 Agent。

只对本轮新自有容器的 1,048,583 字节文件执行原生回收与明确 Originals 清理，再生成 archive 计划。第一个新 ID 预留后，公共预检及另一个 ID 被 SOURCE_BUSY 拒绝，旧恢复/清理拒绝；原日志、Purged marker、整个当前 HDD 命名空间保持。重复 reserve/inspect 与重新创建的引擎读取同一 Reserved/同一 claim，未重新取得预留或恢复成功。

第一个 ID 明确取消后日志 Aborted/claim 缺席，原计划重核通过；第二个新 ID 预留后，旧 ID 的晚到 reserve/discard 不改第二个 claim 的实际文件身份、字节和属性。第二个 ID 再明确取消，仅两个 archive 日志和锁留在私有证据目录，共享 claims 回到原样且为空。两次预留/取消均无归档复制，也未重建被清理原件。

完成保护证明后仅移除本轮空私有目录；公共预检接受目录缺席的新计划，拒绝旧计划。精确清理自有 fixture 后，独立进程重新核对两个 Aborted 日志、空 claim 索引、当前 HDD、准确容器/目录缺席及旧业务/保护基线，输出 archiveFencesIndependent=passed。第三次再读当前 HDD、原日志及空 claim，同样通过。

53 回收、29 独立恢复、17 清理的永久意图完整行保持，活动 0；生产回收 Root124、恢复 Root80、目录 Root98、purge Root34、2346 保护、11 核心及宿主/应用版本保持。HDD121→123 仅增加本轮来源 `.json/.data`，旧文件身份/内容保持；source/purge/archive 候选日志仅在私有证据内，未创建永久归档恢复业务。源与 HDD 同 device，不称跨物理盘灾备。

证据 `container-recycle-archive-fences-f1b588210831494a87b0b2af70f2d9ab` 共 159 文件另加 manifest，摘要 `66fb932c3b2e86b2b983b4def81af17e12c0d1436b323e86dca48ffbc3ee6dde`；目录 root:root/0700、文件 0600。私有资源引用和记录不上项目图谱。上批准确 `7e649fb5` 的 CI `42228477427` 已通过后端/安装/原生依赖/前置及部分浏览器，记录时三个浏览器分片仍运行；本批 CI 亦须按新提交核对，不能沿用旧绿灯。

## 剩余工作

继续受控复制、完整属性与字节核验、暂存/新 inode 持久日志、fsync 和不覆盖发布；失败只观察、不自动复制或发布。之后接签名 RPC/安装、永久业务/共享写入保护/Worker/API/Vue/Jobs，再验真实身份与故障整链。来源 Unknown 专门策略、留存期限、其余 Unknown/实例变化/跨写入、维护连续性与完整59项仍待完成。17项移出不算完成；整项 0/59、阶段 0/6、工程量粗估 25%–35%、酒馆/Live2D 功能 0% 保持，不将独立分项累加成主任务完成。
