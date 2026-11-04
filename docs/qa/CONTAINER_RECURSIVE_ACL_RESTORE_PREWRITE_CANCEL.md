# 递归ACL恢复执行中首项写前取消

2026-11-05，dev；承接[Ready退出与自然到期](CONTAINER_RECURSIVE_ACL_RESTORE_READY_LOGOUT.md)。本批目标是新26对象来源真实确认后，验证当前ACL前像已留存但首项未改写时的页面取消；不把Ready取消或正常递归ACL旧矩阵替代为RESTORE执行中证据。产品仍eeb24dd，不扩大Agent授权、不部署或回滚产品。

## 候选与安全边界（执行前）

新增RESTORE专属观察器：只接受自有wdv-container-publish目录、当前容器实例、26项access恢复（25文件/1目录/26计划修改）。根据实际apply transport argv、Root UID、容器根FD与原执行操作锁FD选择候选，pidfd必须仍指向原PID与/proc启动时间，前后核对，再核验精确system.slice cgroup、MainPID、InvocationID与进程单一INVOCATION_ID。unit后缀是随机driver ID，通过内核绑定识别，不按执行ID猜测单元名。只发送SIGSTOP与必要SIGCONT，不提供任意PID/路径入口；只有本观察器暂停过的进程才会恢复，不由QA发送SIGKILL。

暂停后必须再次确认Root Applying/historyVerified、完整当前树仍原样、首项未完成，以及首子项（若已建立）为Preparing/Prepared/尚未写入的Applying且actualIdentity/aclConfirmed空、version空。原HDD聚合与当前26项前像、选定来源/frame、完整manifest/逐项目标摘要验证后，才允许真实页面取消。若错过边界即失败，不自动重新Apply、不回放旧请求。

页面路径：正常Authentik登录→新来源历史选择→恢复Ready/原引用重载→当前Worker准确停止→真实确认得到Pending→同Worker启动与专属观察器绑定→真实页面Applying→撤销取消框保持POST0→明确取消1次。产品须终止原pidfd对应helper，原执行Inspect/recover后Canceled/Aborted、完成/变更/Unknown均0、0或1子项Aborted，其余NotStarted；聚合当前ACL前像HDD保持，实际完整文件/九字段/ACL/xattr不变。Worker无论失败或成功都恢复同ID/镜像并等healthy，回执只白名单身份/启动/健康，不保存Config.Env或Health.Log。

早期中止证明独立支持：聚合前像已留存而未开始子项、首项已建立但无子历史、首项有已验证历史但尚未修改；这些状态不能套用旧的无历史Aborted证明。每项可选子记录、HDD类型/独立ID域/owner/父manifest、原前像、五计数和outcome摘要严格核对；Partial/Unknown、目标变化或额外子JSON不能被签为零写入。SQL/双Job/5条精确审计、当前API终态/权限/正常双端退出、清理前独立实读与清理后原件复核均接入。

16项新专项及关联11Ready/10分页/10业务/10历史，共57项Root工具与8范围通过，零跳过，Python/Node/YAML及diff检查通过。临时真实Linux ACL/HDD测试覆盖4对象的BeforeEntry/Prepared/Applying早期异常恢复；真实Linux pidfd验证存活self与自有短子进程退出；其余SQL/进程标识负例为显式夹具，不是安装态RPC/真实页面签收。新26对象安装态门禁尚未运行。

复核期间补上pidfd fdinfo-Pid/启动时间/存活与信号前后绑定、仅已暂停进程恢复，覆盖PID退出/替换反例；增加Applying但全树未变的合法子项边界；Worker回执排除运行凭据。均在冻结前修订，未执行实机SIGSTOP/Worker操作，也没有通过放宽物理未变或HDD要求取得通过。

后续保留完整真实运行回执、失败及修复记录；本批只验执行中写前取消，不签执行中正常退出、写后/撤权/重启/Partial/Unknown/来源异常、删除/长期清理、维护连续性与其余59项。17项移出不算完成。


## 首批实机失败：不签写前取消

冻结 `ca251a88fffbc9a9b4ae6eb9327fa37c90f34aa2`、98源码，唯一unit `whaledeck-qa-restore-prewrite-ca251a8`，Invocation `f86a59e066bf4bf193916bcd9024fee9`。新26对象来源及真实Vue Ready/确认Pending已完成，但观察器返回None，REAL_RESTORE_PREWRITE_CUT_MISSED_NOT_COUNTED，原总门禁ExecMainStatus=1/Result=exit-code。没有取得bound-helper证明，没有发送页面取消或处置；也没有保存足以判断是否短暂停顿过的观察事件，不能猜测精确错失原因。

