# 容器回收执行中Worker强杀与Agent重启

2026-11-05，产品保持58b8174、冻结门禁62e19fb5。承接[实时撤权](CONTAINER_FILE_RECYCLING_REVOCATION.md)，分别以全新自有容器和1,048,583字节文件完成两个安装态故障/真实页面整链。本批仅重启指定现有Worker或固定Agent服务，没有覆盖发布、重建核心容器、修改SQL/时钟、重启主机或自动回滚。

## 切点及故障范围

原管理员通过真实Authentik和Vue产生Ready并唯一确认。观察器复核原SQL执行ID/完整计划、源inode/字节/属性、当前systemd MainPID/InvocationID/cgroup、容器根和原操作锁两个内核FD，才使用原pidfd暂停准确helper；Root均为Preparing、HDD已验证、源未移除，私有空目录已绑定。遗漏切点即失败，不允许重做Apply。

故障前检查除本意图/执行外的未完成Job、终端、宿主锁、12类容器文件业务的活动/保护/租约均为空，并限定实际运行的相关transient进程仅本门禁和准确helper。已退出且无进程的RemainAfterExit单元保留为回执，不能仅因其ActiveState=active而当作活动工作。再次确认原pidfd仍活后才注入故障，退出等待从故障命令前开始并发计时。只有Worker强杀或固定whaledeck-agent.service重启两种动作，没有任意服务/PID输入。

## 两项已验行为

| 场景 | 原helper退出 | 原任务结果 | 恢复证据 |
| --- | ---: | --- | --- |
| Worker SIGKILL后恢复原容器 | 0.097884秒 | Failed / CONTAINER_RECYCLE_INTERRUPTED，Root Aborted | 原租约自然等待83.592秒、40次采样；期限后2.896秒保存完成 |
| Agent服务重启 | 0.011014秒 | Failed / CONTAINER_RECYCLE_INTERRUPTED，Root Aborted | 原权限、登录、执行ID保持；实际Agent重启约30.723秒 |

退出时间测量对象是原helper，不能把0.011秒称为整个Agent重启时间。QA强杀的是固定Worker容器，未直接杀Root helper；产品因原RPC断开/服务停止而结束准确原helper。

Worker保持原容器ID和镜像，只允许StartedAt改变，并恢复healthy。强杀后原SQL仍Applying/Running、原租约和写入保护保持；原helper退出后的Root仍Preparing。随后真实等待原期限，逐次核对原登录/请求/计划/执行绑定、同一租约/保护、Root原始字节和完整源原件，不修改数据库或注入时钟。原期限到达后，Worker对原执行观察/中止收敛，保存Failed/Aborted；原执行ID、RootStartedAt和绑定不变，源未重新移除，不能把Started后恢复当作重新Apply。

Agent PID1005597→1286507、版本仍58b8174；MaintenanceHost PID1005591保持，全部核心容器保持。维护入口153次有界采样均200/healthy/Ready/no-store，含重启前、中、后；这仅是本次采样证据，不代表瞬时零中断、其他发布连续性通过或P0-05完整验收，旧连续性失败保留。

两个原管理员会话均继续有效、同Subject且仍是管理员，取消POST0、SQL取消时间为空；没有通过退出/删除身份把中断伪装为取消。原页面显示“文件回收未完成”，实际Jobs原执行ID显示“任务未完成”和CONTAINER_RECYCLE_INTERRUPTED，不提供通用重新预检重试动作；各正常BFF/IdP双端退出一次。规划Succeeded、唯一登记/确认审计、执行Job Failed和14字段安全摘要、Outbox清零及保护/租约释放核对通过。

Root的Aborted、业务/Job的Failed和HDD captureState=Canceled是不同事实，不能混为用户取消。HDD完整原件/摘要仍已验证；sourceRetained/sourceRestored均false，完整源inode/字节/属性保持。清理前独立进程重新读取SQL、Root/HDD JSON、HDD完整数据和实际源文件；独立通过后只清理本次已证明原件、已绑定空私有目录及自有容器，再独立核对资源缺席/永久历史保持。普通8次、匿名4次拒绝和临时身份删除确认各通过。

Worker组Root16→18/HDD19→21，Agent组Root18→20/HDD21→23。2346保护文件、指定旧业务/全部历史保持；最终11核心healthy。Worker组仅准原Worker启动时间改变，Agent组仅准固定Agent PID改变，其他核心/宿主严格相等，不用宽泛基线替换掩盖变化。前批未验证Preparing metadata/partial继续保留，源/HDD同设备不算异盘灾备。

## 工具及封存证据

新增9项证明/边界测试，68项回收工具以ResourceWarning为错误通过，范围8项、Python编译和Node语法通过。负测覆盖其他活动工作/非空transient进程、错误服务、原操作/租约/Job漂移、缩短时钟观察、稀疏采样、提前完成、重建Worker、其他核心或MaintenanceHost变化、原权限丢失/取消结果及未清IdP会话；没有在单元测试中执行实际服务故障。

两组均准确Result=success/ExecMainStatus=0，各98份冻结验证源码、两张实际320×600暗色页面截图及清理前后独立证明。证据为deploy/artifacts下主机私有权限，逐文件SHA重算通过；不提交凭据或内部原始回执。

| 场景 | container-recycle-restart-目录后缀 | 文件数 | EVIDENCE_SHA256.json摘要 |
| --- | --- | ---: | --- |
| Worker | 40498b3790bb4b08961e32624e110198 | 130 | cdc18f21fea5e20189cf41ccc5184acdc0d71194d31a29b0a01e56d83f813783 |
| Agent | 62b29e5054b94357813f1c9f5e028f81 | 128 | 8a97e616b0371961443bb83809522d2b83dfe5a941b67776802455c0ae37ed79 |

Worker unit `whaledeck-qa-recycle-restart-b1994e397841`、Invocation `5166f6c0a5db4cecb63f67ab0219089f`；原helper PID1272510/Invocation74e733dc5a1048cca510cebf8b12783b，意图1406322b-c533-12da-82e6-325e0c91a230、原执行b7162b1a-d28b-d44e-c0d4-5db5c3bf0785。

Agent unit `whaledeck-qa-recycle-restart-7bba7dd411da`、Invocation `af7d3ddfca09476282f2efaada91c389`；原helper PID1284708/Invocationb3e5e21a28684dd79054e59cdba3d66d，意图1d0b3223-ae85-dc92-97b7-a5e6e06bc441、原执行73067866-9c33-a8c0-3c01-3a49c3245703。

## 未验边界与下一步

仅签HDD已验证、源未移除时的上述两个重启组合，不签移除后重启、目标消失/实例变化Unknown、跨写入、独立恢复业务、长期保留/彻底清理、目录/完整元数据删除或59项完整交付。新候选准确远端CI终态本批尚未核验；继续Unknown/跨写入及独立恢复业务、长期清理和其余目标。

整体25%–35%仍为低置信度工作量粗估，整项0/59、六阶段完整签收0/6和AI子计划0%保持；17项移出不算完成，新增验证代码不自动更新旧产品源码规模快照。
