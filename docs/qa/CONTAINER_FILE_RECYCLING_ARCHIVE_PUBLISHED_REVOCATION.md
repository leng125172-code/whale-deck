# 发布命名空间后实时撤权：移组与停用真实整链

2026-11-07，QA `bccc280004e5d6120d1df9bd15543581dc3c37a7` 在已部署f318d9ab/协议1.59上，用两套全新自有容器及临时真实身份，完成移出管理员组与账号停用两项整链。承接[发布后服务重启](CONTAINER_FILE_RECYCLING_ARCHIVE_PUBLISHED_RESTARTS.md)，没有重放旧Apply/ACK，没有产品修改、服务重启或重新部署，master未改动。

## 切点、实际授权变更及因果证明

每项通过真实Authentik→可见Vue完成回收、留存清理Ready取消/实际清理、归档Ready取消与目标确认。Root仍Publishing、publishedIdentity为空，但最终目标已由NOREPLACE发布为原pending/output新inode，内容/属性/SHA完整，stage为空。原pidfd/Invocation/cgroup及容器root、archive/purge/source三锁、HDD、parent、stage、output八个互异FD重新核对；停止后再读原件、claim、HDD及发布后父版本。

仅对本轮生成的`whaledeck-upload-qa-<12hex>`身份变更：匹配实际Subject、UUID、数字PK、用户名和管理员组后，移组只PATCH该用户groups=[]，停用只PATCH该用户is_active=false，返回后另GET确认。lost response不授权第二次PATCH。实际凭据仅在父进程/私有stdin/浏览器内存，不进入argv、env文件、证据、trace或storageState；原角色、普通用户和其他账号不改。

变化前cut lease仍大于45秒，确认后仍大于25秒，execution期限远于10分钟。确认前后原helper均经原pidfd证明仍存活，之后在原cut lease前退出；Unknown结果审计属于原actor、发生在授权确认之后且早于释放浏览器settled信号。没有改时钟、SQL、目标取消或owner logout，不能用自然到期或其他故障替代撤权。

| 场景 | 原helper退出 | 失权页面与接口 | unit / invocation | unit耗时 |
| --- | --- | --- | --- | --- |
| 移出管理员组 | 2.934360959秒 | 原目标403；当前身份200、同Subject/admin=false；forbidden页 | `whaledeck-qa-archive-published-revoke-bccc28000001` / `8155269c1ad64f52a6e2ba6b81124d53` | 209.881秒 |
| 账号停用 | 3.066385633秒 | 原目标403、当前身份403；登录页 | `whaledeck-qa-archive-published-revoke-bccc28000002` / `3a314cd34d9941faa03556034c9c0509` | 209.889秒 |

两项私有面板及原归档dialog消失，三类文件回收/清理/归档hints清空，原owner logout POST均0。失权owner不能继续读取或取消原任务；新恢复管理员在全新浏览器context正常登录，Subject与原owner不同，9个旧意图/来源/by-key引用均404。

## Unknown与不同管理员明确处置

原任务收敛Unknown/Running/AwaitingContainerRecycleArchiveReceipt、1条Outbox及barrier保留；Root Unknown、archiveCopied=null，目标完整新inode与空stage、source/purge/marker/claim、HDD字节和历史元数据保持。原计划与执行标记、19字段实际Jobs安全摘要逐项核验；页面显示未知/未证明且无重新预检并重试按钮。

另一管理员先“暂不处置”，管理写入和ACK均0，前后SQL/receipt/Root/文件/HDD完全相等；随后仅一次实际页面ACK/202，意图Acknowledged、执行Job Failed/Acknowledged、barrier释放及Outbox 1→0。ACK JSON等于原receipt，actor不同；Root/claim/原目标/HDD及复制null事实不变。目标意图及执行Job取消时间null、目标取消审计均0。当前管理员正常双端退出通过：BFF POST302、实际经过身份源、BFF401/身份源403、最终WhaleDeck登录页；原owner的授权失效不冒充正常退出验收。

完整新目标物理存在也不等于历史复制完成；directoryFsyncProven/durablePublicationJournalSaved/terminalCopyJournalSaved仍false，不签目录fsync或Published/Completed持久回执。

## 独立验证、准确收尾与保持

每项独立live在物理清理前重新读取当前SQL/审计、授权证据与时序、Root/claim/source/purge/marker、完整目标及空stage、HDD与original.bin。随后QA-only清理持原三把锁，核Root/claim/HDD和nofollow FD、目标单link/完整snapshot、空stage原身份及parentVersionAfterPublication，再移除本轮目标与空stage；不调用Prepared-only/Completed cleanup，不改写Root历史或历史父版本。原private、普通夹具文件和自有容器也按准确身份/目录项收尾。

final与第三次当前独立核验均通过；移组第三次读取在停用场景启动前完成，移组after=停用before，最终当前基线=停用after。没有新增服务控制，11核心、2346保护、21安装模块/f318和旧业务全行hash保持；user NOPASSWD保留。累计意图73回收/29恢复/55清理/36归档，活动0；Root/HDD187/169、恢复91、目录98、purge80、archive40。源与HDD仍同device，不签跨物理盘灾备。

## 检查、修正与证据

完整622项回收root检查38.945秒、零跳过，含11新专项；8范围、14 CI合同、4份Python编译及1份Node语法通过。初版静态测试误将用于准确核验的Docker inspect也作为服务控制拦截，已收窄为实际控制动作匹配；范围检查另发现上批元数据把固定声明写成近义措辞，恢复“17项移出不算完成”后通过，59项范围未变。没有放宽撤权、Unknown或删除证明。

准确QA CI `42321461678`本轮仍未完整终态，不签整条远端CI成功。候选首推网络超时，读取远端确认旧SHA后重推成功，没有重复实机操作或身份PATCH。

root目录0700/文件0600，逐项权限及manifest封存；文件数另加manifest：

| 证据 | 文件/冻结源码/真图 | manifest SHA256 |
| --- | --- | --- |
| `container-recycle-archive-published-revocation-e224028790e34e90b63822f89bb1c5e9` | 258 / 163 / 13 | `e8c2054b4e8dbd71f61adc7bb03f09d8e72d5367946b9b028cb66f0b815dcdc9` |
| `container-recycle-archive-published-revocation-27adc05a6c024d22b6bb736ad1559b59` | 258 / 163 / 12 | `928c120fbbebcf3d451292f07fd46c4240d5dadeedf7d6d9b9ae5bca2700950b` |
| `archive-published-revocation-prep-54ccdaad02db446aba10f2073ae51acd` | 170 / 163 / 0 | `9281671b9e937f4e1b619dfe7d005c3a704a0f5ee4e9989667aa30617524faea` |

只签这两种代表性发布后身份变化，不扩成完整故障矩阵。继续适用写后正常退出、来源/共享写入保护、成功后claim/重复来源与留存策略、维护连续性、大文件及59项完整交付；17项移出不恢复、不计完成。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持。
