# 独立恢复：Worker强杀、自然租约及Agent重启

2026-11-06，冻结工具 `88b1d2bfad7af7d0e8dee60cb27d3afbbfb7cfa6`，在保持3d80d17的部署上依次使用两个全新自有容器、来源和真实临时Authentik管理员，完成准确Applying写前切点的Worker强杀与Agent重启。两轮均原helper退出、原执行Failed/Aborted、来源仍Trashed；清理前后独立进程passed，unit均exited/0。9项新专项结合回归共105项Linux root通过、零跳过，8范围和语法检查通过。

## 重启前安全门禁

页面回收全新1 MiB+7字节文件并通过SQL/原件/HDD证明，再以新ID生成并确认独立恢复。按MainPID/Invocation/cgroup、确切restore argv及容器root/恢复锁/原来源锁三个FD，暂停在Applying、原件留存/目标缺席/来源claim匹配处；暂停后父进程重开当前HDD数据与原件核验。

故障前确认仅本意图和执行活跃：其他Job、终端、宿主文件锁及13类文件工作均0；transient内仅当前固定QA单元与准确helper允许有进程，历史RemainAfterExit空单元仅作回执。完整原来源绑定、原意图/执行/owner/login/计划、RootStarted等字段逐项保持。只允许固定原Worker容器SIGKILL/恢复或固定Agent服务restart；不重启主机、维护进程、数据库或其他应用，不改SQL/时钟、不补发Apply。

## Worker强杀与自然租约

unit `whaledeck-qa-restore-restart-c7d5a901e4b2`，Invocation `5cf879f225e84738a14d488d1b132154`。SIGKILL前原helper仍存活，原pidfd约0.114427秒观察退出。Worker恢复为同一容器/同一镜像、StartedAt变化；两个宿主PID/版本保持，最终Worker健康。

强杀后同一意图仍Applying、Job Running，原租约token/expiry和写入保护没有清空或重写。原helper退出后的Root仍Applying、来源claim存在；自然观察实际等待84.681108秒，共40份样本。每份到期前样本均保持原token/expiry、Root与claim原始字节SHA、原件命名空间，且重新读取当前HDD数据；墙钟/单调时间差及采样间隔检查通过，clocksOrSqlModified=false。完成时间不早于原租约到期，随后同一执行观察/收敛为Failed，不把固定等待90秒或正在续租的dispatcher当作自然过期证明。

## Agent重启与维护入口

unit `whaledeck-qa-restore-restart-f3b0a462c891`，Invocation `aa3e1a5b318a4c81833bb2a6edd265c1`。仅restart whaledeck-agent.service；Agent PID2369264→2627487，实际apphost及版本仍3d80d17，维护PID2369260保持，全部运行容器保持。重启调用/身份核验区间约30.699649秒，原helper pidfd约0.012956秒观察退出。

维护入口覆盖重启前/中/后153个实际样本，HTTP均200、healthy/Ready/no-store，终态及前后时序检查通过。这里只签这些采样点，不证明瞬时零中断，也不补签此前失败的维护连续性批次。Agent场景中Worker仍运行，未把它写成第二次自然租约过期验收。

## 页面、原件与保持

两轮原管理员在故障后仍同Subject且有管理资格，原引用GET200、页面显示“文件恢复未完成”，任务详情显示原17字段摘要Failed/Aborted与 `CONTAINER_RECYCLE_RESTORE_INTERRUPTED`；通用重新预检重试按钮缺席。取消POST及显式取消审计0、取消时间为空；正常双端退出发生在终态后，不替代故障触发。普通用户每轮5 GET/4 POST均403，匿名5 GET均401。

留存原件的1000:1000/0640/user属性及原字节保持，HDD元数据和数据验证通过，sourceRetained=true/sourceRestored=false/restoredByThisOperation=false；恢复Root claimed/historyVerified为true是历史事实，当前claim已清除，Outbox0、租约清空、保护释放。独立进程重读SQL、Root、HDD实际数据与留存inode后，才精确清理本轮自有trash/空目录/容器及临时身份；清理后再核永久历史和资源缺席。该QA清理不是产品恢复，来源Root/HDD仍保留Trashed历史。

2346保护文件、11核心保持，仅允许已记录的Worker原容器StartedAt或Agent PID变化；维护进程/其他容器未变，无产品部署。旧22来源/8恢复、第二轮前23/9的完整行SHA分别保持，新增2来源/2带执行Failed恢复、活动0。递归来源Root1964、递归恢复Root257/HDD169、目录Root96保持；回收Root44→46→48/HDD47→49→51，独立恢复Root19→21→23，只增两执行json/lock，当前来源claims清除。四张真实PNG记录原任务及Job；源与HDD同device2049，不是异盘灾难恢复。

准备脚本初次按替换前名称查模板锚点发生ValueError，尚未生成新入口或触发服务操作；已修正定位并在实码核对失败文案，后续105项与两轮实机均通过。该准备记录单独保留于 `restore-restart-precheck-f0d65191dd474cdabc65cbb000aea1cc`，不把准备错误写成生产故障或隐藏初稿。

继续写后、Unknown/实例变化/跨写入等剩余组合、长期清理及其余59项；17项移出不算完成。25%–35%仍为整体粗估，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持。

私有封存位于deploy/artifacts：Worker `container-recycle-restore-restart-8b3dc28643a34b928e602aa1fd820374`，154文件/108冻结源码，`RESTART_SHA256.json` 摘要 `4e41347a5713f6e9f08781e46f3f214e13ae84d46089c8f3e2c241afa9216dd4`；Agent `container-recycle-restore-restart-1ad58a8a344c493d8f84e613b7fa3004`，151文件/108源码，摘要 `91de0764c39d1f2a63f0965f43bfaf0c5e855a50c3809cc2267ed1141a1e6de3`。准备修正记录1文件的 `PRECHECK_SHA256.json` 摘要 `24af4f69c4aa93d6e0cf3a5b20b8e76ab5b25bbd618164f82ff91f0f3aafad9c`。凭据与私有配置不入仓库或图谱。
