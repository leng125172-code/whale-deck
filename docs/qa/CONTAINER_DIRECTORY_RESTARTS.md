# 容器目录：排队任务的重启、取消与登录撤销

## 当前结论

2026-10-28，运行时仍为 `5f3622d07f7e265762785be56712323fde1b5405` / Agent minor44。本轮没有修改产品或重新构建/覆盖服务；验证器 `b7dc949` 对已安装版本执行了四个真实场景，全部通过。

这是**执行前排队阶段**的实际故障门禁，不是 Root Preparing/Prepared/交换期间强杀，也不是目录 Unknown 跨管理员页面验收。它们继续保留在目标，完整59项仍进行中，17项移出不算完成。

## 四个实际场景

验证脚本：`deploy/validation/validate_container_directory_restart.py`，通过 `run_with_ephemeral_admin.py` 运行。每例使用独占、无网络、非root进程、只读根和自有bind的测试容器，不修改生产文件、SQL、缓存、时钟或租约。

| 场景 | 实际动作与结果 |
| --- | --- |
| Worker 重启 | 原Worker停止后正常BFF计划/确认登记，相同HTTP重试返回同任务及原寿命。连续三次确认Pending/version0、无租约/RootStarted/Root记录/目标目录，API仍200。启动同一Worker容器后原任务Succeeded/Root Completed，目录inode/1000:1000/0750与永久计划、回执匹配。 |
| 排队时 Agent 重启 | Worker仍停止，正常登记后实际systemd重启Agent，新PID的真实apphost对应5f3622d；MaintenanceHost PID不变。再启动Worker，原任务成功，原容器实例和计划没有重建。 |
| 排队时取消 | Worker停止时通过原登录明确请求取消同一任务，再启动Worker。原Job/意图Canceled，Root永久Aborted，RootStarted为空、publishedVersion为空，目标不存在。 |
| 排队时正常退出 | Worker停止时登记，随后通过正常WD/Authentik流程退出并确认双端拒绝。启动Worker后原登录复核拒绝执行，原任务Canceled/Root Aborted，未创建目录。 |

实际执行前检查其他活动Job、终端、宿主路径锁、上传与目录发布屏障均为0。每例Worker只操作准确原CID，核对名称、镜像、PID启动变化和健康；finally恢复Worker。Agent只在无其他活动工作时重启。严格fixture核对其余十个核心容器的CID/镜像/状态/StartedAt不变，没有重建基础依赖。

成功目录只在SQL终态、完整Root回执、实际空目录inode/UID/GID/权限与私有目录inode相符时移除；原文件有首次冻结的完整元数据与全内容校验。取消目标保持不存在。永久Job、审计、Outbox处理记录、Root回执不清除。

## 实际结果与独立复核

成功轮次证据：`deploy/artifacts/container-directory-restart-5d1e19a1873d4bac866c58770ace84ac/`，包含四份baseline、四份Root记录归档、results/verified/independent-final。日志 `/tmp/wd-directory-queued-restart-2.log`。

独立新进程读回四任务及对应原Job状态、接受审计各1、Outbox已处理、无租约或发布屏障，四份Root归档与当前永久原件逐字节相同。准确测试CID/源均不存在，临时目录driver unit为0；两类临时身份均0、11核心healthy。全库目录计数10：8成功、2取消，Unknown/活动/租约/屏障均0。总数包括上一轮四项正常网页和本轮初次观察失败前已完成的两个任务，不能把10全部写成这次四项门禁。

既有产品基线为2802 Unit/201 Integration（7项默认原生依赖跳过）、522前端及50联合/目录10项页面通过；这些本轮没有无理由重复执行。新增验证器Python编译/差异检查通过，并接入CI语法门禁。运行时5f3622d的CI40611513488最终五作业（backend、frontend、native-postgres、native-valkey、installer）全部success。新增QA/文档提交的后续CI另行跟踪，不用旧运行的结果宣称新验证源码也已在CI执行。

## 保留的初次观察失败

第一次Worker场景通过；第二次实际Agent已重启，但systemd Type=simple在完成exec之前返回MainPID。观察器直接执行瞬间的`/proc/<pid>/exe --build-info`，命令失败。Agent并没有崩溃，Worker在finally恢复，原任务实际Succeeded。不能把初次整轮写成通过。

修正为：在同一新PID上等待真实`WhaleDeck.Agent` apphost，再读取构建来源；PID再次变化会拒绝，不把观察重试当成重新启动服务。还补了失败收尾：已接受任务先只读等待其终态，只有完整正向证明成功才清理其准确空目录，不与fixture拆除竞争；不重新提交原操作。

初次失败证据 `deploy/artifacts/container-directory-restart-9614ab46d3ac4a72a0deb9b2345175cc/` 和 `/tmp/wd-directory-queued-restart.log` 保留。其第二个任务经独立SQL/Root/目录inode完整核验后，只移除了已证明的空创建目录与私有目录。初版观察器没有冻结原始`original.bin`的inode，因此没有放宽删除门禁：唯一原始测试文件及目录通过同文件系统rename保留在root私有0700证据区，内容和inode保持，已离开应用目录；不是继续运行的应用或容器。准确位置与原因记录在该轮`failed-observer-cleanup.json`，不作为成功轮次已删除的数据冒报。

## 后续

继续目录Root发布准备阶段的实际进程中断/目标变化、进行中取消和撤权，以及原身份消失后当前管理员真实页面Unknown处置与跨应用等待。正常目录页面、排队重启和底层引擎SIGKILL分别保留各自证据范围。再推进容器复制移动/删除/权限、完整隔离、长期留存及其余59项。
