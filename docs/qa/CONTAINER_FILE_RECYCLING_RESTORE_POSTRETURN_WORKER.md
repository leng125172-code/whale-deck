# 独立恢复：原件归还后Worker强杀与自然租约

2026-11-06后续：另一个全新来源的写后Agent重启已实机验收，Worker/维护进程及所有容器保持，见[Agent记录](CONTAINER_FILE_RECYCLING_RESTORE_POSTRETURN_AGENT.md)。以下保留Worker强杀与自然租约的原始边界，不将两种故障混用。

2026-11-06，工具 `3e9f44e928673c97d99ad91675d18b0fe38f6758` 在保持3d80d17部署上，以全新自有来源和临时真实Authentik管理员，完成写后Worker强杀。原件已经返回时，同一执行在原租约自然到期后保留Succeeded/Completed，不套用写前Failed/Aborted结果。

unit `whaledeck-qa-restore-restart-9df12ab8c703`，Invocation `0195733f2f5d4b539858de29dfcc51f2`，exited/0；清理前/后独立进程均passed。4项新专项结合回归共124项Linux root通过、零跳过，8范围与Python/Node语法通过。仅签此单一写后Worker组合，Agent/退出/撤权写后及Unknown等矩阵仍待。

## 实际故障与租约

页面先回收全新1 MiB+7字节原件并独立核验，再以新ID预检及确认恢复。固定helper经MainPID/Invocation/cgroup/确切argv和容器root/恢复锁/原来源锁三个FD绑定，在本Root Applying、精确claim存在、原来源Restored、原inode已回目标且trash缺席处暂停。父进程重开当时的Applying/claim/来源/HDD实际数据，验证后才允许固定Worker故障。

故障前其他Job/终端/宿主路径锁及13类文件工作均0；transient只允许当前固定QA单元与准确helper有进程。原helper PID2731218、Invocation `f86c16ce33e54f0cbd20f13fe58eb024`，SIGKILL固定原Worker前仍存活；原pidfd约0.111115秒观察到退出，计时始于故障前。Worker恢复为同一容器和镜像，StartedAt变化、最终健康，两宿主身份保持；没有重建容器、重启Agent/维护/数据库或修改时钟/SQL。

强杀后SQL仍Applying、Job Running、保护和原租约token/expiry保持，原件仍处于已归还位置。旧helper退出未必已为本恢复写最终journal；新的Worker必须等原租约到期后观察/收敛，不能指望已退出的原helper继续运行或重做Apply。

实际自然等待87.465084秒、41份样本：到期前原绑定/执行/RootStarted、lease token/expiry、Root与claim原始字节SHA保持；目标原inode不变、trash缺席，当前来源Root/HDD及数据每份重新核对。采样间隔、墙钟/单调时间和最终完成时间不早于原expiry检查通过，clocksOrSqlModified=false。到期不是成功事实本身，最终还必须有与原件及精确claim一致的真实Completed回执。

## 最终结果、页面和清理

最终同一意图与执行Succeeded、`CONTAINER_RECYCLE_RESTORE_COMPLETED`、真实回执Completed，bindingCurrent/historyVerified/historyAvailable=true，sourceState=Restored、sourceRetained=false/sourceRestored=true/restoredByThisOperation=true。原回收业务行仍保留历史Succeeded/Trashed；实际原生来源/HDD Restored，不改写旧回收Job。

原管理员仍同Subject且有管理资格，取消POST/显式取消审计0、取消时间为空，RootStarted与执行身份不变；Outbox0、租约清空、保护释放，成功来源claim保留且匹配本恢复。Vue实际显示“本次文件恢复已完成”，准确take=100原Job详情显示17字段成功归属，无通用重新预检重试按钮。两张真实PNG包含原任务及原Job；正常双端退出发生在终态后，不替代故障触发。

普通用户5 GET/4 POST均403、匿名5 GET均401。独立进程重读SQL、Root、成功claim、来源Root/HDD及当前归还的原inode，关联暂停时的Applying原件后，才精确清理本轮已恢复叶文件、空私有目录、容器和临时身份；清理后再核永久结果/HDD和资源缺席。没有再次rename、删除永久历史或新增恢复操作。

独立回执最初沿用通用字段名restoredBeforeLateCancel；本轮没有取消。后续报告键改为originalReturnedBeforeWorkerCrash，原始回执及冻结源码不改写，验证逻辑逐字保持，第三次只读核对SQL/Root/HDD/租约证据和生产保持通过；没有为改报告键再做一次服务故障。

## 保持与剩余

2346保护文件、11核心及全部旧业务完整行SHA保持，仅允许固定原Worker的StartedAt变动；Agent和维护PID/版本保持3d80d17，无产品部署。旧26来源/12恢复保持，新增1来源/1Succeeded恢复、活动0。递归来源Root1964、递归恢复Root257/HDD169、目录Root96保持；回收Root52→54/HDD55→57，恢复Root29→32，仅本执行json/lock/成功来源claim新增。

源/HDD同device2049，不算异盘灾难恢复；本批没有维护连续性采样。继续其他写后中断、Unknown/来源丢失/实例变化/跨写入及长期清理等剩余组合和59项。17项移出不算完成，25%–35%仍为粗估，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持。

私有封存 `deploy/artifacts/container-recycle-restore-postreturn-worker-4f7a644848594115b71f9545041f5ca3`，165文件/114冻结源码，含准确切点/当前HDD、服务前后/原pidfd、原租约41采样、原Job/页面、清理前后独立与诊断键更名后的只读复核。`POSTRETURN_WORKER_SHA256.json` 摘要 `ca8ee13354423ffeec521c9c13f65617bffb0d3306c1cd96be09e21f8bb654c0`。凭据与私有配置不入仓库或图谱。
