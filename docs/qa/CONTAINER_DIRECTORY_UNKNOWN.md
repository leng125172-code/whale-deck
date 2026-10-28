# 容器目录：发布准备中断、Unknown 页面处置与进行中撤销

## 当前结论

2026-10-28，已安装产品仍为 `5f3622d07f7e265762785be56712323fde1b5405` / Agent minor44。本轮只增加验证器、受控测试fixture选项和文档；`src`、宿主产品模块、`web/src`与该运行版无差异，没有重复构建或部署。

已通过真实 Preparing/目标消失和 Prepared/非空私有对象两类中断，原身份删除后另一管理员的真实手机暗色页面处置、跨应用发布屏障，以及 Prepared 期间明确取消/正常退出。原子rename交换期间、业务Worker/Agent在进行中强杀的其余组合与完整容器文件能力仍按目标继续，不把单个切点推成全部故障验收。

## 绑定进程的故障工具

`bound_container_directory_cut.py`仅用于root验证，没有加入产品API或增加任意PID入口。它不接受PID、命令或任意宿主路径：

- 只选固定Python transport的Apply进程、准确systemd cgroup；进程必须同时持有独占测试容器root FD和原任务锁文件FD，PID由pidfd绑定。
- 原任务Root记录的operation/owner、完整容器实例/路径/模式和计划指纹必须与已接受SQL意图相符。记录来自真实业务/Agent/helper，没有伪造或改写。
- SIGSTOP后再次读取实际状态、目标不存在与私有对象inode/空目录证明；切点未命中即失败，不冒充发生过中断。只杀该helper，或等待产品自己的取消路径终止它，不杀任意进程。
- fixture默认继续拒绝容器意外消失。显式removable选项只提供无参数的自有删除回调，再次核验准确CID、镜像、标签、用户、网络及唯一bind配置后删除，不接受外部CID。

新增7项安全拒绝和真实临时文件系统检查：错误进程即使持有两个inode也拒绝、非fixture路径在pidfd/信号之前拒绝、终态不能算发布前切点、已有目标/悬空链接拒绝、未登记/替换私有inode和非空对象保留。294项完整容器Linux-root测试通过、零跳过，ResourceWarning提升为错误；Python编译与Node语法检查通过。

## 两类实际Unknown与跨管理员页面

验证器 `validate_container_directory_unknown.py/.mjs`，正常Cookie/CSRF/Origin、真实SQL/Worker/Agent/固定helper，无SQL、缓存、时钟、租约或Root记录注入。

| 实际场景 | 证据 |
| --- | --- |
| Preparing且目标消失 | 真实计划/明确确认后，在Root永久Preparing记录处暂停准确helper，证明SQL Applying/RootStarted/发布屏障已设置，再删除唯一测试CID、pidfd SIGKILL对应helper。Worker自然查询到Unknown，继续保留屏障，没有目标。两个完整执行通过。 |
| Prepared且私有对象变为非空 | 保留原容器，在Root Prepared、准确临时目录inode已登记处暂停，仅在该私有目录写入固定25字节测试数据，再杀对应helper。自然恢复保留非空对象并转Unknown，没有发布，没有删除对象。 |
| 跨应用保护 | 另一测试容器正常登记目录，观察至少两个claim/defer周期：仍Pending、RootStarted为空、目标不存在；原Unknown屏障保持。 |
| 当前管理员处置 | 原账户正常退出并由temporary_user上下文准确删除后，不同管理员正常Auth登录手机暗色文件页。选择原Unknown，暂不处置时确认请求0；再次明确确认恰好1次。旧意图Acknowledged/原Job Failed，保留原计划、原主体、原登录、MAC与Root原件；不表示创建成功。 |
| 解除后另一应用继续 | 只在旧任务确认事务后才取得另一任务RootStarted，实际目录成功，独立inode/1000:1000/0750/版本与SQL、Root一致。WD/Authentik双端正常退出，页面无水平溢出，原/等待测试CID与源严格清理。 |
| 迟到Root Apply | Prepared场景处置后，原容器仍活着；通过已安装固定helper输入原operation/owner/plan，再次Apply仍Unknown，Root原件逐字节不变，私有25字节对象/inode不变，目标不存在。此处是实际固定Root helper，不伪称新增签名Agent RPC权限验收。 |

