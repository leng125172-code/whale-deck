# 归档Reserved：Worker强杀自然租约与Agent重启

2026-11-07，在产品 `f318d9ab130ecab94668acf9754a6a6e448de92c` 上，修正QA `b997ba6ee32bfc1c3613a5b2a53f5cd532f3e734` 用两套全新资源分别通过Worker强杀/自然租约与Agent重启。承接[实时撤权](CONTAINER_FILE_RECYCLING_ARCHIVE_REVOCATION.md)，仅签Root Reserved复制前组合，不代表写后、Unknown或全部故障矩阵完成。未部署、重启主机、修改SQL/时钟或重放旧Apply。

## 固定对象和服务故障门禁

实际信号/重启前，规划和执行Job只豁免本轮两个ID；终端、主机文件锁、其他Job及15类文件业务表的未完成、barrier、lease token/expiry必须为空。归档表只排除本轮目标意图，不能将另一条归档任务或残留expiry漏过。QA主单元MainPID/InvocationID/cgroup须对应当前父进程；当前helper之外的其他WhaleDeck临时单元必须无进程，已退出回执不当作运行任务。

沿用归档五FD与原pidfd、真实SQL预留回执/完整计划/owner/双来源/当前HDD及Root Reserved证明。故障动作前再次核原Root与冻结记录逐项相等、原MainPID/Invocation、五FD和实际T/t停止状态，确认原helper仍活。预留及Apply入口已持久化，不等于复制事实。

- Worker仅对原`whaledeck-worker`容器ID发送SIGKILL，再启动同ID；镜像和名称必须不变，StartedAt确实改变，正常收尾等healthy。不重建容器或改策略。
- Agent只重启固定`whaledeck-agent.service`，确认新PID实际执行已安装apphost、sourceRevision仍f318d9ab；维护进程不得改变，并采样重启前、期间、之后的维护入口。

失败时仅终止已精确绑定的暂停helper，不SIGCONT恢复复制；恢复本轮实际尝试的固定服务，并保留未决夹具。Worker fixture只在该场景豁免Worker StartedAt，Agent fixture不豁免其他核心容器变化。baseline仍严格拒绝其他容器、维护进程、旧业务/Root/HDD变化。

## 实际结果

| 场景 | 原pidfd退出 | 原任务与物理结果 | 附加证明 |
| --- | ---: | --- | --- |
| Worker SIGKILL | 0.112996秒 | 原ID Failed/INTERRUPTED、Root Aborted、未复制 | 同容器/镜像恢复；原租约真实等待86.478219秒、41个到期前完整样本 |
| Agent restart | 0.010755秒 | 原ID Failed/INTERRUPTED、Root Aborted、未复制 | Agent PID变化、维护PID不变，152样本全200/healthy/Ready/no-store |

故障前后Owner仍为同一管理员，没有取消、退出或撤权；目标意图/执行Job取消时间null，双ID cancel-requested审计严格0。失败码是`CONTAINER_RECYCLE_ARCHIVE_INTERRUPTED`，不能把用户取消的Canceled或Unknown算通过。实际规划/执行19字段Jobs、公开receipt、原来源链与SQL一致，原规划Ready历史不覆盖，无重试按钮或重新确认旧操作。

Worker恢复后不修改SQL/时钟，直接等待原lease_expires_at_utc。每个完全落在到期前的窗口核原token/expiry、Applying/Running与barrier、不可变来源/命令/计划/签名/预留/Apply字段、Root和claim原字节及hash、源回收/purge/marker和HDD实际重读；目标与stage缺席。跨越expiry的采样不计到期前样本。完成时间不早于原expiry，新Worker只观察/撤销原预留，不重新Apply。Root仅允许Reserved或Aborted且四个复制身份为空；claim必须对应原操作或在Aborted后缺席。

