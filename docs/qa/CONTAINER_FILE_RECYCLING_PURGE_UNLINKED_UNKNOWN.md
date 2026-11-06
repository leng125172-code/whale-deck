# 清理真正Unlinked写后取消、Unknown与跨管理员处置

2026-11-06，产品仍9694af1c，验证16635e53。承接[Reserved重启与自然租约](CONTAINER_FILE_RECYCLING_PURGE_RESTARTS.md)，本批用全新自有资源通过真实unlink后的页面取消、Unknown及跨管理员明确处置整链。这个切点不能用写前Reserved、单纯文件名消失或SQL Applying替代；只签这一组合，不代表所有写后/Unknown故障完成。

## 新切点与候选验证

新增独立五FD观察器、Unknown/ACK业务证明、父门禁、真实浏览器、独立核验器和10项检查。159项清理相关Linux root检查通过、零跳过，14 CI合同/8范围和Node语法通过；初轮构造SQL夹具缺少version导致9通过/1错误，修正后通过，错误记录保留。临时原生测试使用显式Unlinked检查点和真实临时文件，并非安装态业务证据。

原生顺序是Root Removing持久化，打开本次.purging FD，unlink+fsync，仍通过这个FD检查nlink=0及完整字节/属性，关闭后才记录Removed/unlinkConfirmed=true。本观察器先绑定固定apply argv、Root UID、systemd Invocation/cgroup、本轮容器根/原执行锁/原来源锁/当前attested HDD数据四个内核FD，再等待第五个FD：原留存inode已经nlink=0，size/mtime/模式/UID/GID与原计划一致，ctime不早于预删除记录。

打开原pidfd后再次绑定，暂停后重读完整计划和Root Removing/pendingIdentity/unlinkConfirmed=false、来源Purging/marker Deleting，核对原路径/.trash/.purging三名称缺席。只通过已绑定暂停进程的/proc FD只读打开，重复fstat并计算完整SHA/xattrs，确认仍是原inode与原字节；最后重新attest当前HDD后才向浏览器放行。五个内核对象必须互不相同。这个FD读取不恢复目录项，不复制原件回路径，也不证明原生后续校验或清理事务已经完成。

失败只终止严格绑定且已暂停的原进程，不恢复破坏性Apply；错过窗口就失败且不重放。任意其他PID、普通已命名文件或仅路径缺席不能通过这个切点。

## 新资源真实结果

unit whaledeck-qa-purge-unlinked-16635e53 / Invocation 345fc5ab607146cbb0abc0a197929915，最终active/exited、Result success、ExecMainCode1/ExecMainStatus0。冻结131份验证源码并匹配安装helper字节。六组件仍9694af1c，没有覆盖安装、Worker/Agent重启或数据库/时钟修改。

新1,048,583字节普通文件通过真实Authentik/Vue回收，父进程逐阶段核验来源Root、HDD、留存和清理Ready，才允许明确确认。实际暂停点Root Removing，来源Root Purging、marker Deleting，pendingIdentity存在但unlinkConfirmed=false；原helper第五个FD的nlink=0，原inode/完整SHA/属性一致，三个名称均缺席。SQL仍Applying，reservation及Apply入口已持久化，回执尚缺席。已证明发生unlink，不能据此推断事务清理成功或报告原件仍在。

父进程在页面动作前开始原pidfd退出观察。页面向原意图仅请求取消一次，6.232066秒内观察原进程退出，浏览器动作的单调时间落在观察起点与退出之间。原生随后只观察/调和到Root Unknown、marker Unknown；不会重做rename/unlink。原用户真实清理弹窗显示“清理结果未知，保护已保留”，API/SQL purgeApplied=null，来源仍Purging、HDD仍Trashed，原规划Job完整Ready历史保持。

Unknown保持非终态：原执行Job Running/AwaitingContainerRecyclePurgeReceipt，意图completed为空、围栏保留、租约清空；显式取消对应意图与Job时间均有值、执行取消审计一次，原执行及取消两条Outbox均待处置。它不是Canceled/Aborted，也不能把null写成false或true。

原用户正常BFF/OIDC双端退出，旧入口401，原回收和清理hints清空。不同Subject的真实管理员正常登录，原所有者入口404，仅通过管理员元数据读取原Unknown；真实Jobs详情16字段显示Unknown与“未证明”，没有通用重试。选择“暂不处置”时管理写及ACK都为0，父进程重新读取并逐值验证原Unknown SQL/Job/回执/原生记录/物理命名空间/HDD保持，才放行下一步。

该管理员随后仅明确确认一次处置原意图。原意图Acknowledged、原执行Job Failed/Acknowledged，完成时间写入、SQL围栏和两条Outbox释放；ack审计一次且actor为不同管理员。原Root/marker仍Unknown、来源仍Purging、HDD仍Trashed，原receipt和pendingIdentity/unlinkConfirmed事实均保持，purgeApplied仍null。处置只结束原历史不确定性，没有再次清理、恢复或承认清理成功。实际Jobs的Acknowledged摘要和警告保留“未证明”，新管理员随后正常双端退出。

## 准确收尾、保持与证据

两次独立核验分别在自有容器仍存在及准确清理后执行，再第三次最终重读，全部通过。只有证明三个源名称都缺席、private目录为原身份且为空后，才移除这个空目录；fixture机制再核对并移除其唯一既知ordinary测试文件与自有容器。原被unlink文件未重建，没有执行普通或归档恢复。HDD数据、metadata和全部原生历史保留；清理后重新获取当前attested只读HDD FD，完整内容/身份/属性/存储与原Trashed归档一致。

全部旧47回收/29恢复/11清理完整业务行SHA、旧Root/HDD、2346保护与11核心healthy保持。最终48回收、29独立恢复、12清理、活动0；SourceRoot/HDD109/107、恢复Root80、目录Root98、PurgeRoot24。本轮没有新增恢复意图。真实普通9拒绝、匿名5读取401，另有退出后的原入口401与不同管理员404。

私有成功证据container-recycle-purge-unlinked-unknown-e6a71c40ddae4aefa5d6205340ea4ee4，131冻结源码/5真PNG，184条文件散列另加manifest，摘要228bfb6d9b00c11154ed41df185bfb7c527dedc58b2ac6307f92a9160389aed6。目录Root700/文件600；抽看320宽原Unknown弹窗及处置后Job，仅逐字复制两图到独立user私有目录并核对SHA。准备证据container-recycle-purge-unlinked-candidate-f6bc11c50f784cf49459ad6a78e4efce为10条另加manifest，摘要7bedd16d21d21b67e1e06d3a176836e1d244a13a1994ab69e4b836b82c4b5552，包含构造夹具初错。散列逐项重算，凭据未进入源码、日志或图谱。

准确fefa74d3的CI42214467139已固定12项全success；本批新提交仍待新准确CI终态。继续其他写后中断/重启/撤权、Unknown来源或实例变化/共享写入、留存策略/归档恢复、维护连续性和完整59项。源与HDD同device不能计独立物理磁盘灾备。正式整项0/59、阶段0/6、工程量粗估25%–35%，酒馆/Live2D功能0%保持；17项移出不算完成。
