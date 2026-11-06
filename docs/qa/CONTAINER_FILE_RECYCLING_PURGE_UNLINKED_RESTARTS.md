# 真正Unlinked写后Worker强杀/自然租约与Agent重启

2026-11-06，产品仍9694af1c，验证221c74b2。承接[写后实时撤权](CONTAINER_FILE_RECYCLING_PURGE_UNLINKED_REVOCATION.md)，两套全新资源分别通过unlink后Worker SIGKILL与旧租约自然重领、固定Agent服务重启。这里应收敛到Unknown，不能套用写前Reserved→Aborted/Failed证明，也不代表全部Unknown/共享写入或归档恢复完成。

## 准备和范围

新增专用写后重启/自然租约证明、父门禁、真实浏览器、独立核验器及8项检查。179项清理相关Linux root检查全通过、零跳过，14 CI合同/8范围和Node语法通过。初轮179项中两项构造正向基线缺少预期精确新增文件而失败，保留177/2及针对性6/2记录；仅补齐夹具，未放宽运行时精确作用域断言。

固定服务控制复用已验收的restart实现：只豁免本轮规划及执行Job，14类文件业务表的其他活动/围栏/租约、终端、主机锁和其他运行中的WhaleDeck临时任务必须为空。匹配自身MainPID/Invocation/cgroup，只有范围证明和原pidfd存活确认后，才记录故障尝试并控制固定Worker容器或Agent服务。Worker只向同一ID发送SIGKILL并按实际状态启动，不重建容器或更换镜像；Agent只重启固定服务并验证实际apphost/sourceRevision。错误路径修复本轮实际尝试的服务并保留未决现场。

五FD切点保持原helper固定argv/Root UID/Invocation/cgroup与容器根、两把原操作锁、当前attested HDD数据和nlink0原inode的绑定。Root Removing、pendingIdentity存在、unlinkConfirmed=false，来源Purging/marker Deleting，三个名称缺席，原字节/SHA/属性一致；SQL Applying与预留/Apply入口事实不改。不同于写前，故障后不能resume或重做unlink，也不能因文件名已消失就记清理成功。

## 写后Worker强杀与自然租约

unit whaledeck-qa-purge-restart-221c74b2452e / Invocation 7cfb63959cf54e3ca4056dfa11c7f660，最终active/exited、Result success、ExecMainCode1/ExecMainStatus0。新1,048,583字节文件经真实Authentik/Vue回收、清理Ready及明确确认，父进程逐步核对后才故障注入。Root真实unlink切点确认后，Worker被SIGKILL，同一容器/镜像显式启动，原helper在0.110948秒内退出。

不改SQL或时钟，真实等待旧租约87.070058秒，取得40个到期前完整读窗口；跨到期的读窗口不算样本。逐样本原token/期限、SQL Applying/Running与围栏、不可变来源/命令/计划/MAC/预留和Apply入口、Root/marker/source原始字节、三名称缺席及当前HDD副本保持。原Root仍为Removing或只读调和后的Unknown，来源仍Purging，不能接受Reserved/Aborted或新发布。

原lease到期21:17:51.775678+08:00，原执行首次Unknown结果审计21:17:53.434315+08:00，actor为原操作者且该审计恰一次；独立核验要求审计不早于原expiry，父进程settled实际观测时间不早于该审计。Unknown的completed仍为空，未用伪造完成时间代替审计。到期后同一意图/执行/原生operation ID只观察并记录Unknown，不能重新Apply或把原文件名重新创建。

## 写后Agent重启

unit whaledeck-qa-purge-restart-221c74b2452f / Invocation 6a840c96302342049e556546fac81345，最终同样success/ExecMainCode1/ExecMainStatus0。另一全新资源在相同真实五FD Unlinked窗口暂停后，仅固定Agent重启，原helper0.012095秒内退出，新Agent实际apphost/sourceRevision保持9694af1c对应版本。维护进程身份保持，重启前/期间/之后152采样全部HTTP200/healthy/Ready/no-store；该窗口不补签发布升级的整体维护连续性。

