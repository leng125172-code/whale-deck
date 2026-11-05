# 首项恢复完成后的进程边界候选

2026-11-05，承接[Partial原件候选](CONTAINER_RECURSIVE_ACL_RESTORE_PARTIAL_RECORDS.md)。本批新增首项恢复完成后的观察分支，复用首写前已使用的准确进程定位和原件证明；不新增产品写接口或安装钩子，没有启动生产故障、真实页面处置或重放旧任务。

## 实现与严格范围

共享观察器抽出内部实现，原`freeze_before_first_restore`仍使用首写前条件，新增`freeze_after_first_restore`显式入口。两者保持自有容器bind源路径、CID/实例/父inode、已安装restore apply transport、准确操作锁与容器根双FD、pidfd/开始时间、cgroup/MainPID/Invocation绑定；仅能发送SIGSTOP/SIGCONT，不能向任意PID发信号或用SIGKILL制造通过。

新入口只接受26对象access恢复，文件25/目录1/计划修改26；Root Applying且HDD前像已验证，startedThrough=finishedThrough=0。第二项开始、Root已终结、历史缺失或计划范围不同均不签。暂停后重新证明绑定，读取可信来源与实际恢复HDD/首子Root原件，必须与暂停时Root完全相同；完整物理树只有第一项改变，其余25项保持，才交付持有的pidfd。原件归档不得预先存在；错过或证明失败不重试原写入，失败路径恢复进程并关闭pidfd。真实取消、等待产品停止原helper和结算仍须后续父门禁完成。

原件证明拆成“首项完成事实”和“终态Partial”两层。Applying读取不计算或宣称终态实际计数，不把进度Root当Partial回执；原Partial入口仍严格只收Partial，另对完整计数/outcomeDigest/回执检查。两层共用来源/清单/逐项HDD、首项ACL/身份和其余树保持证明，没有放宽旧终态校验。

## 测试及边界

关联112项root工具测试通过、零跳过。新增2个真实临时文件系统测试，在AfterEntry检查点读取实际Applying首项原件，再取得取消后的Partial，证明这两类证据不能互换；既有Partial原件8项、原首写前/业务/任务摘要测试保持。另有4项观察器范围/流程检查，使用显式进程替身，覆盖阶段/类型/26对象范围、拒绝旧归档或非自有路径、成功移交pidfd及物理证明失败后SIGCONT/关闭资源。这4项没有对工作站真实helper发信号，不算安装态边界命中。

4个Python AST、CI YAML及diff检查通过。临时ACL、存储格式原件与容器会话仍为已声明隔离夹具，未取得26对象安装态Partial、真实Authentik/SQL/任务页、不同管理员处置或前后独立进程证据。不能据本候选提高主任务/阶段完成率。下一步在全新资源中接正常取消、Partial Jobs及管理员处置整链，明确处理错过边界、原任务排空和准确清理；第二项已启动形态、Unknown、删除/长期清理、维护连续性及其他59项继续。旧维护失败和源/HDD同device限制保留。