更严重的是失败清理顺序错误：原Apply尚未结算时，测试浏览器被关闭、临时身份清理、原容器被夹具移除。源文件目录按未验证规则保留；原任务随后SQL/Root均Unknown、发布保护true，聚合计数0/0/0/0/26，startedThrough22、finishedThrough21。不能把这算成零写入或取消，也不把工具清理导致的情况称为产品正常写前故障。

只读逐项核对确认：item-00.bin至item-21.bin共22份子Root为Completed/aclConfirmed=true，实际inode/owner/bytes/非目标ACL及目标access匹配；item-22.bin子Root为Unknown/ACL未证实，当前文件保持原样；其余3对象也保持来源捕获后状态。聚合仍保留Unknown26，不改写SQL/Root计数为22。恢复HDD完整聚合加23子项、来源聚合加26子项原件均归档；新原件不是模拟数据。

## 原任务保存、真实管理员处置及清理

专属恢复工具 `92b866f65da1ba457fb71ba9eebb78170bd7c97c` 只允许上述固定失败目录，不提供任意任务或Apply入口。唯一恢复unit `whaledeck-qa-restore-recovery-92b866f`，Invocation `2a924a5a16544d66ac8020826e57ca1b`，ExecMainStatus=0/Result=success。

所有可用原件及26对象完整字节/九字段/access/default/xattr先核对后，另一名全新真实管理员正常Authentik登录/文件页，原owner入口404，异常页保留Unknown26。先点“暂不处置”并验证ACK0、完整SQL与物理/Root/HDD保持，再一次明确“确认处置并释放保护”202→Acknowledged。Root永久Unknown事实和原五计数/历史保持，原执行Job以Failed结束、发布保护释放；不续做、不回滚22个已变更对象、不删除HDD。新管理员原上下文正常BFF/Authentik退出与匿名401通过；两个真实320暗色PNG保存并人工复核。原失败上下文没有正常退出，不补签。

恢复工具三轮读取当前实体及全部原件，确认后才精确删除25个自有文件和空根，original.bin按确定性全字节/uid/gid/单链接核对后删除，源目录和原容器均缺席。清理后另进程重开54份来源及48份恢复Root/HDD，与处置前归档逐字节相同，不宣称再次读已清理源字节。该只读脚本首次相对路径被trusted拒绝，改绝对路径后通过；没有重发ACK或恢复请求。

普通业务仍54意图/49执行/103Job；恢复9意图/6执行/活动0，新增原Unknown→Acknowledged，不多创建任务。旧1682保护文件字节九字段保持、普通保护总1736；来源Root1354，恢复Root130/HDD101（含本批聚合及23子项）、目录94。11核心ID/镜像/状态保持，只有同ID同镜像Worker按已记录流程重启；Agent1379812/维护1379807与eeb24dd保持。源/HDD同device2049与旧维护连续性限制保留。

私有原失败与恢复证据 `deploy/artifacts/recursive-acl-restore-prewrite-cancel-bd15b60dd0914c08a8728c1758cf9259/` 保存原两个unit准确回执/日志、原SQL/物理现场、逐项Root/HDD、真实管理员暂不处置/ACK/退出、清理与清理后原件证明。`recovered-original.json` 明确originalGatePassed=false、originalRestorationNotReplayed=true、normalOriginalLogoutNotAccepted=true。不生成原verified.json、不改写失败终态。

## 失败清理修复候选与未验边界

修复候选：失败后先终止测试浏览器，停止后续页面动作；保留测试身份和容器等待原执行结算。只有原SQL已终态/无租约、原Root终态且操作锁空闲，才允许移除容器；若有界350秒/800观察仍无法核实，保留自有容器/目录并明确记录，不能以清理强制销毁运行目标。身份仅在这段排空等待期间保留；超时后外层临时身份上下文仍会撤销测试账号，不能宣称无限期保留身份。原执行不会重做。失败排空回执与边界选择/暂停/错失事件有界保存；Worker finally仍恢复健康。保留开关只对工厂创建的精确自有ID/目录生效，默认调用不改变。

