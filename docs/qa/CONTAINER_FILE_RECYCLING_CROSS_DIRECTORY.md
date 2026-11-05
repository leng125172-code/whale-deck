# 回收Unknown下跨容器目录自然延期与ACK后释放

2026-11-05，产品保持58b8174，门禁2507c6a5。使用两只全新自有、无网络/只读根/非root容器，完成“回收Unknown保留保护 → 另一容器真实登记目录 → 四次自然延期 → 不处置保持 → 明确ACK → 同一目录任务执行”的实机整链。承接[源路径消失Unknown](CONTAINER_FILE_RECYCLING_UNKNOWN.md)，不重放旧操作，也未重启核心服务、覆盖发布、修改SQL/时钟或回滚。

## 原Unknown与另一容器登记

原管理员在第一只容器通过真实Authentik/Vue生成异步Ready、唯一确认。精确原执行ID/完整计划、MainPID/Invocation/cgroup、双内核FD及pidfd绑定后，在Preparing、HDD已验证/源未移除切点暂停原helper。QA以原子不覆盖重命名保留源inode/字节属性，再正常BFF/IdP退出；原helper1.860762秒内退出。

原任务/Root保持Unknown，Job Running、源事实null、保护保留、原执行Outbox1。实际移位原件与完整已验证HDD保持。另一名临时管理员在隔离浏览器上下文正常登录，读取原任务，同时在不同CID的第二只容器通过真实目录界面生成一次计划、登记一次work/文件回收保护后的目录，模式0750。Origin与Idempotency-Key、原计划Token的SHA、目录命令/原实例/Actor/Job ID全部绑定；无取消或上传。

目录登记返回202/Pending、实际页面“等待执行”。不能因另一容器就绕过回收写入保护。目录业务源码在StartedAsync检查任意ContainerRecycleIntents.PublishBarrier，Busy后自然Defer，本批通过真实持久化/Worker/Agent/页面验证该行为，未暂停Worker或手动推进期限。

## 四次真实自然延期

四次采样对应目录版本2、4、6、8，实际UTC观测时间：14:58:43.308000、14:58:50.263866、14:58:57.171594、14:59:03.993528。各次不同next_attempt_at_utc自然前进，间隔超过5秒，后一次观测晚于上一次应重试时间；不是修改版本或注入时钟。

每次目录行仍Pending、Job Queued、原请求/计划/幂等/登录/容器/路径/Actor字段保持，root_started_at/completed为空、保护false、接受审计1、终态审计0、Outbox待处理。允许短暂claim租约，只有租约归还、版本更新且下一尝试在未来时才计入一次延期，不能把正常claim误报成执行。

同时检查目标目录缺席、原目录Root JSON回执缺席，第二只容器仅有原original.bin且完整九字段/字节保持；原回收完整意图/Job SQL快照、Outbox1、Unknown保护、Root原始字节、HDD与移位原件完全保持。只读Inspect可能创建该目录操作的空锁文件，不据JSON回执缺席声称所有Root文件都不存在；RootStartedAt永久未写和目标缺席是未开始发布的关键证据。

## 暂不处置、明确ACK与同一目录任务完成

当前管理员在真实异常界面先选择“暂不处置”。ACK为0；此时已有且仅有目录计划、目录登记两次管理POST。父进程必须先完成四次延期并重新核对原Unknown/物理/HDD快照不变，才允许实际点击明确确认；不是先释放保护再补拍阻塞证据。

唯一ACK POST202后，原回收变为Acknowledged、原执行Job Failed、保护释放/Outbox0；原执行/请求/计划绑定、Root Unknown和null源事实、HDD及移位原件保持，唯一不同管理员ACK审计通过。不会重做原回收或恢复原文件。

ACK完成后4.914387秒，同一原目录任务d1a17a4a-6418-508d-7f93-cfda11cfe0c5才写入RootStartedAt并Succeeded。原目录ID/幂等键/请求/计划/登录/Actor/实例/路径全部保持，没有重新登记或发新计划。真实Root Completed回执与publishedVersion、UID/GID1000:1000、0750空目录、inode设备2049/inode170655894及绑定私有目录匹配，签名计划指纹重算通过；Outbox0、接受审计1/终态审计1。

同一管理员上下文回到容器页面，原目录任务显示“目录已创建”，文件列表可见实际目录；原ID GET200/no-store且命令/版本匹配，匿名GET401。回收原Job在实际Jobs界面显示未知→已处置安全摘要，源事实仍未证明。管理写入总计3（目录计划、目录登记、ACK各1），无重复ACK/取消/上传；回收普通8拒绝/匿名4拒绝、两次正常双端退出、临时账号删除确认通过。

## 独立核验与准确清理

清理前独立进程重新读取回收SQL/审计、Root/HDD原始JSON/完整HDD数据与移位原件，并核对四次延期、ACK时间、目录SQL/Root原始回执及实际空目录inode/模式/原哨兵文件。只有独立通过后，精确删除第二只容器的本次空目录与已绑定空私有目录，再由自有fixture核对哨兵原件并清理该容器；第一只容器也只清理已证明移位文件/空私有目录/自有资源。未使用递归删除，Root/HDD及目录回执保留。

清理后再次独立核对永久记录和两个自有源目录/容器缺席。94份旧目录Root文件（含47份JSON与锁）全部保持，仅允许本目录ID的.json/.lock两项新增，最终96份；回收Root26→28/HDD29→31。2346保护文件、11核心healthy、两宿主、指定旧业务及旧失败/partial保持。源/HDD同设备，不签异盘灾备。

## 测试、封存与边界

新增7项跨写入证明测试，关联93项回收工具以ResourceWarning为错误通过，范围8项、Python编译与两个Node脚本语法通过。负测覆盖未归还claim租约与真实执行的区别、重复版本/虚假延期时间、目标出现或Unknown保护丢失、目录原绑定漂移、额外Root项/旧锁变化/核心漂移、同容器伪跨写、重复登记/ACK及未独立核验就清理。测试采用合成SQL快照，实际时钟/数据库/页面证据来自本门禁。

证据deploy/artifacts/container-recycle-cross-directory-8e43c0fdbaa24c6fb1a0a9b143a12685，共155文件、102份冻结验证源码、七张真实页面截图。EVIDENCE_SHA256.json摘要cfc059aaea68b9b95a451336b2049f658736c2fd637a683cc44d65a66f054b7b，逐文件重算通过。成功unit whaledeck-qa-recycle-cross-directory-50697c96d9be、Invocation fdad4be3790047eb8a3fd80c99df8845准确Result=success/ExecMainStatus=0。

原helper PID1490492/Invocation01e520030a674be3af27faee0296b6f8；回收意图4bf8fd9f-b4fe-8589-3c83-f3a2ded7e49c、原执行fd9316b3-a6f2-48ff-e6b2-04f6f6f4029a。主机私有证据不提交凭据、计划Token或原始内部回执到Git。

仅签源路径消失/正常退出形成的回收Unknown对另一容器目录创建的延期和释放，不等于全部writer配对、全部故障组合或完整删除/恢复/长期清理已完成。新候选准确远端CI终态仍待；继续独立恢复业务及其他59项目标。整体25%–35%粗估、整项0/59、完整阶段0/6、AI子计划0%和旧代码量快照保持；17项移出不算完成。
