# 独立恢复：原件归还后的Agent重启

2026-11-06后续：另一个全新来源的写后正常双端退出已实机通过，同执行保留Succeeded/Completed；截图发现的Job页尾提示另有未部署修复候选，见[退出记录](CONTAINER_FILE_RECYCLING_RESTORE_POSTRETURN_LOGOUT.md)。本页保持Agent故障的原始边界。

2026-11-06，工具 `bbf5a2ce289aea321ddf5b0f3c57708bba639029` 在保持3d80d17部署上，使用全新容器、来源和临时真实Authentik管理员，完成写后Agent重启。只有固定Agent重启被本入口允许，没有以强杀Worker、取消、退出或撤权替代故障，没有SQL/时钟改写或旧Apply重放。

unit `whaledeck-qa-restore-restart-0b5a3cf921d7`，Invocation `0f5e1fccd2c54ceca53bc1fc8f999ce7`，exited/0；清理前后独立进程均passed。4项新专项结合回归共128项Linux root通过、零跳过，8范围与Python/Node语法通过。仅签这一个写后Agent组合，其余矩阵及59项交付仍未完成。

## 准确切点和服务变动

Vue回收全新1 MiB+7字节文件、SQL/原Root/原件/HDD证明通过后，以新ID预检和确认恢复。固定restore helper经MainPID/Invocation/cgroup、确切argv及容器root/恢复锁/原来源锁三个FD绑定，暂停于本Root Applying、原来源Restored、原inode已回目标且trash缺席、来源claim匹配处。父进程独立读取当时的Root/claim/来源/HDD实际数据后才允许重启，不能仅从历史标签推断写入。

故障前只允许本意图和执行活跃；其他Job、终端、宿主路径锁及13类文件工作均0，transient仅当前QA单元/准确helper可有进程。原helper PID2761664、Invocation `17723914cba347c29021e12f51aae522` 在故障前存活，原pidfd约0.009966秒观察退出，计时始于服务故障前。

固定Agent PID2627487→2763557，实际apphost/版本仍3d80d17；维护PID2369260及全部容器ID/镜像/StartedAt保持。重启调用和实际新进程身份核对约30.637345秒。Worker保持运行，不把Agent重启写成第二次Worker强杀或自然租约过期验收。

## 维护入口与最终归属

实际维护入口采样152次，覆盖重启前/中/后，均HTTP200/healthy/Ready/no-store；时序与独立验证通过。只签采样点，不证明瞬时零中断，不补签此前失败的连续性批次。

同一意图/执行最终Succeeded、`CONTAINER_RECYCLE_RESTORE_COMPLETED`、真实回执Completed；bindingCurrent/historyVerified/historyAvailable=true，sourceState=Restored、sourceRetained=false/sourceRestored=true/restoredByThisOperation=true。成功来源claim继续保留并匹配本操作，RootStarted/执行/完整来源绑定保持，Outbox0、租约清空、保护释放。

原管理员仍同Subject且有管理资格，取消POST/显式取消审计0、取消时间空；原引用GET200。真实页面“本次文件恢复已完成”和准确take=100的原17字段Job成功摘要均通过，通用重新预检重试按钮缺席。两张真实PNG保存原任务和Job；双端正常退出在终态之后，不替代重启触发。普通用户5 GET/4 POST均403、匿名5 GET均401。

独立进程重读SQL/真实回执/Root、成功claim、来源Root/HDD数据及已归还原inode，关联暂停时Applying原件后，才精确删除本轮已恢复自有叶文件、空私有目录及容器，临时身份按生命周期移除。清理后再次核永久历史、实际HDD与资源缺席；没有再做归还或删除永久历史。

## 保持与剩余

2346保护文件、11核心与全部旧业务完整行SHA保持，只允许上述Agent PID变化；维护和所有容器保持，无产品部署。旧27来源/13恢复保持，新增1来源/1Succeeded恢复、活动0。递归来源Root1964、递归恢复Root257/HDD169、目录Root96保持；回收Root54→56/HDD57→59，恢复Root32→35，只增本执行json/lock/成功来源claim。

源/HDD同device2049，不是异盘灾难恢复。继续写后正常退出/实时撤权、Unknown/来源丢失/实例变化/跨写入及长期清理等剩余组合和59项。17项移出不算完成，25%–35%仍为粗估，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持。

私有封存 `deploy/artifacts/container-recycle-restore-postreturn-agent-fa666ba19a2e4972b29db1a0d0e3caee`，161文件/114冻结源码，含准确切点/当前HDD、服务与原pidfd、152维护样本、原任务/Job、清理前后独立回执。`POSTRETURN_AGENT_SHA256.json` 摘要 `e8e84c6b5c9c0734b6ba29427be996880615f67751abb0d39990a78025a35f0b`。凭据与私有配置不入仓库或图谱。