修复后20项写前专项（新增真实临时上下文异常保留且不吞异常、排空/保留范围反例）及2项固定恢复范围、41关联工具，共63项通过，另8范围通过，零跳过。Docker保留/进程排空条件负例是夹具，临时ACL/HDD与pidfd是本机实际测试，不冒称修复后新实机故障整链通过。本轮不再新建另一笔恢复试错，下一批必须全新资源/ID并保留观察事件与正确失败清理。

写前取消、执行中退出/写后/撤权/重启/Partial/Unknown等完整矩阵、删除/长期清理、维护连续性及其余59项仍待；当前只新增失败保存与原任务显式处置证据，不提升完整完成率。


收尾：原失败/恢复248文件已生成并逐项验证SHA256清单，清单SHA256 `9ec99587a7cdbb64ff93819cf9959152bcf54c1fc289e92190f850645b96c9ac`。原失败unit退出1、恢复unit退出0准确回执均归档后才停止QA unit；原工作站没有恢复重放或回滚。另只读核对11核心healthy、原ID/镜像保持，只有已授权同Worker StartedAt变化。

最终20写前专项+2恢复范围+41关联工具共63项、8范围通过，Python/Node/YAML及diff检查通过；本轮修改MD的733个本地链接存在/缺失0，原Ready QA正文前缀保留，Git索引1000:1000/664。新清理/诊断修复仅候选，未运行另一批真实写前取消；新候选CI终态尚未核实，不称全绿。

项目图谱事件 `whale-deck.container-acl.ca251a8.prewrite-failure-recovery` 已取得project/key/event匹配且duplicate=false成功回执，pending移入synced，阶段保持active。


## 新资源复验准备

2026-11-05继续复核：修正失败处理引用未导入subprocess的问题，单独验证测试浏览器terminate超时后仅回收QA自有Node子进程，避免NameError遮盖原失败；不向Root helper发送SIGKILL。21写前专项、2恢复范围、41关联工具及8范围共72测试通过，零跳过。首次命令误写Ready测试模块名导致ImportError，该次失败保留，改为现存test_recursive_acl_restore_ready_logout后整组通过。

实机前只读检查：恢复9意图/6执行/活动0，来源54意图/49执行/103Job；其他活动Job、终端、宿主锁及上传/目录保护均0。运行eeb24dd和两宿主保持。下一次运行必须冻结新修订，使用全新26对象来源、临时身份和操作ID；结果依实际观察事件及完整物理/原件核验记录，未运行前不签通过。


## 新资源73fa7bc复验：仍不签写前取消

冻结73fa7bc，唯一unit whaledeck-qa-restore-prewrite-73fa7bc，Invocation 2bac098288524ca393074ba6d97995e0；全新26对象、来源与身份正常进入Vue Ready并确认Pending。准确helper已pidfd-bound，但25秒总观察结束时尚未取得Applying写前边界，原门禁Result=exit-code/ExecMainStatus=1。第一次排空观察在返回后117ms仍为Preparing，后续进入Applying并完整Completed。只能证明本批观察时限不足；不倒推首批ca251a8的精确错失原因。没有bound-helper、页面取消、原上下文正常退出或完整权限页面签收。

修复后失败清理本次实际等待32个样本，19.225秒才取得SQL Succeeded、Root Completed和原操作锁空闲；26完成/26变更/0Unknown，无租约/发布保护，随后才清理临时身份与容器。原文件目录仍按未核验规则保留，未重发Apply或ACK。恢复累计10意图/7执行/活动0。失败证据位于私有deploy/artifacts/recursive-acl-restore-prewrite-cancel-2c40b80c1c444bb9973e223664f7e7d6。

新候选将有界观察等待改120秒，浏览器握手180秒；Worker启动前等待观察线程完成范围检查并armed，日志新增阶段/到期时间。armed只证明线程就绪，不是helper或写入同步栅栏，不能保证永不漏窗；暂停后完整物理未变、精确pidfd/FD/Invocation和HDD要求全部保留。显式测试45秒Preparing后才终态仍可观察且不发信号，未通过放宽首写前条件取得通过。

新增固定本次失败目录的只读核对与准确清理工具，不接受任意任务，不发Apply/ACK/SQL更新/进程信号。需证明原SQL与Job成功、26项Root/HDD/完整实体和旧保护保持后才清理；清理后重开108份来源与恢复原件，不宣称重读已删除源字节。22写前专项、2原恢复范围、2本次范围、41关联与8主范围共75测试通过，零跳过；仍须实际运行核对工具并保存结果。
