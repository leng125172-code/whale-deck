# Prepared写后中断：真实Unknown及不同管理员处置

2026-11-07，产品 `f318d9ab130ecab94668acf9754a6a6e448de92c` 上冻结QA `fe2063a17ef3a0424d3a5ceb77ef76da6e7f7b42`，全新1,048,583字节资源通过Prepared写后中断、Unknown/null、不同管理员明确ACK及准确QA暂存收尾。承接[Prepared候选](CONTAINER_FILE_RECYCLING_ARCHIVE_PREPARED_CUT.md)，未部署、改SQL/Root事实或重放旧Apply；只签本组合。

## 真实写后切点及页面中断

Owner真实Authentik/Vue完成新原回收、清理Ready取消/新清理成功、归档Ready取消，再确认新的目标归档。观察器实际匹配安装态固定apply argv、root UID、MainPID/InvocationID/cgroup和原pidfd，以及八个互异FD：容器根、归档/purge/source三锁、HDD数据、父目录、stage和output。

原Root实际Prepared，完整暂存新inode的字节/SHA256、uid/gid/mode/mtime/link count/xattrs、prepared/pending身份、原source/purge/marker/claim和HDD数据/历史元数据重新核对；目标仍不存在、publishedIdentity仍空。SQL实际预留回执仍Reserved，而Apply入口已记录、最终receipt未取得。写过完整暂存不等于已发布或已复制成功。

父级证明后放行真实页面取消。原helper **5.830486秒**内经原pidfd观测退出，实际页面动作单调时间位于开始与退出之间；不以服务排空或QA收尾杀进程代替产品中断。Root由Prepared变Unknown，其他记录字段不改，暂存新inode保持，未发布目标。没有SIGCONT恢复或重复Apply。

## Unknown事实和不同管理员ACK

Owner页面显示Unknown，原执行Job仍Running/AwaitingContainerRecycleArchiveReceipt，原意图Unknown、Root Unknown、archiveCopied=null、来源/本操作fence为true、barrier保持、lease清空，相关未处理Outbox为2。复制未证明不能显示false或成功。目标执行确认/取消审计各1；作为对照的另一个Ready取消单独计数。

Owner正常BFF/身份源双端退出，原归档读取401，归档/清理/回收hints清空。另一Subject管理员通过可见登录读取原recovery，旧Owned引用404；实际19字段任务详情证明Unknown/null且无重试按钮。管理员在Files异常面板先“暂不处置”，管理写入和ACK均0，父级重读SQL/Root/暂存/HDD与Unknown快照一致；随后仅一次明确ACK POST，原任务Acknowledged/执行Job Failed，barrier释放、Outbox归零、AcknowledgedBy为该不同Subject。19字段实际Jobs和ACK审计对应，Root仍Unknown、复制事实仍null、原claim/暂存/HDD不变。没有取消、重确认、重试、恢复或其他资源写入。

两名管理员分别正常双端退出；9普通/5匿名拒绝通过。原规划Job保持19字段Ready历史，原回收及成功purge SQL/Root/HDD保持，不改成Restored。

## QA暂存收尾边界

产品ACK只处置历史不确定性，不清理文件、不标成功。独立live核验当前SQL、ACK/原receipt、Root/claim、完整暂存和HDD通过后，单独的QA-only函数才处理本轮已知私有stage：

- 限定本轮自有fixture父目录；严格native请求、原Root operation/owner/plan，以及Acknowledged、不同Actor、无lease/barrier、ACK JSON等于原receipt且仍Unknown/null。
- 独占原archive/purge/source三把已有operation锁，持锁重读Root/claim和当前HDD数据及历史；父目录身份/parentAfterStage必须与保存值一致，目标缺席。
- no-follow打开stage/output，stage必须root:root/0700、只有copy.partial，file必须是同prepared/pending inode、nlink1、完整原始属性/内容/hash。重新核entry与FD相符后，仅unlink该entry、fsync空stage、rmdir该目录并fsync父目录。
- 不调用只接受Completed的新副本cleanup，不删除源、目标、HDD或历史，不清claim。删除stage后父目录版本自然变化，Root历史parentAfterStage不回写；不把新父版本当旧证明继续执行。

这些是受控测试夹具约束加官方producer同序锁，stat与unlink并非原子inode条件删除；不声明可抵抗任意root并发替换，也不作为通用产品Unknown清理API。Root/claim/HDD在QA移除暂存后原样保留。随后精确清本轮空旧private和自有容器/夹具，final及第三次独立当前Root/HDD重读通过；不称为产品恢复成功。

## 检查、原件与范围

修订后553项回收root检查零跳过，含14新专项；不同管理员ACK/空值、2→0 Outbox、规划/19字段执行历史、暂不处置0写、硬链接/外来目标/变更HDD/错误ACK/锁占用拒绝及真实临时stage精确清理通过。8范围/14 CI合同及4份Python/1份Node语法通过。初版Node重复声明jobProofs的语法失败、误把scope guard mock的测试失败及重复导入TestCase问题在任何实机运行前修正，准备记录保留；未放宽业务/权限门禁。本批新QA远端CI完整终态尚待核验。

实际unit `whaledeck-qa-archive-prepared-unknown-fe2063a1` / invocation `9db2710b8a4b4e17a4cce269b1c43d92`，原journal报告passed并正常退出，耗时212.934秒；不使用临时unit回收后的默认systemctl值。

root目录0700/文件0600封存，以下数量不含manifest：

| 证据目录 | 文件数 | manifest SHA256 |
| --- | ---: | --- |
| `container-recycle-archive-prepared-unknown-d0fc4458ac934f54a4b7fba075436c38` | 228，含143冻结源码/12真图 | `53d811a69b90c20cd3dedf93b6c8dc1e61baa161e64fbc493604ee24e9a9acab` |
| `container-recycle-archive-prepared-unknown-prep-4586133993ad48baa2142533f54df04e` | 146，含143源码 | `3676aa01d735c11cf741758d2fd75c5a5eac7857c7dbd782bc4cd29daea973c9` |

只增本轮限定业务，最终65回收/29恢复/39清理/20归档、活动0；Root/HDD163/153、恢复83（包含本轮保留claim）、目录98、purge64、archive24。旧业务全行hash、2346保护/11核心/21安装模块/f318d9ab及user NOPASSWD保持。源/HDD同device，不签跨物理盘灾备；没有导出凭据、session注入、route替身、trace或storageState。

继续其他适用写后中断/重启、发布后和真正绑定不确定性、来源/跨写入、claim/重复来源与长期策略、维护连续性、大文件/退出稳定性及59项。本次暂存已完整但未发布的物理证明不能泛化到全部Unknown；整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
