# 命名空间发布后、完成回执前：真实 Unknown 与管理员处置

2026-11-07，QA `234bc495991f0ff300ba0aac53510b67b3549549` 在已部署产品 `f318d9ab130ecab94668acf9754a6a6e448de92c` 上，以全新自有容器及两名临时真实管理员完成本组合。承接[发布后切点候选](CONTAINER_FILE_RECYCLING_ARCHIVE_PUBLISHED_CUT.md)，本批无产品修改或再次部署，master 未改动。

## 实际切点与中断

真实身份经可见Vue完成原文件回收、留存清理Ready取消/实际清理、归档Ready取消和目标归档最终确认。私有stdin传入两份登录凭据，浏览器删除fixture中的credentials引用；owner/recovery均显式传给登录函数，不通过新环境变量、命令参数、storageState、trace或证据文件保存凭据。

原安装态apply进程绑定pidfd、MainPID、InvocationID、cgroup、固定自有实例和分域执行ID；容器root、archive/purge/source三锁、当前HDD数据、父目录、stage和output八个互异FD证明通过。Root仍为Publishing且publishedIdentity为空，目标已经由NOREPLACE发布为原pending/output新inode，完整内容/属性/hash与HDD来源和计划一致，stage存在而copy.partial缺席。ctime仅要求不早于Prepared；发布后的父目录版本与历史parentAfterStage不同，停止前后稳定。

仅在以上证明完成后交给页面实际“请求取消归档恢复”及确认；原helper自允许页面动作前开始计时，3.221992605秒内经原pidfd观察退出。原操作不CONT、不重放Apply。unit `whaledeck-qa-archive-published-234bc4950001`，invocation `fa870dd6d07e43858f167f2d70817dfc`，success/0，209.200秒。

Root Publishing和当前完整目标不能证明目录fsync完成，也不能证明Published/Completed持久回执已保存。本批始终保留directoryFsyncProven、durablePublicationJournalSaved及terminalCopyJournalSaved=false，不将现存目标解释成历史成功。

## Unknown、不同管理员与真实页面

取消审计恰1；原SQL意图Unknown、执行Job Running/AwaitingContainerRecycleArchiveReceipt、barrier保留且2条Outbox待处理，Root Unknown、archiveCopied=null、共享来源claim仍属原操作，目标完整新inode和空stage保持。规划及执行历史、公开receipt与实际Jobs页19个固定字段逐项关联。

原管理员正常BFF/Authentik双端退出，旧引用匿名401；另一Subject从正常页面登录，旧owner引用404。Jobs明确显示结果未知、未证明，且没有重新预检并重试按钮。异常入口先“暂不处置”，管理写入和ACK均0，独立读取前后全部相等；随后仅一次真实确认ACK/202。意图Acknowledged、Job Failed/Acknowledged、barrier释放且Outbox 2→0，但原receipt/Root/claim/目标/HDD不变，archiveCopied仍null。ACK JSON等于原receipt、ACK Subject与原actor不同，不能把处置计成复制成功。处置后的Jobs仍显示未证明。第二次正常双端退出、普通用户9个拒绝和匿名5个拒绝通过。

## 独立核验和仅限本轮QA夹具的收尾

独立live核验在任何目标删除之前完成：当前SQL/审计、Root原件、claim/source/purge/marker及HDD字节和元数据、实际目标完整snapshot/空stage、发布后父版本和original.bin均重读。Unknown→ACK只允许处置相关变化；原操作/目标/历史保持。

新专用QA收尾与Prepared-only及Completed cleanup分开：必须是本轮固定自有fixture、已由不同Subject ACK、Root Unknown/复制null且无lease/barrier；重新独占archive/purge/source三把原操作锁，核对Root原件、source/claim及当前HDD attestation。当前目标必须仍为原pending inode且与切点完整frame/hash/属性相等、单link，stage精确原身份/root:root 0700且为空；父目录身份和当前版本必须等于切点parentVersionAfterPublication，禁止拿rename前parentAfterStage授权删除。

持有nofollow parent/stage/target FD，重核目录项与FD后仅unlink本轮recycle-rpc.bin，确认打开target的nlink=0，再rmdir该空stage并fsync目录；Root/claim/HDD不删不改，历史父版本不重写。原private目录也只在已证明为空/原身份后rmdir，fixture本身验证original.bin精确内容和目录项再移除容器。本机制只是受控QA资源回收，不是产品Unknown清理API，也不是内核原子inode条件unlink，不能宣称抵抗任意root并发替换。

准确收尾后重新读取HDD，独立final及第三次当前只读核验均通过；Root仍Unknown/claim保留，当前基线等于已保存after，活动意图0。保护2346项、11核心、21安装模块及产品f318保持，旧业务全行hash不变。业务累计68回收/29恢复/45清理/26归档；Root/HDD172/159、恢复86、目录98、purge70、archive30。源与HDD仍同device，不签跨物理盘灾备。

## 检查、原失败和证据

- 完整595项回收root检查零跳过，38.766秒；含19新专项。实盘临时NOREPLACE/Unknown及准确目标收尾、不同Subject/复制null/2→0 Outbox/19字段Job、硬链接/替换/属性/HDD/claim/Root/父版本/stage链接/锁占用拒绝通过。
- 8范围、14 CI合同、5份Python编译及Node语法通过。初版测试只预期AssertionError，漏列nofollow stage符号链接实际返回的NotADirectoryError；改为允许OSError拒绝后完整检查通过，未改动或放宽删除保护。隔离测试用native checkpoint与明确Proc/unit替身，不冒充实机证明。
- 准确QA CI `42314529650` 在本轮观察时前端检查、backend、native-postgres、native-valkey成功，installer和六浏览器分片尚未全部终态，未签完整CI成功。本批没有重新部署。

root私有目录0700、文件0600，两批均逐项权限核对并封存manifest：

| 证据 | 文件数（另有manifest） | 冻结源码/真图 | manifest SHA256 |
| --- | ---: | --- | --- |
| `container-recycle-archive-published-unknown-1cf9f8cfc5eb44ab97abc613a5170bca` | 232 | 146 / 12 | `78feb4e34f65c5cd1e97323e37607614a7e638d03ef02f83e0edc3e84178e8d1` |
| `container-recycle-archive-published-unknown-prep-55aabd271202430386b522011455d5c9` | 149 | 146 / 0 | `2e7fe36743a75bc72bf81af8a236453c7d7b9a3ed685307040d934ee5cf840b2` |

只签本次命名空间已发布、完成回执前的页面取消/Unknown/不同管理员ACK组合。其余发布或绑定不确定性及适用故障、长期留存策略、维护连续性、大文件与登录退出稳定性，以及59项完整交付仍待推进；已完成写入不重放。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不计完成。
