# 清理Reserved执行中：Worker强杀、自然租约与Agent重启

2026-11-06，产品仍9694af1c。承接[实时撤权两场景](CONTAINER_FILE_RECYCLING_PURGE_REVOCATION.md)，本批用两套全新自有容器/真实身份，分别通过Worker SIGKILL后自然租约重领、固定Agent服务重启的原ID安全收敛。只签Native Reserved切点，不代表写后/Unknown或全部故障矩阵完成。

## 范围和验证准备

新增专用服务故障/自然租约证明、父门禁、真实浏览器、独立核验器和10项检查。149项清理相关Linux root检查全通过、零跳过；14 CI合同、8范围和Node语法通过。固定Worker对象与Agent服务的操作均受前置范围检查约束：仅豁免本轮规划及执行Job，14类文件业务表其余活动/围栏/租约、终端、主机锁和其他运行中的WhaleDeck临时服务必须为空；退出后无进程的临时单元只作为回执保留。当前QA主单元还须与自身MainPID/Invocation/cgroup匹配。

沿用严格四FD/pidfd切点，固定apply程序及本轮容器根、原执行锁、原来源锁和当前attested HDD数据。暂停后Native Reserved、来源Trashed/marker Reserved，无pendingIdentity或unlinkConfirmed；SQL已经Applying且reservation_json/hash、apply_started_at持久化。预留和Apply入口不等于删除事实，也不改写为缺席。当前权限必须保持管理员；没有取消或撤权，因此预期是Failed/CONTAINER_RECYCLE_PURGE_INTERRUPTED，而不是Canceled。

故障前再次读取原SQL与原pidfd，完成范围证明后才记录service-fault-attempt并执行固定动作。Worker只向原容器ID发送SIGKILL，并按实际运行状态显式启动同一容器，不替换镜像、重建容器或调整策略。Agent仅重启whaledeck-agent.service，验证新实例真实执行已安装apphost并保持相同sourceRevision，同时观测维护入口；维护进程必须不变。失败时准确终止已严格绑定且暂停的原helper、修复本轮实际尝试的服务，保留未决自有资源，不恢复破坏性Apply或重放旧操作。

Worker恢复后不改数据库或时钟，直接等待原lease_expires_at_utc。到期前每个完整读窗口都核对原token/期限、Applying/Running与围栏、不可变命令/来源/计划/签名/预留及Apply入口、Root/marker原始字节和原件/HDD。跨越到期的读取窗口不算到期前样本；不能把合法重领误作提前变化。自然到期后新Worker只观察/取消原预留，不重新Apply。

## 实机结果

| 场景 | 原pidfd退出 | 恢复与观察 | 原任务终态 |
| --- | --- | --- | --- |
| Worker强杀 | 0.112131秒 | 同容器/镜像显式启动，真实等待旧租约84.081474秒，39个到期前样本 | Failed/INTERRUPTED、Root Aborted、未清理 |
| Agent重启 | 0.011648秒 | Agent实例改变、维护实例保持，152维护采样全部200/Ready/no-store | Failed/INTERRUPTED、Root Aborted、未清理 |

Worker验证f9d0f302，unit whaledeck-qa-purge-restart-f9d0f30257a8 / Invocation 08bd0536695242348e4b5b0add4ac4fc；Agent验证8cf2ee15，unit whaledeck-qa-purge-restart-f9d0f30257a9 / Invocation 88725fc252a548598f0c40b53a3a64d5。两者最终active/exited、Result success、ExecMainCode1/ExecMainStatus0，各冻结133份验证源码并匹配安装helper字节。没有安装升级、发布覆盖、主机重启、SQL/时钟修改或旧Apply重放。

每项均通过真实Authentik/Vue回收新1,048,583字节文件，来源、留存/HDD及清理Ready逐步独立核对后才明确确认。精确暂停和服务范围检查通过后才故障注入。Worker场景原租约在19:46:54.576445+08:00自然到期；到期前Root/marker及原件/HDD保持、SQL旧token/围栏未提前释放，最终completed不早于原到期时间。Agent场景的152样本覆盖重启前、期间和之后；仅Agent PID变化，维护PID与版本保持。这只证明本次Agent窗口，不补签发布升级的整体维护连续性。

