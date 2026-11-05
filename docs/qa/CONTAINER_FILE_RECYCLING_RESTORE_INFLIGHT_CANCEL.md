# 独立恢复：准确写入前切点的真实页面取消

2026-11-06，冻结工具 `91b5b16df692f7857b32067042fe134f6019986f`，对保持3d80d17的实际部署执行一轮全新自有资源验收。正常可见Authentik登录、Vue创建回收来源并通过SQL/Root/HDD证明后，另建独立恢复、在准确Applying切点暂停该helper，再由页面明确取消。没有重放旧任务、模拟响应、注入会话、数据库编辑或产品测试钩子。

门禁 `whaledeck-qa-restore-inflight-cancel-first`，Invocation `a7b358797b5345b29d2f983a243fabb5`，exited/0；清理前、清理后的两个独立进程均passed。源码新增4项结合原关联检查共55项通过，Linux root零跳过，8范围及Python/Node语法通过。本批只签以下单一故障切点，完整权限/恢复故障矩阵仍未完成。

## 实际切点与取消

- 全新1 MiB+7字节文件、1000:1000/0640/user属性，来源由页面回收。原来源意图Succeeded、原执行Trashed、源路径缺席、留存原inode及HDD已验证后，才放行新恢复预检。
- 页面Ready后先启动观察线程，再确认一次恢复。暂停严格绑定独立恢复ID、原来源/完整计划、systemd MainPID/Invocation/cgroup、固定restore apply argv、容器root FD/新恢复锁FD/原来源锁FD三个不同内核身份。原helper PID2496952、Invocation `dff190c3fe004cd8a77c075192b1e7e8`；暂停后复查Root Applying、SQL Applying/RootStarted/写入保护、正确来源claim、原来源Trashed、留存原inode未变且目标缺席。
- 候选切点证明自身仍标注hddDataReread=false；本门禁在helper暂停后，另由父进程重开实际来源Root、HDD元数据及数据，逐字节摘要/属性/物理命名空间与此前原件保持，才给页面放行取消信号。没有从历史historyVerified标签替代当前读取。
- 320×600明主题真实页面取消按钮完整可见、无横向溢出。明确取消POST202恰一次；原pidfd测得helper退出约3.271652秒，计时在HDD复核及放行页面操作前开始，含页面交互等待，不是只测HTTP取消响应耗时。不以晚到的PID查询补签退出。
- 最终意图及执行Job为Canceled、恢复回执Aborted、原来源仍Trashed/sourceRetained=true/sourceRestored=false/restoredByThisOperation=false。Root记录claimed=true/historyVerified=true表示曾建立并核验claim，当前claim文件实际缺席并归档为null；不将两者混为一谈。RootStarted/完成/取消时间存在，租约清空、写入保护释放、Outbox0，计划/确认/执行取消审计各恰1。

## 页面、身份与独立清理

实际Jobs页使用准确take=100列表的原执行行，详情显示17字段独立恢复安全摘要：Canceled与Aborted并列、原来源引用和本操作未恢复事实正确。三张真实PNG包含暂停后等待恢复页面、取消结果和原Job详情；截图中“等待原inode恢复”是页面当时的轮询状态，真实暂停阶段以独立SQL/Root证明为准。

普通用户5 GET/4 POST均403，匿名5 GET均401；同管理员上下文正常双端退出、可见重登后三个原意图/原键/原来源引用404，再正常双端退出。两次退出发生在取消完成后，不签收执行中正常退出场景。临时管理员与普通身份均精确移除。

独立进程重读完整SQL/审计、原Root/当前claim缺席、来源Root/HDD及实际留存inode，确认源路径仍缺席，才删除本轮精确自有trash、空私有目录及容器。此为QA夹具清理，没有发产品恢复或把取消写成恢复成功。清理后再次独立核对永久记录/HDD和资源缺席；Root Aborted、原来源Root/HDD Trashed与全部历史保留。

## 保持与剩余

私有证据 `deploy/artifacts/container-recycle-restore-inflight-cancel-3163a432fcac4726be4c88ad1be4af69`，157文件/114冻结源码，含原进程/三FD切点、当前HDD复核、永久SQL/Root/claim、真实页面、退出观察、unit及两个独立回执。`INFLIGHT_CANCEL_SHA256.json` 摘要 `9dab1b6abf561273f598c3a43a24bdd17ecb141cb052a533041a53bca0397650`；私有配置及凭据不入仓库或图谱。

2346保护文件、11核心服务、两宿主与应用3d80d17保持，无部署或服务重启。旧18回收/4恢复意图完整行SHA保持，新增1回收与1带执行的Canceled恢复、活动0。递归来源Root1964、递归恢复Root257/HDD169、目录Root96保持；回收Root36→38/HDD39→41，独立恢复Root11→13，仅本恢复json/lock新增，当前来源claim已清除。

源与HDD数据同device2049，不是异盘灾难恢复证明；本批没有维护连续性采样。此前失败与候选记录保持，不以本场景补签其他切点。继续执行中退出/撤权、Worker/Agent重启与自然租约、写后/Unknown/实例变化/跨写入、长期清理及其余59项。17项移出不算完成，25%–35%粗估、整项0/59、阶段0/6、酒馆/供应商及Live2D功能0%保持。
