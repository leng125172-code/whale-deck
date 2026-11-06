# 清理真实Unlinked后正常退出、Unknown及跨管理员处置

2026-11-06，产品保持9694af1c，验证c6d88f1a。承接[写后显式取消组合](CONTAINER_FILE_RECYCLING_PURGE_UNLINKED_UNKNOWN.md)，本批用全新资源通过真实unlink后正常BFF/OIDC退出、Unknown和不同管理员明确处置。正常退出没有发送cancel，不混用上批取消时间、审计或两条待处置消息作为证明；只签这一组合。

## 准备与门禁

新增独立正常退出父门禁、业务/ACK证明、真实浏览器、独立核验器和6项针对性检查。165项清理相关Linux root工具通过、零跳过，14 CI合同/8范围和Node语法通过；构造SQL/UI检查不代替真实身份、PG、Root和HDD。

保持原五FD边界：原helper固定apply argv、Root UID、systemd Invocation/cgroup，以及本轮容器根、原执行锁、原来源锁、当前attested HDD数据和实际已unlink原inode的FD。暂停后Root Removing、来源Purging/marker Deleting、pendingIdentity存在但unlinkConfirmed=false；原FD nlink=0、模式/UID/GID/size/mtime及完整SHA/xattrs匹配，ctime不早于历史身份，原路径/.trash/.purging都缺席。重新attest归档后才放行。SQL Applying与预留/Apply入口曾记录独立保留，不等于事务完成。

原pidfd观察先于页面放行。浏览器只对真实导航POST的BFF退出请求写入带原意图/执行ID的私有标记；父进程必须在观察此请求时确认原pidfd仍活，再于20秒内观测同一进程退出。按钮点击或预写“将退出”不足以通过。意图/执行Job的取消时间均须null、双Job取消审计0、页面cancel POST0；Unknown只保留原执行的一条待处置消息。

若错过切点或任何绑定/证明失败，只可失败并保留现场；不重放Apply或恢复暂停的破坏性操作。HDD与原生历史保留，不从proc FD复制原件回路径，不把已unlink或ACK报为事务成功。

## 全新资源实机结果

unit whaledeck-qa-purge-unlinked-logout-c6d88f1a / Invocation b6ad501d69d64e5daef786f40a7afe7f，最终active/exited、Result success、ExecMainCode1/ExecMainStatus0。冻结135份验证源码，安装helper逐字节匹配9694af1c对应产品；六组件仍9694af1c，无安装覆盖或Worker/Agent重启。

真实Authentik/Vue回收新1,048,583字节文件，父进程核对来源Root/HDD/留存及清理Ready后，才允许明确确认清理。实际暂停时Root Removing、第五FD链接数0、完整字节/属性一致，三个源名称缺席；尚未写入持久化unlink确认。原用户安全关闭详情，从用户菜单正常BFF/OIDC双端退出。真实BFF退出POST被观察到时原pidfd仍活，6.289177秒内原进程退出，随后从原会话读原任务401、原回收/清理hints清空。

该退出触发停止和只读调和，原Root/marker为Unknown、来源为Purging、HDD为Trashed；意图/Job取消时间null、取消审计0、页面cancel POST0。不同Subject的真实管理员正常登录，原所有者入口404，从专用管理员恢复元数据GET轮询并读到同一原执行Unknown、purgeApplied=null。原执行Job Running/AwaitingContainerRecyclePurgeReceipt，completed为空、SQL围栏保持、租约清空，原执行的一条Outbox未处理；原规划Job完整Ready历史保持。没有将它写成Canceled/Aborted或清理成功。

实际Jobs的16字段Unknown摘要保持“未证明”，无通用重试。不同管理员选择“暂不处置”后ACK与管理写均0；父进程逐值重读原Unknown SQL/Job、回执、原生记录、物理命名空间和HDD保持，才放行下一次决定。随后只对原意图一次明确ACK，意图Acknowledged、原Job Failed/Acknowledged、完成时间写入、SQL围栏与该条Outbox释放，ack审计一次且actor不同于原操作者。

处置后Root/marker仍Unknown、来源仍Purging、HDD仍Trashed，pendingIdentity/unlinkConfirmed和原receipt不变，purgeApplied仍null。它只结束原历史不确定性，没有再删除、恢复或承认事务成功。Jobs Acknowledged摘要和未知警告保持“未证明”，新管理员随后也正常BFF/OIDC双端退出。两次真实退出均有BFF 302/303、身份源end-session导航、两侧会话清除与最终登录页回执。

## 独立核验、收尾与范围

在自有容器仍在和准确清理后分别执行独立进程核验，再第三次最终当前HDD重读，全部通过。只有证明三个源名称缺席、private目录身份为原计划且为空后，才移除空目录；fixture机制再核对其唯一既知ordinary测试文件和容器，完成准确清理。被unlink原文件未重建，没有生成恢复意图，也没有普通或归档恢复。HDD完整归档与全部历史保持；清理后重新取得当前attested只读FD，完整data/metadata/身份/属性/存储与原Trashed副本一致。

旧48回收/29恢复/12清理完整行SHA、旧Root/HDD、2346保护及11核心healthy保持。最终49回收、29独立恢复、13清理、活动0；SourceRoot/HDD112/109、恢复Root80、目录Root98、PurgeRoot26。真实普通9拒绝、匿名5读取401，另列原会话401和不同管理员旧引用404。无旧任务重放、数据库/时钟修改或真实外部消息。

成功证据container-recycle-purge-unlinked-logout-83ed8f2debfb429db6947ab6a5641012，135冻结源码/5真PNG，189条文件散列另加manifest，摘要1d817fa619e4ad4e07aec5a3aa4f1b6dfd5b45e6251eecd4a51e90794ac18d11。原件Root目录700/文件600，抽看320宽写后暂停页及处置Job，复制到独立user私有目录的两图与原件SHA相同。暂停页仍是当时Applying轮询状态，不作为原用户看到Unknown的证据；Unknown/ACK展示由不同管理员的实际Jobs证明。

准备证据container-recycle-purge-unlinked-logout-candidate-6947a296e22a4e2ebc64f07ce854cf6a为8条文件散列另加manifest，摘要57f3f77b2743a1e8f497700e24304dd136fed0e5323c67672f98e22c5aba7e32。所有散列逐项重算，凭据未进入源码、日志或项目图谱。

准确8bfd0803的CI42218612906已有前置/后端/原生依赖/安装成功，记录时浏览器分片仍运行，不提前签完整终态；本批新CI同样继续核对。其他写后撤权/重启、来源或实例变化/共享写入、留存策略/归档恢复、维护连续性与59项仍待完成。源与HDD同device不计独立物理磁盘灾备。正式整项0/59、阶段0/6、工程量粗估25%–35%，酒馆/Live2D功能0%保持；17项移出不算完成。