两项都保留同一意图、执行Job和原生operation ID，规划接受/确认各一次，原规划Job完整Ready历史保持。原用户权限仍为管理员，取消时间两处均null、双Job取消审计0、页面cancel及故障结果前logout均0。结果Failed/CONTAINER_RECYCLE_PURGE_INTERRUPTED、Native Aborted、marker Released，purgeApplied/sourceFenced/ownedFence均false，围栏/租约释放、Outbox归零，源Root/HDD仍Trashed、原件完整留存。没有把Aborted误作用户取消。

只有父进程重核原终态、原件和HDD后，才为QA收尾由同一真实用户创建新的专用独立恢复并明确确认。同一原inode返回原路径，Restore Succeeded/Completed及SQL/claim/Root/HDD对应。独立进程在自有容器仍在时重新核验后，准确移除测试文件、空private和容器，HDD保留；每项清理后父与独立进程重新取得当前attested只读HDD FD，再第三次最终独立重读，均通过。HDD仅因专用恢复Trashed→Restored，其他metadata、数据inode/字节/属性/存储保持。

真实Jobs列表/详情16字段显示Failed/INTERRUPTED、Aborted及未清理事实，预留与Apply入口曾记录仍显示，无通用重试。每项有3真PNG，共6图；抽看320宽原流程与桌面失败Job。原清理弹窗截图捕获时仍显示轮询中，不作为即时失败提示展示证明；终态展示由实际Jobs摘要证明，不单签原弹窗的即时终态。之后正常BFF/OIDC双端退出；每项另有普通9拒绝、匿名5读取401。

## 原标签与保持证据

Worker原独立核验器stdout遗留purgeInflightLogoutIndependent名称，但结构化JSON已正确记录realServiceRestart=true，父门禁本身也输出actualPurgeRestart。业务/Root/租约与三次独立核验事实通过；原代码和错误stdout完整保留，不改写原件或补签这个旧标签合同。封存前另行只读核对cut SQL与故障起点的reservation_hash和Apply入口等不可变字段一致。8cf2ee15修正新核验器控制台标签，并将该cut/start完整绑定永久加入证明；Agent场景已用新资源执行修正后的门禁。

每项验证旧业务保持；合计另核验初始45回收/27恢复/9清理完整行SHA，仅新增两个精确来源/恢复/清理作用域。只允许原Worker容器/镜像的StartedAt及Agent PID变化，其余2346保护、11核心healthy、维护进程和旧Root/HDD保持。最终47回收、29独立恢复、11清理、活动0；SourceRoot/HDD106/105、恢复Root80、目录Root98、PurgeRoot22。既有失败完整/partial归档保留。

Worker私有证据container-recycle-purge-restart-35e474ca49564992af25e3236dce3207为197条文件散列另加manifest，摘要4a47dc40ddf829bbe994d4a43c2935ea5aba41d8b28fd18bc5373752213cf246；Agent证据container-recycle-purge-restart-1041b17b5e58476ba7d2387a81a713e1为194条另加manifest，摘要743ced9a24265105564c9c657648469ec71fa778ee78214a62d16cfe57043aab。各133源码/3真图，Root目录700/文件600。准备证据container-recycle-purge-restart-candidate-3c27e51c372f414e91b825c1843946b7为14条另加manifest，摘要f6bbac6e98ca843f4451c768175fafe0f49ee5bc54c172fa30ccf7326f56b053。所有散列逐项重算，截图复核副本与原件SHA相同，凭据未进入源码、日志或图谱。

准确dd8d4884的CI42210512884已固定12项全部success，先前浏览器时序修正获得完整终态；旧失败仍保留。本批只新增QA工具和记录，产品仍9694af1c，新提交仍待新准确CI终态。继续写后、Unknown/共享写入、留存策略/归档恢复、维护连续性及59项；源与HDD同device不计独立物理磁盘灾备。正式整项0/59、阶段0/6、工程量粗估25%–35%，酒馆/Live2D功能0%保持；17项移出不算完成。
