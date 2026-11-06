# 归档复制：安装态父级采集与独立放行候选

2026-11-07，候选 `b7670f99` 已实现固定路径只读采集、三阶段父级控制与独立进程复核。18项采集/收尾/语法专项、完整585容器文件与453回收检查零跳过通过，14 CI合同/8范围及7份改动Python语法通过。承接[原生探针](CONTAINER_FILE_RECYCLING_ARCHIVE_PROBE_CANDIDATE.md)，本批没有运行新安装态写入门禁、切换能力/版本、安装或签收正向签名RPC/HDD、真实身份业务页面。

## 固定路径、原始字节与真实对象分开核对

新增只读 acquisition，从固定archive journal、原source/purge journal与marker、既有restore claims、当前配置HDD读取。目录须Root0700、非符号链接且路径/FD同一；私有记录经Root普通单链接0600的FD重读并逐字节归档，不用重新序列化的JSON冒充原件。不导入或调用产品复制/恢复内核，也不通过RPC或进程控制推进状态。

HDD必须通过当前配置、目录FD和只读attest，保留storeIdentity/storageHash、history原始字节和实际data的完整身份/长度/字节摘要/属性，出口再核对。join要求调用方提供真实已完成清理的基线，逐值保持Purged source、Completed/unlinkConfirmed purge、Purged marker/history/data，校验新计划的分域摘要与本操作claim。只有claim缺席可记为null，必需日志缺失直接失败。

物理采集不强开或创建旧回收private：计划允许缺席时保持null；存在则核对Root0:0/0700和身份。新stage固定独占操作名，Root0700、准确inode，目录只允许已知copy.partial。Reserved/Aborted时目标与新stage必须缺席；Completed活体状态要求目标新inode与root/receipt相同、不同于原已删/HDD身份，字节/属性/UID/GID/mode/单链接/大小/mtime匹配，stage准确且为空。Unknown不进入通用成功物理证明或自动收尾。

## 父级和独立进程

父级先验证实际安装helper逐字节等于指定产品revision，验证探针源/入口/整个发布包checksum与probeRevision，再检查空闲基线；新能力Ready之后才创建全新自有容器/文件。旧runtime或缺新helper不能进入写入路径，本批没有用patch固定路径的方式绕过安装前置条件。

从source规划/回收、purge规划/预留/取消与正常清理，到Archive规划/负向矩阵/预留/取消与新复制，每次RPC回复均先做严格wire证明、原始记录和当前物理/HDD核对，才发送下一动作。只允许新source的三份记录、两份HDD、两个purge和两个archive ID各自journal/lock及一个共享claim增量，其他旧行/文件/核心身份必须保持；五ID必须不同。

新副本存活时保存24步证据和34帧前缀，并启动独立验证进程。前缀明确closed=false，不冒充已结束探针；独立进程还需确认原探针PID/start tick/cmdline仍活，重新读安装模块、源代码/包校验、全部原始记录、当前HDD以及新目标/stage和当前容器实例。历史取消journal只与自己的原件比较，不能拿后来属于正常操作的claim/marker去补签过去取消状态。

独立live证明通过后，父级重新绑定parent和leaf FD、完整目标身份/字节以及准确空stage身份/权限，再只移除已证明的新副本和stage，校验叶FD已无链接并fsync父目录；不是产品回滚或未知文件清理。然后仍用原probe请求同一archive Inspect，必须历史Completed/archiveCopied=true且bindingCurrent=false，原root/claim/HDD保持。结束语法现强制36帧，缺少这次观察的35帧不再被最终证明接受；34帧只能作为live prefix。

最后才删除已证明的旧空private、关闭probe并精确收尾自有fixture。最终独立进程重读当前HDD/claim/Root、确认准确容器/目录缺席、旧业务/保护及有限增量保持；不以保存的成功标志代替当前读取。新副本或stage身份不符时拒绝清理并保留现场，没有递归删除。

## 检查与原失败

临时真实文件系统专项覆盖Root/HDD/新inode捕获不改写、缺席private不创建、unsafe目录/claim权限拒绝、必需来源缺失、捕获原始字节变化、HDD损坏、目标替换/硬链接/stage外来条目、Unknown拒绝，以及准确FD收尾和错误身份拒绝。目录 namespace/HDD硬件证明使用明确替身，不能计作实际安装态或签名RPC验收。工作流和命名空间增量用构造证据验证，physical证据仍须现场采集。

初版8项I/O专项中7通过、1失败：缺必需来源时已拒绝，但实现把FileNotFoundError转成AssertionError，测试要求传播原始缺失类型。仅允许claim缺席的规则不变，其他缺失改为直接重抛；初始代码与失败事实/输出类型保存，未保存的完整原日志不重建。本轮另一项复核收紧了最终语法：最终入口仅接受36帧，不能靠调用方额外断言补齐清理观察。

生产仍9694af1c；53回收/29恢复/17清理永久意图完整行、活动0，回收Root124/HDD127/恢复Root80/目录Root98/purgeRoot34、2346保护、11核心与宿主/应用身份前后保持。没有新生产来源历史、archive namespace或安装态业务写入。上批417391da的CI42251486376已核对12路全部success，关闭上批旧CI截取修复的远端终态缺口；本批新CI仍待确认。

证据 `container-recycle-archive-gate-candidate-e98e906a2ed8409a91cc0e46f83fad93` 共144文件另加manifest，摘要 `1943b9f98d35df5587a0a7bd4833e83ba7991fd5cc57457f97f3938ee04110bc`；129份源文件包含124份Python闭包与5份探针/Contracts来源，Root0700目录/0600文件保持。私有资源引用不上项目图谱。

下一步版本/能力切换与标准安装、用完整父级/独立门禁跑实际正向RPC-HDD，再推进永久业务/共享保护/Worker/API/Vue/Jobs与真实身份整链。故障矩阵、成功claim/暂存清理、留存/维护连续性及完整59项仍需完成，17项移出不算完成；整项0/59、阶段0/6、粗估工程25%–35%、酒馆/Live2D功能0%保持。
