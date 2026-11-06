# HDD 归档复制恢复：新 inode 与不覆盖发布内核

2026-11-06，内核 `d5395d51` 与独立实机门禁 `7768ab50839c03982037a68269c59dc102d22441` 已通过分项验证。在[来源预留](CONTAINER_FILE_RECYCLING_ARCHIVE_FENCES.md)之上实现显式 Reserved→复制→新 inode 发布；生产仍 `9694af1c`，没有安装、签名 RPC、永久业务、Worker/API/Vue/Jobs 或真人身份页面验收。不计完整归档恢复或主任务完成。

## 复制、发布和不可变来源

仅接受已独立预留的 archive 操作，仍要求原清理 Completed/unlinkConfirmed、source/marker/HDD Purged 和精确本操作 claim。Apply 先重做完整计划，再锁定原 HDD gate、只读归档 FD、原容器/实例/父目录与目标空位。每块复核 session、当前存储配置/UUID/目录、同一归档 FD/路径身份；完整字节数和 SHA256 必须匹配，归档副本和旧 source/purge/marker 均不改写。

每个新 operation 在目标父目录原子创建独占 `.whaledeck-container-archive-<operation>`，Root 0:0/0700；O_EXCL/NOFOLLOW 创建 root 0600 单链接暂存文件，禁止使用原已删除 inode 或 HDD 数据 inode。目标可用空间需覆盖数据及 16 MiB 余量。逐次处理短写，核对链接数和暂存路径/FD，按原文件恢复 UID/GID、普通权限、mtime 与源计划内的有界 `user.*` 属性，再完整重读字节/属性并 fsync 文件与目录。源计划原本不支持的 ACL/其他扩展元数据并未因此扩为支持。

自建暂存目录使 parentVersion 变化，不能直接忽略：创建前绑定全目录条目及父目录身份，创建后仅允许新增本操作目录，核对其余条目完整身份与父 mode/owner/link 转换，然后固定新的 parentVersion；之后每个发布前检查均要求它和其他条目保持。未知预占目录、目标冲突、邻居变化、暂存替换/硬链接或会话/配置变化均拒绝。

Prepared 与 Publishing 分别落盘；最终再次核对完整暂存与来源，以 RENAME_NOREPLACE 一次发布，绝不 exchange/覆盖既有目标。发布后 fsync 两个目录，重读新目标的身份、字节/属性及原父目录绑定，最后记录 Published、退出归档证明并确认当前绑定后才记 Completed。原清理、来源、历史仍保持 Purged；新归档复制的结果只归于新 journal/operation。

## 中断、观察与当前绑定

Applying、Copying、Prepared、Publishing 或 Published 中断只收敛为 Unknown，保存现场和来源 claim；inspect/discard/旧 Apply 均不续写、重发 rename 或自动清理。即使原路径已有新文件，未完整提交结果也不补签成功。显式取消只处理尚未进入写入的 Reserved，先 Aborted 再释放本操作 claim。

已提交 Completed 的历史 archiveCopied=true 不因后来目标消失而抹除；有 session 才核对 bindingCurrent，目标缺席/变化为 false，无 session 则为 null，不能将“未核对”说成已失效。观察可重建本 journal 已知且缺失的精确保护 claim，属于元数据保护修复，不是纯只读，也不复制、发布或删除负载；冲突 claim 不覆盖。成功仍保留归属 claim 和已记录身份的空操作目录，长期清理及再次重用来源需要后续明确策略，本批没有自动释放这些保护。

## 检查及准备记录

24 项专项加此前 140 项关联 Linux root 检查，共 164 项零跳过；14 项前端 CI 合同、8 项范围、4 个新增模块严格语法通过。包括九个真实写入 SIGKILL 切点（Applying、PrivateCreated、PrivateBound、Copying、CopyChunk、Prepared、Publishing、PublishedNamespace、Published）与 Completed 提交后强杀；重读和晚到调用不续做，原 HDD/清理记录保持。