每轮真实身份/Vue在新资源上完成来源回收、清理Ready取消/明确清理、归档Ready取消/明确确认，父进程逐阶段独立放行。随后实际Jobs详情、9普通/5匿名拒绝、正常双端退出及同Subject重登9旧引用404通过。独立live读取通过后，仅移除本轮已核对的空旧private/自有夹具，HDD保留；final和第三次当前独立重读均通过。第一轮封存后才开始第二轮，基线变化按各自服务范围核验。

维护152个样本只证明本次Agent窗口的观测，不补签整体发布升级零中断。源/HDD同device，不签跨物理盘灾备；1MiB用例不抹去之前大文件和身份页未定因问题。

## 首轮失败与修正

首版87dcf0c6用新资源完成了Worker故障和自然租约，浏览器及父级业务证明也完成；独立live核验器却遗留了从取消门禁复制的“取消审计=1”断言。该断言与重启必须无取消相反，首轮按失败保存，未签整链通过。

失败后另行只读核对真实SQL/公开保存回执、Root/文件/HDD、服务与自然租约证明，准确清理自有夹具，服务已恢复同ID/image/healthy；这些收尾明确标记notGateAcceptance。没有改业务状态。修正为双ID计数与保存值一致，再调用共享严格零审计证明，并新增负向检查。之后用另一套全新资源完整复验Worker，再运行Agent；旧Apply未重放、原失败未覆盖。

修订后完整526项回收root测试零跳过，最终12专项、8范围/14 CI合同及4份Python/1份Node语法通过。最初525全量之后补入的只读故障前绑定、expiry及源记录重读经专项/语法检查，最终修正版本526完整重跑。上批08873d58准确CI `42300680074` 固定12路全success；本批新QA完整CI终态仍待核验。

## 原始回执与封存

实际journal按原invocation匹配passed及正常退出，不使用临时unit回收后的默认状态：

- Worker：`whaledeck-qa-archive-restart-b997ba6e0001` / `c06e36f2fcc24cdf8b067b57d15c584b`，286.574秒。
- Agent：`whaledeck-qa-archive-restart-b997ba6e0002` / `d839399a4cb649c5a1f90b6d1e5f8d2b`，232.661秒。

root目录0700/文件0600；文件数不含manifest，目录均在deploy/artifacts：

| 目录 | 文件数 | manifest SHA256 |
| --- | ---: | --- |
| `container-recycle-archive-restart-74ebe5d87390468cb08bf0e5250b91a6` | 232，含141源码/12真图 | `bb939d4b63abb5ccc78b0499846659d122a0178dd961ff8bf60b12b708bf0312` |
| `container-recycle-archive-restart-e216d2dc7c8b4fb2870030f7d2ecdd9c` | 229，含141源码/12真图 | `950dbb2145b4e0e8ded0c529d28d36c48d2781c18f35b0edc0b9728bddd364ec` |
| `container-recycle-archive-restart-20b0b1bbf06f4d338dbda18c38a252c8`（首轮失败/收尾） | 226 | `41bd497a850d4f330cce95a57e7b58c734dc7b4f75311073adc0cbf41ab6a7c2` |
| `container-recycle-archive-restart-prep-711591c21e2245ab9307c715b819f4ce` | 143 | `587394dbc6c26f48a6b83d81cb346e49edb8543de10720eade62ac0e4c2286bd` |
| `container-recycle-archive-restart-corrected-462076f54d934975b8a5b744bf7f7a4a` | 144 | `aef1c8ca54863cedc7f9bb66fe1aa7ecf96649b58be9b8a05f7514f503d0057a` |

含失败原件后，最终64回收/29恢复/37清理/18归档、活动0；Root/HDD160/151、恢复82、目录98、purge62、archive22。只增三轮限定记录，允许失败和复验两次同ID/image Worker启动变化，以及Agent分项的Agent PID变化；旧业务、2346保护、11核心其他实例、21安装模块、f318d9ab和user NOPASSWD保持。

继续写后、Unknown、来源/跨写入、claim暂存/重复来源、长期策略/维护连续性、大文件/退出稳定性及59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
