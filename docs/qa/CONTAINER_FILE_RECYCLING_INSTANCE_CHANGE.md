# 容器回收同CID实例变化Unknown及跨管理员处置

2026-11-05，产品保持58b8174，门禁05fc50c4。承接[源路径消失Unknown](CONTAINER_FILE_RECYCLING_UNKNOWN.md)，在全新自有容器/1,048,583字节普通文件上，完成“已验证HDD、源未移除时重启该容器，随后正常退出”的实机组合。本批没有重启宿主/Agent/Worker或其他核心服务，没有覆盖发布、修改时钟/SQL或重放旧操作。

## 实例故障与原件证明

原管理员经真实Authentik/Vue完成异步Ready、唯一确认。在Preparing/historyVerified=true且源未移除、私有空目录已绑定时，按原SQL执行ID/完整计划、MainPID/Invocation/cgroup、容器根与原操作锁双内核FD及原pidfd暂停准确helper。

故障前核对自有目录/父inode、源完整快照、精确容器ID/验证Label/镜像/挂载、非root用户、无网络/只读根、全部cap丢弃及资源预算。除原意图/执行外，未完成Job、终端、宿主锁及12类文件业务活动/保护/租约为0。只允许对原自有CID发一次docker restart --timeout 1，调用前记录尝试标记；丢响应不重试，不提供任意服务或目标输入。

该CID、名称、镜像、挂载、用户和全部安全/预算配置保持；PID1417914→1419903，StartedAt由2026-11-05T14:26:25.682392337Z变为2026-11-05T14:26:56.279410361Z。原instanceId保持在原请求/SQL执行中，新PID:StartedAt单独记录，不把新实例冒充原目标，也未重建容器。

分别从宿主自有目录和/proc/新PID/root/work读取同一源文件。读取新命名空间前后重新inspect精确CID及完整frame、打开新pidfd、核对work的设备/inode与宿主目录一致，源九字段身份、完整SHA、user属性完全等于原件。只重启该测试容器，11核心快照和两宿主保持。

## 停止、Unknown与明确处置

原helper退出观察从自有容器重启命令前开始并发计时，实际7.723033秒退出。重启结束时原helper仍活，随后页面正常BFF/IdP退出；此时间覆盖重启、新实例核验和正常退出准备，不是单独的容器重启耗时，也不签“实例变化单独主动中断”场景。没有取消POST、第二次确认或Apply。

产品保持原执行ID/RootStartedAt/原instanceId，保存Unknown/原Job Running、completed空、保护保留/租约清除，原执行Outbox1；Root仍Unknown，sourceRetained/sourceRestored为null。原路径中的源文件实际仍在且新命名空间读到完整原件，不能据此把原目标绑定的未知事实改成false或回收成功。HDD完整原件/摘要已验证，captureState=Pending保持。

原会话退出后旧GET401、引用清空；另一临时管理员在隔离上下文正常登录，旧登录所属ID404，通过当前管理员恢复接口和真实文件页读取原任务。实际Jobs先显示原执行Running及未知安全摘要。320×600异常面板核对原ID/源事实未证明/HDD已验证，确认和“暂不处置”均完整可见。

先选“暂不处置”，ACK及管理写入0；父进程重新读取完整意图/Job SQL快照、Outbox计数、Root/HDD字节和宿主/新命名空间原件，与首次Unknown证明完全相等。之后才允许另一管理员真实点击唯一确认，ACK POST202，将原任务变为Acknowledged、原Job Failed/phase Acknowledged、保护释放/Outbox0。原请求/计划/执行绑定、Root Unknown、null源事实、HDD Pending和全部原件保持；不重新回收、恢复或删除原件。

唯一审计的原执行ID、不同Actor、Result=Unknown、精确容器Target及非空Trace核对通过。实际Jobs再显示同一执行的已处置安全摘要、无通用重试按钮；两次正常双端退出、普通8拒绝/匿名4拒绝与临时账号删除确认通过。

ACK后再次确认同一新实例仍运行且配置/frame保持，宿主和新命名空间源快照均保持，观察时间晚于ACK完成。清理前独立进程重新读取实际SQL/审计、Root/HDD原始JSON、完整HDD数据及两个视图下的原文件；独立通过后仅精确清理本次测试文件、空私有目录和容器，再独立核对永久原件和资源缺席。HDD与源同设备，不签异盘灾备。

Root24→26/HDD27→29；2346保护文件、11核心healthy、两宿主及指定全部旧业务保持，旧失败/未验证partial保留。仅自有测试容器PID/StartedAt变化，清理前后的独立证明没有用宽泛基线替换隐藏其他漂移。

## 测试和证据

新增8项实例证明专项，关联86项回收工具以ResourceWarning为错误通过，范围8项、Python编译与Node语法通过。合成frame负测覆盖CID/镜像/挂载/用户/特权/网络变更、PID或StartedAt不变、错误ACK/重复尝试、源/新命名空间/核心漂移、其他活动工作和过早ACK后观察；测试自身不启动服务故障，安装态整链另由真实门禁执行。旧验证文件没有为本场景改写。

证据目录deploy/artifacts/container-recycle-instance-853557999a2a41a39dc15ba5c406c805，共143文件、102份冻结验证源码、五张真实页面截图。EVIDENCE_SHA256.json摘要：d6af96cc63817ff96454fbf6bfbd8f9ae18581cb3327607d3f10472682b40eea，逐文件重算通过；证据为主机私有权限，不提交凭据或原始内部回执。

成功unit whaledeck-qa-recycle-instance-37605a7fcbb7，Invocation f58b331f114e4648a64d930fa640db7d，准确Result=success/ExecMainStatus=0。原helper PID1419468/Invocation bbd4f29be04349b7a24003dd17758805；意图38cdc378-d1ab-4698-a6e6-ccdaa77a8b33、唯一执行241b9d8f-ccd7-5685-935a-7ff91a12fd94。

仅签同CID实例变化+正常退出及不同管理员撤销/明确处置，不签实例变化单独主动中断、跨写入延期/释放、移除后边界、独立恢复业务、长期保留/彻底清理或完整59项交付。新候选准确远端CI终态仍待核验；整体25%–35%粗估、整项0/59、完整阶段0/6、AI子计划0%及旧代码量快照保持，17项移出不算完成。
