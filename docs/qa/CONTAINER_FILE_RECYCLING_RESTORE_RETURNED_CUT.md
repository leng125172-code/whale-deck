# 独立恢复：原件归还后、最终回执前切点候选

2026-11-06，候选 `f2d556bd3b79c370598b532446a08a71f31574da` 新增QA专用 `bound_container_recycle_restore_returned_cut.py`。本批完成写后切点定义、真实临时原inode归还与局部恢复检查、生产只读保持核对；没有安装、发布、向生产helper发信号、调用新恢复RPC或真实身份/页面写后验收。不能将候选通过写成写后故障矩阵完成。

## 接受的窄切点

本恢复Root仍Applying、claimed/historyVerified=true、sourceOutcome=null，完整计划/owner/派生执行与永久行匹配；原来源Root已持久记录Restored，精确来源claim仍指向本恢复。实际目标必须是原留存inode，设备/inode/模式/UID/GID/链接数/字节数/mtime前8字段保持、ctime不倒退、字节SHA与user属性相等，原私有目录身份保持且trash路径缺席。

该切点晚于原来源自己的Restored记录，早于独立恢复最终journal。Restoring、Trashed、Unknown、缺失目标、残留trash、缺失/外来claim、替代inode、错误版本/owner/digest均不计通过。已Completed/Aborted/Unknown的本恢复不会再次当切点，不重放Apply。

冻结函数复用既有MainPID/Invocation/cgroup、固定restore apply argv、容器root/恢复锁/原来源锁三个FD身份及pidfd；在观察到原来源Restored后才尝试暂停，暂停后再次核对全部事实。未命中必须失败并只恢复本次已停止的原pidfd；没有任意PID/任意路径信号CLI或新增生产暂停钩子。窗口是否能在真实负载中可靠捕获仍待实测。

证明明确 `terminalRestoreJournalSaved=false`、`hddDataReread=false`、`rpcReceiptObserved=false`：不会从当前物理归还直接伪造最终RPC Completed。后续门禁必须另读当前HDD及最终永久结果，且不能将这里的原来源Restored标签当作本恢复最终回执。

## 本轮实际验证

11项新专项结合回归共116项在Linux root通过、零跳过；8项范围和Python语法通过。临时fixture使用真实原生rename归还原inode，在仅测试实例的SourceReturned checkpoint SIGKILL自有子进程，保留下“本恢复Applying/原来源Restored/HDD格式记录Restored/claim存在”。没有手工写一个同内容目标来冒充原inode。

另保存一份独立临时正向原件归档：Applying Root、Restored来源Root、来源claim、Restored HDD格式元数据、物理原件与证明。随后局部Discard收敛为Completed、restoredByThisOperation=true；原inode不变，并以禁止再次exchange的检查证明局部恢复未重做rename。写入已经发生，不能因随后收到取消或丢失响应就捏造Aborted。

这些是临时文件/原生模块I/O及自有测试子进程，容器命名空间、存活和HDD设备证明使用明确替身；不是安装态签名RPC、物理HDD故障或真实Authentik/Vue证据。进程正向绑定沿用既有显式Proc替身检查，未据此声称生产暂停通过。

生产只读复核8条已有执行：2 Completed、6 Aborted，均与永久行/完整计划匹配且拒绝作为新切点；终态判定不读取已清理的物理命名空间。前后全部旧业务完整行SHA、2346保护/11核心及Root/HDD/宿主一致，运行版本仍3d80d17，活动0。生产信号、新RPC、身份变更均0，开发用户NOPASSWD:ALL保持。

## 下一步与范围

私有证据 `deploy/artifacts/container-recycle-restore-returned-cut-8a67405ad8c54a8fb1a1975841bd2564`，118文件/105冻结源码，含真实临时Applying/Restored/claim/HDD格式原件、物理归还及局部Completed证明、8条真实终态只读拒绝和生产前后保持。`RETURNED_CUT_SHA256.json` 摘要 `5e54b3207a8c006226d80933ab6fd4df5f01ee54d81d07192b7dac6a26d0333f`。私有配置与凭据不入仓库或图谱。

需以全新自有来源接真实页面与安装态helper，准确捕获该写后切点，再验证取消、退出、Worker/Agent中断或撤权后的同一执行观察/归属及清理；已发生归还时不能按写前案例预设Canceled/Aborted。也未覆盖rename内部/仅Restoring、Source丢失/实例变化Unknown、跨写入或长期清理。

59项及17项移出边界不变，25%–35%仍是整体粗估，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持。前序真实常规/写前取消、退出、撤权、重启验收继续保留，不重做或改写其回执。
