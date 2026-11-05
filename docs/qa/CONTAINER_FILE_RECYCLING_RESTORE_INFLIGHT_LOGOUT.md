# 独立恢复：执行中正常双端退出

2026-11-06，冻结工具 `5ac8641c65a5a24d64b4ba028c5f8c5651dcd79b`，在保持3d80d17的实际部署上，以全新自有容器、来源及临时真实Authentik管理员，完成Applying写入前切点的正常退出验收。没有点击或调用任务取消、模拟响应、注入会话、修改业务数据库或重放旧Apply。

门禁 `whaledeck-qa-restore-inflight-logout-first`，Invocation `a3825b1e48cd4aa99e1c8920f6f203e7`，exited/0；清理前后两个独立进程均passed。4项新专项结合既有退出/切点/协议/原件检查共65项通过，Linux root零跳过；8范围及Python/Node语法通过。本批只签这个切点与正常退出组合，不签完整故障矩阵。

## 准确切点与真实退出

页面先回收全新1 MiB+7字节文件，独立确认原inode、1000:1000/0640/user属性、来源Root与实际HDD后，再以新ID预检及确认恢复。观察线程在确认前启动，按固定restore apply argv、systemd MainPID/Invocation/cgroup、容器root FD/恢复锁FD/原来源锁FD绑定helper；暂停后重验Applying/RootStarted/写入保护、正确来源claim、目标缺席与原件仍留存，并重新读取当前HDD数据。错过切点不会重发Apply。

原helper PID2530457、Invocation `95c11983785d4bc988da8b487cfec1de`。320×600明主题页面关闭原对话框/抽屉后，经原用户菜单执行退出；浏览器仅在真实BFF导航POST发出时写标记。原pidfd观察线程证明该POST被观察时helper仍存活，约3.169763秒后观察到退出。计时在放行页面动作前开始，包含关闭界面/菜单交互，不是只测HTTP退出响应。

两次正常退出均实际观察到BFF POST302、OIDC end-session导航、BFF当前会话401、Authentik当前身份403及最终登录页。第一次发生在恢复执行中；同账号可见重登后，旧意图、旧键及原回收意图三个Owned引用均404，当前管理员通过RecoveryGet200读取原历史结果，再正常双端退出一次。

授权链是恢复工作流→ContainerFileUploadService.AuthorizeAsync→ILoginSessionRegistry.IsActiveAsync，检查持久化的旧login binding；Cookie票据退出走ServerSideTicketStore.RemoveAsync撤销共享会话绑定。不是仅凭浏览器回到登录页推断任务已经停止，也没有用移组、停用账号或显式取消替代正常退出。

## 永久结果与独立证明

- 意图及原执行Job为Canceled，恢复回执Aborted；cancel_requested_at_utc为空、取消POST0、取消审计0。RootStarted/完成时间存在，租约清空、写入保护释放、Outbox0；计划/确认审计各1。
- 恢复Root claimed=true/historyVerified=true表示曾建立并验证claim；当前来源claim实际已清除并归档null。原来源仍Trashed、sourceRetained=true/sourceRestored=false/restoredByThisOperation=false；原来源Root/HDD/数据与回收后快照相同。
- 新登录的实际Jobs页取准确take=100列表中原执行，显示17字段安全摘要、Canceled/Aborted、正确原来源引用及本操作未恢复。两张真实PNG保存暂停后等待恢复界面及原Job详情；界面轮询的等待状态不替代独立SQL/Root Applying证明。
- 普通用户5 GET/4 POST均403，匿名5 GET均401。临时管理员与普通账号按自身生命周期精确移除；没有改动其他真实用户身份。

独立进程重开永久SQL、原Root、当前claim缺席、来源HDD元数据/实际数据及留存inode，证明源路径缺席后，才精确删除本轮自有trash、空私有目录和容器。该步骤是QA夹具清理，没有产品恢复或来源Restored宣称。清理后再次独立重读永久记录/HDD、验证资源缺席；原来源和恢复历史全部保留。

## 保持与剩余

私有证据 `deploy/artifacts/container-recycle-restore-inflight-logout-55189c53cbd64841a586f327fdadd47c`，148文件/106冻结源码，含原进程切点与退出时序、双端退出回执、当前原件/HDD、永久SQL/Root/claim、实际页面、unit及两个独立回执。`INFLIGHT_LOGOUT_SHA256.json` 摘要 `9a8151f16b90b13413ec1deb251285f8490b455a7df82990490300ab512f2062`；凭据和私有配置不入仓库或图谱。

2346保护文件、11核心、宿主及应用3d80d17保持，无发布/服务重启。旧19回收/5恢复意图完整行SHA保持，新增1来源与1执行Canceled恢复、活动0。递归来源Root1964、递归恢复Root257/HDD169、目录Root96保持；回收Root38→40/HDD41→43，独立恢复Root13→15，仅本ID的json/lock新增，当前来源claim已清除。

源/HDD同device2049，不算异盘灾难恢复；没有维护连续性采样。本批不补签其他切点、实时移组/停用、Worker/Agent重启及自然租约、写后/Unknown/实例变化/跨写入或长期清理。59项与17项移出边界不变，25%–35%仍为粗估，整项0/59、阶段0/6、酒馆/供应商及Live2D功能0%保持。