Worker仍用原任务数据收敛到Unknown，首次结果审计21:22:12.144724+08:00。该项没有人为修改租约，也不把它冒充Worker强杀的自然租约窗口。两项均核验原用户在故障结果前仍为同一管理员、logout/cancel POST0，两处取消时间null、双Job取消审计0；写后Unknown不是用户取消或Failed/Aborted。

## Unknown、不同管理员处置及收尾

两项都保留原规划Job完整Ready历史，原执行Job Running/AwaitingContainerRecyclePurgeReceipt，意图completed空、SQL围栏保持、租约清空、一条原执行Outbox待处置。Native Root/marker Unknown、SourcePurging、HDDTrashed，purgeApplied=null。原用户页面主动查询原任务并实际显示“清理结果未知，保护已保留”，在此之前确认auth/me同Subject且管理员true，然后才正常BFF/OIDC双端退出；不是用退出或权限失效触发本次服务故障。

另一Subject的真实管理员正常登录，原所有者入口404，仅从管理员元数据读取同一Unknown，真实Jobs16字段摘要显示“未证明”且无通用重试。先选择暂不处置，管理写0，父进程逐值重读原SQL/Job/receipt/记录/物理命名空间/HDD不变后，才放行一次明确ACK。原意图Acknowledged、原Job Failed/Acknowledged、完成时间写入、SQL围栏与原消息释放，ack审计一次且actor不同。

ACK后Root/marker仍Unknown，来源仍Purging、HDD仍Trashed，原receipt、pendingIdentity与unlinkConfirmed事实保持，purgeApplied仍null。没有再次删除、rename、恢复或承认事务清理成功。处置后实际Jobs摘要保留未知警告，新管理员再正常BFF/OIDC退出。每项另验普通9拒绝、匿名5读取401；原退出后的401与另一管理员旧引用404分列。

各在自有容器仍在、准确清理后独立核验，再第三次当前HDD重读，全部通过；第一项封存完成才启动第二项。确认三个名称缺席、private为原身份且空后才移除空目录，fixture机制再核对清理其唯一ordinary测试文件和自有容器；被unlink原件未重建，没有恢复意图，不把HDD完整副本当作已执行归档恢复。重新取得attested只读FD确认HDD完整data/metadata、身份/属性/存储与原Trashed副本一致。

## 保持、证据与剩余工作

各冻结145验证源码并匹配安装helper字节，六组件仍9694af1c，没有安装升级、发布覆盖、主机重启或真实外部消息。每项旧业务保持，合计再核验初始51回收/29恢复/15清理完整行SHA与精确作用域；只原Worker同ID/镜像StartedAt、Agent PID允许变化，维护进程/2346保护/11核心healthy与所有旧Root/HDD保持。最终53回收、29独立恢复、17清理、活动0；SourceRoot/HDD124/117、恢复Root80、目录Root98、PurgeRoot34，未新增恢复。

Worker私有证据container-recycle-purge-unlinked-restart-b53dea6720074d3aa332910228c87500为207条文件散列另加manifest，摘要ff58d6f98862892353af7a058ae916d9bff2039819ad94e45d8e8383e8e62f30；Agent证据container-recycle-purge-unlinked-restart-e27783974ba945e38cc7e8ec4fff701d为203条另加manifest，摘要c91b2e32d20986749dd51abe4402accad553a2e20b74f2c9f4eb61fa5c60c536。各145源码/5真PNG，共10图；抽看320宽原Unknown页与处置Job，Root目录700/文件600保持，只逐字复制两图到独立user私有目录并确认SHA。

准备证据container-recycle-purge-unlinked-restart-candidate-a02da3f783624cf7a8a07e95bb498f71为9条散列另加manifest，摘要4758daee538d3c2310fc26e693d8656e882ee0517f2229901ef727d08fa07715，包含构造夹具初错。散列逐项重算，凭据未进入源码、日志或图谱。

准确0658c7ef的CI42222633155已有后端/原生依赖/安装/前置及多数浏览器分片成功，记录时完整终态仍待核对，本批新CI亦不提前记绿。继续其他Unknown来源/实例变化与共享写入、留存策略/归档恢复、维护连续性及完整59项；源与HDD同device不能计独立物理磁盘灾备。正式整项0/59、阶段0/6、工程量粗估25%–35%，酒馆/Live2D功能0%保持；17项移出不算完成。