另覆盖新 inode/原 UID-GID-mode-mtime-user 属性、无需隐式预留、已取消不重入、同内容归档换 inode、存储配置变化、容量不足、短写/写入与 fsync 错误、晚到目标不覆盖、暂存属性/硬链接、创建暂存时和之后的外部目录变化、完成后绑定丢失、无 session 的 null 与精确 claim 修复、非法结果记录拒绝。临时测试的命名空间和硬件证明为明确替身，不冒充真实身份或安装故障矩阵。

首次 19 专项中 18 通过，容量测试替身缺少既有 HDD 证明使用的 f_flag，1 项失败；修正替身后通过，未放松容量检查。保留初始两份源码及失败事实/工具输出摘录，未保存的完整日志不重建为原件；3 文件另加 manifest，摘要 `302c1e292cfb122889a316530d78dd9ae4984a481e63a2d6dbce470c9cc5d1cc`。

## 全新容器/实际 HDD 与独立实读

首批 d5395d51 全新自有容器/当前配置 HDD 已通过：原生回收和 Originals 清理后，从归档复制 1,048,583 字节为新 inode，完整属性/字节及重复调用保持，精确 QA 清理后独立/第三次重读通过。该批没有在新副本存活时独立重读，不补签此门槛；164 文件另加 manifest，摘要 `4061df53b085a4ce37b7467398b70e7213bf16642b021c06245765ed2861fbbc`。原成功证据保留，后续仅增强验证工具，内核和测试源码保持。

最终使用另一套全新资源，unit `whaledeck-qa-archive-copies-7768ab50`、Invocation `9a748924099943f5a10dae6d9331bc2d`，原 journal 业务 passed/Deactivated successfully，耗时 71.695 秒。冻结 16 宿主模块与 117 验证闭包源码；候选 journal/claim 均为私有目录，没有替换生产状态。

先用新 ID 验 Reserved 取消，再以另一个新 ID 实际复制；旧清理原件已缺席，原空回收私有目录已移除，使用新鲜空位计划。实际发布 inode 与原文件/HDD 副本均不同；字节、UID/GID、权限、mtime、单链接及 `user.*` 属性匹配。重复 Apply/inspect/discard 保持同一 Completed/新 inode，不再次复制。原 source/purge/marker、全部 HDD 条目及归属 claim 均保持预期。

清理前独立进程验证冻结源码与 Git、实际容器实例/唯一受控 bind/父目录，直接重读存活的新目标及空暂存目录身份；核对新 inode、完整字节/属性、Completed 日志、原 HDD 和旧业务保护，输出 liveTargetNewInodeReread=true 后父级才允许收尾。随后仅删除已证明的新测试副本和准确空目录，不返还旧 inode、不回滚产品操作；Root Completed、归属 claim 保留，历史复制 true/当前绑定 false。清理后独立及第三次当前 HDD/日志/claim 重读、精确容器/目录缺席检查通过。

53 回收、29 独立恢复、17 清理永久意图完整行与活动 0 保持；生产回收 Root124、恢复 Root80、目录 Root98、purge Root34、2346 保护、11 核心及宿主/应用版本保持。HDD123→127 仅两套本轮来源各新增 `.json/.data`，旧文件保持；没有创建永久归档业务意图。源与 HDD 同 device，不称跨物理盘灾备。

最终证据 `container-recycle-archive-copies-2bafc24e5fc644589ef1cad9c3f4f07d` 为 166 文件另加 manifest，摘要 `79c6522bffedd3e023982d98f03762a916e9753a27acdc89985d0446ed6c379f`，root:root/0700 目录与 0600 文件保持。准确上批 ad5b9462 的 CI42230741788 已核对 12 路全部 success；本批新 CI 仍需按新提交确认，不沿用旧绿灯。私有资源引用不上项目图谱。

## 后续

补专用协议/SDK/原生探针与标准安装、真实签名 RPC/HDD，之后接永久持久化/共享写入保护/Worker/API/Vue/Jobs、实际身份整链和故障矩阵。Unknown 来源仍拒绝，需单独策略；成功后的暂存目录/claim 清理、来源复用、留存期限、维护连续性及其余59项继续。17项移出不算完成；整项 0/59、阶段 0/6、工程量粗估 25%–35%、酒馆/Live2D 功能 0% 保持，不以有限分项代替完整交付。