成功证据：

- Preparing两次：`deploy/artifacts/container-directory-unknown-ab5f7831208447faa88efb39933aac5e/`、`container-directory-unknown-8b6ef780456445f4bcd8caa907f40099/`；日志 `/tmp/wd-directory-unknown.log`、`/tmp/wd-directory-unknown-2.log`。
- Prepared完整成功：`deploy/artifacts/container-directory-unknown-a448a79b32704cbf9607a3ddcd195266/`；日志 `/tmp/wd-directory-prepared-unknown-2.log`。
- 包含baseline、绑定进程和冻结Root记录、物理inode、Unknown原件、等待版本、真实页面截图/结果、处置、永久Root归档、verified及新进程independent-final。主代理已目视复核手机确认与处置截图。

独立新进程检查原/等待Job及单条接受审计、Outbox终态、当前处置主体与已删除原主体不同、旧任务completed时间不晚于等待任务RootStarted、Root归档与永久原件逐字节一致、没有走API失败兜底冒充UI通过，准确源/CID不存在。

失败门禁也不得留下测试Unknown阻塞全局写入：finally仅通过同一真实管理员BFF确认端点核对/封存准确测试任务，记录fallback且整轮仍失败；不直接改SQL/清表/删除Root证据。成功轮次没有fallback记录。清理只处理已冻结完整元数据的测试对象；Prepared额外文件有准确inode/完整SHA证明，永久业务与Root历史不删。

## Prepared期间产品主动取消与登录撤销

`validate_container_directory_inflight.py`两例通过：实际Root Prepared及SQL发布屏障后暂停准确helper，验证器不发SIGKILL，由产品六秒复核循环与固定停止入口结束它。

| 操作 | 实际停止时间 | 真实终态 |
| --- | --- | --- |
| 原登录明确请求取消 | 5.312秒 | Canceled / CONTAINER_DIRECTORY_CANCELED；Root Aborted，目标不存在 |
| 正常WD/Authentik退出 | 5.387秒 | Failed / CONTAINER_DIRECTORY_INTERRUPTED；Root Aborted，目标不存在 |

登录退出没有伪造一条用户明确取消请求，按实际Failed/Interrupted记账；两者均非创建成功。随后自然对账结束原任务，释放屏障、租约与Outbox，只有证明Root Aborted及准确空私有目录inode后才清理。没有DB/时钟改动，正常退出与临时身份清理通过。

证据 `deploy/artifacts/container-directory-inflight-03074a2be7244b8493f78b1c933c5034/` 的两份baseline/Prepared记录/绑定进程/Aborted记录、results/verified/independent-final；日志 `/tmp/wd-directory-inflight.log`。

## 最终状态与失败记录

独立复核最终20目录意图：12成功、3取消、1中断失败、4历史Acknowledged；Unknown/活动/租约/发布屏障均0。这个总数包含此前正常页面、排队测试及验证器失败前已经实际完成的任务，不能把20全记成本轮成功门禁。原永久Unknown Root记录保留为终态栅栏，不因SQL解除保护就抹去。两类临时身份0、临时目录driver unit0，准确测试源/CID清理，11核心healthy。

第一个Prepared整轮在末尾固定helper重放时报文缺少行尾换行，输入校验拒绝；该次已有真实Unknown/跨管理员页面和等待应用成功，但**整轮不记通过**。finally已按准确inode清理自有对象，无遗留屏障或运行容器。修正为完整单行+EOF、保留私有stdout/stderr后，重新完整执行通过。日志 `/tmp/wd-directory-prepared-unknown.log` 与失败证据 `container-directory-unknown-0d0bee791c384b7fb8df5e5815984b0d/` 保留。

首轮工具会等待Worker下一次健康探针，接近短寿命helper超时，不算产品错误。已让观察切点的启动只等待Docker启动返回，立即处理已暂停helper，finally再等Worker健康；第二次Preparing和后续Prepared都按该顺序真实通过。运行版5f3622d的CI40611513488五作业全部success；新增QA提交的CI另行跟踪，不用旧CI宣称新验证器已在云端执行。

继续原子交换切点、进行中Worker/Agent重启等剩余组合；再推进容器复制移动/删除/权限、完整隔离和长期留存，以及其余59项。17项移出不计完成。
