# 真正Unlinked写后实时移组、账号停用与Unknown处置

2026-11-06，产品仍9694af1c，验证0ba57b99。承接[写后正常退出组合](CONTAINER_FILE_RECYCLING_PURGE_UNLINKED_LOGOUT.md)，本批两套全新资源分别通过unlink后的移出管理员组和停用账号。原用户没有主动退出或取消，不能拿写前Reserved、普通注销或显式取消的事实替代。只签这两个组合，完整矩阵仍待闭环。

## 准备和边界

新增父门禁、真实浏览器、独立核验器与6项检查；共享已验收的零取消Unknown业务事实证明。171项清理相关Linux root工具、12项临时身份范围检查通过，全部零跳过；14 CI合同、8范围及Node语法通过。构造SQL/UI/身份检查不代替工作站实机证据。

继续使用五个互不相同的内核FD与原pidfd绑定：固定apply程序、Root UID、Invocation/cgroup、本轮容器根/原执行锁/原来源锁/当前attested HDD数据及nlink=0的原inode。暂停后Root Removing、来源Purging/marker Deleting、pendingIdentity存在且unlinkConfirmed=false，三个源名称缺席；完整原字节/属性匹配，SQL Applying与reservation/Apply入口保留。只说明unlink已发生，尚不能证明清理事务完成。

权威变更只针对该轮生成的QA账号。OwnedAuthentikRevocation精确匹配随机QA用户名、Subject UUID和数字PK，核对active及管理员组，写前重新读取，只允许一次groups=[]或is_active=false并回读确认，丢失响应也不重试。移组仍active；停用保留原组。原pidfd在变更前、确认后均须仍活，再于20秒内退出；执行期限比权威确认晚十分钟以上。没有恢复原用户的组/active，最终由临时账号机制准确删除本轮账号。

原用户logout POST=0、清理cancel POST=0，两处取消时间null、双Job取消审计0；Unknown保持一条原执行待处置消息。不同Subject管理员先只读原Unknown和任务摘要，暂不处置时管理写0，父进程重新核对原记录后才允许一次明确ACK。原件不重建，不从HDD生成恢复副本，也不重放Apply。

## 两套新资源的实际结果

| 场景 | 原pidfd退出 | 原用户页面/身份 | 原结果 |
| --- | --- | --- | --- |
| 写后移出管理员组 | 6.133427秒 | 同Subject仍登录、非管理员、管理接口403、无管理权限页 | Unknown / purgeApplied=null |
| 写后停用账号 | 6.231488秒 | 原会话/身份拒绝，回工作站登录页 | Unknown / purgeApplied=null |

移组unit whaledeck-qa-purge-unlinked-group-0ba57b99 / Invocation 6f48d649e6b4442f941d5a644bbbee2c；停用unit whaledeck-qa-purge-unlinked-disabled-0ba57b99 / Invocation 2cdb94797d114c5a8d96cf598cd028b3。两者最终active/exited、Result success、ExecMainCode1/ExecMainStatus0，各冻结139份验证源码并匹配安装helper字节。没有覆盖部署、Worker/Agent重启、数据库/时钟修改或真实外部消息。

每项通过真实Authentik/Vue回收新1,048,583字节文件，父进程逐步核对来源Root/HDD/留存及清理Ready，才放行明确确认。确实暂停在原inode FD nlink0、Root Removing且未持久确认的unlink后窗口。仅权威源变更后，原helper在上述限时内退出；Worker只观察/调和到Root/marker Unknown、来源Purging、HDD Trashed。意图及Job取消时间null、取消审计0、页面取消0、原用户主动退出0；不是Canceled/Aborted，清理事实不填false或true。

移组和停用分别验证实际auth/me与原接口拒绝、页面回退和敏感面板/原回收与清理hints清空。原浏览器只关闭本地context，不通过logout代替撤权。另一位真实管理员登录，原所有者引用404，通过管理员恢复元数据GET核对同一原执行Unknown；原Job Running/AwaitingContainerRecyclePurgeReceipt、completed为空、SQL围栏保持、租约清空，一条原执行Outbox待处置，原规划Job完整Ready历史保持。

每项真实Jobs的16字段Unknown摘要显示未证明，无通用重试。恢复管理员先点暂不处置，ACK和管理写均0；父进程逐值重读SQL/Job/receipt/Root/marker/物理命名空间/HDD保持，再允许一次确认。之后原意图Acknowledged、原Job Failed/Acknowledged、完成时间写入、SQL围栏与该条消息释放；ack审计恰一次且actor不同于原操作者。

ACK后原Root/marker仍Unknown、来源仍Purging、HDD仍Trashed，原receipt、pendingIdentity、unlinkConfirmed和完整归档不变，purgeApplied仍null。处置不承认清理成功，没有再删/rename/恢复。处置后实际Jobs摘要与未知警告保持未证明。恢复管理员正常BFF/OIDC双端退出；原用户仍是零主动logout。每项另验真实普通9拒绝、匿名5读取401。

## 独立核验、准确收尾与保存

每项在自有容器仍存在及准确清理后分别独立核验，再第三次当前HDD重读，全部通过；第一项重核和封存完成才开始第二项。确认三个源名称缺席、private目录原身份且为空后，才移除空目录；fixture机制随后核对并清理其唯一既知ordinary测试文件及自有容器。被unlink原文件没有重建，没有新增恢复意图；HDD完整data/metadata、身份/属性/存储及历史原样保留。

两项分别保持旧业务，合计再核验初始49回收/29恢复/13清理完整行SHA、2346保护和11核心healthy保持，仅增加两个精确来源/清理作用域。最终51回收、29独立恢复、15清理、活动0；SourceRoot/HDD118/113、恢复Root80、目录Root98、PurgeRoot30。既有完整/partial归档和所有原生证据均保留。

移组私有证据container-recycle-purge-unlinked-revocation-2644b35671ad48d4adc8c0a1da067c00，193条文件散列另加manifest，摘要43dce0c316013f19846d2ffbefbcd7b265fe24d24e6d749196d81b0a78c6c9f8；停用证据container-recycle-purge-unlinked-revocation-1101834d471f4835bee486f6e96e5c23，同为193条另加manifest，摘要17c38550773ccc63d1b485d3d1b3407e7f65d572436c298180f39a8018735483。每项139冻结源码/5真PNG，共10图；抽看两种320宽失权页和处置Job，原件Root目录700/文件600，复制到独立user私有目录的四图SHA相同。

准备证据container-recycle-purge-unlinked-revocation-candidate-3b11381cafed49e78b6ddd0552658dad，7条文件散列另加manifest，摘要f32c3a51c78a1ea7e64964aed2703b3448889fd9750654b0641426984249a624。所有散列逐项重算，凭据未进入源码、日志或图谱。

准确7dbb5fa3的CI42220370516已有后端、原生依赖、安装、前置等成功，记录时部分浏览器分片仍在运行；不提前记完整CI通过。本批新CI完整终态、其他写后重启/Unknown来源或实例变化/共享写入、留存策略/归档恢复、维护连续性和59项继续。源与HDD同device不计独立物理磁盘灾备。正式整项0/59、阶段0/6、工程量粗估25%–35%，酒馆/Live2D功能0%保持；17项移出不算完成。
