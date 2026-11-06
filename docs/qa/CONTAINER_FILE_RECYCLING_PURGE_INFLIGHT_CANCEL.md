# 清理执行中取消：精确切点与失败记录

2026-11-06，产品保持已发布9694af1c。承接[Ready正常退出与自然到期](CONTAINER_FILE_RECYCLING_PURGE_READY_LOGOUT.md)，本批验证真实页面确认后的原生清理进程取消。工具与失败修正不代表真实取消通过，也不代表整项或阶段完成。

## 验证边界

只在本门禁全新自有容器和真实临时管理员中登记一次来源、清理意图和确认。安装helper逐字节匹配产品版本，冻结完整验证源码闭包；不修改产品、数据库、Root记录、时钟或旧任务，不部署新版本。

SQL Applying表示持久化执行入口，不能据此声称已删除。可签取消切点只接受暂停后的Native Reserved、来源Trashed、来源保护Reserved、原留存inode/完整字节/属性与当前HDD归档保持，没有pendingIdentity或unlinkConfirmed。进入Native Applying、Removing等状态已经不适用这个无删除取消门禁。

发送暂停前必须绑定本轮确定执行ID、固定apply程序、Root UID、systemd Invocation和cgroup，以及四个互不相同的当前内核FD身份：容器根、原清理锁、原来源锁和当前attested HDD归档。pidfd打开后重新验证进程，暂停后仍要核对完整计划、原来源、物理目录、锁和HDD，最后存储attestation完成才向浏览器放行。失败时只终止已经严格绑定且已暂停的原进程，不恢复破坏性Apply；未绑定的进程不发送信号。错过切点只能记录失败和真实结果，不能重放或将可能消失的原件报成仍在。

页面明确取消后，原pidfd退出观察必须从放行前开始，在20秒内完成，且浏览器发出取消动作的单调时间不晚于观测退出。终态应SQL/执行Job Canceled、原生Aborted、purgeApplied=false、来源保护释放、围栏/租约/Outbox清空；规划Job保留原Ready历史。保留预留和Apply入口事实，不将它们误写成缺席。

若取消成功，后续仅用专用产品独立恢复返回同一原inode来收尾，先独立验证，再准确移除本门禁文件、空private目录及容器。HDD保留，清理后必须重新取得当前attested只读FD，核对仅恢复导致Trashed→Restored，其余数据/身份/属性/存储保持。另验16字段Jobs、原引用隔离、普通与匿名拒绝。上述均是通过条件，不能由候选检查替代。

## 保留的第一次实机失败

候选5d24c315，unit whaledeck-qa-purge-cancel-5d24c315，Invocation 22e6b525ca2045d58da0a0627d5d3bb4，最终exit-code/1。真实1 MiB+7字节来源回收与Ready证明完成，但没有接受到Reserved切点。后续页面请求取消，Root/SQL为Unknown，purgeApplied=null、保护保留，原路径/留存名/临时删除名都已缺席，Root pendingIdentity存在但unlinkConfirmed=false。HDD仍为Trashed且完整归档保持；不能证明已清理成功，也不能报成安全取消。

原门禁保留现场。随后仅在新的真实管理员页面对这个原意图明确处置一次，SQL Acknowledged、原执行Job Failed、Outbox归零；原Unknown Root、marker和null清理事实保持。初次收尾脚本被打开的恢复弹窗挡住退出；后续只读任务页第一版旧文案断言错误，修正文案后真实任务摘要可见，但退出助手将drawer当dialog关闭仍超时。上述退出步骤不签通过；临时身份已撤销，没有再次提交处置。真实处置前后及Job截图、失败日志均保留。

只有重新核对空private目录身份、两个目标名缺席、准确QA容器及唯一既知ordinary测试文件后，才移除这个自有测试容器和空目录；HDD不删除。清理后重新attested读取HDD、旧业务完整行SHA与2346保护/11核心保持、活动归零。此收尾不补签原取消门禁，也不是从HDD恢复已删除文件。

原私有证据container-recycle-purge-inflight-cancel-0523c312b73641a3848aa1e53cf8bf43，127冻结源码、4真PNG；171条文件散列另加EVIDENCE_SHA256.json，摘要e23a63fb08827ac476c4ba8fcce30fa67c2e407d530ff8e91c3862968011e206。

## 保留的第二次实机失败

候选4bcf57b6增加切点诊断，尝试32 MiB+7字节真实测试文件来扩大原生校验窗口。unit whaledeck-qa-purge-cancel-4bcf57b6 / Invocation 411ec960f51a43fa879f758df8c00ae1，最终exit-code/1。浏览器在来源回收完成等待超时，尚未创建清理意图、预留或删除。原身份撤销后产品结束来源为Canceled/Aborted，原文件完整保持，historyVerified=false；HDD仅有本轮.json和.partial，不应当作可恢复的完整归档。

准确核对SQL终态、原文件完整snapshot和另一个已知QA文件、无private留存、原容器后，才移除两文件和这个自有容器。HDD部分归档保留，清理后重新attested读取，metadata、partial内容/身份/属性/存储保持；全部旧业务/Root/HDD/11核心及产品版本保持，活动归零。扩大文件方案已经撤回。

原私有证据container-recycle-purge-inflight-cancel-2452107947a447af90766bd760aefbf4，127冻结源码、1真PNG；147条文件散列另加EVIDENCE_SHA256.json，摘要e491feb462a1d486a8fdc8cb4896a02f159271166d051ae8b7840348fcfd27fa。

## 第三次失败与真正原因

ee5ce70a撤回大文件并将暂停前二次服务查询移至暂停后，124工具检查通过。但新资源仍没有绑定到原Apply，诊断明确boundOriginal=false、stopSent=false、Root已经Applying；门禁继续失败，随后正常页面取消使原结果Unknown。新的管理员页面只明确处置这一个原任务一次，正常退出；SQL Acknowledged/Job Failed、Root Unknown/purgeApplied=null与完整HDD保留。原任务没有重放，准确空目录/自有资源清理后HDD重新读取、旧业务保持且活动归零。

进一步核对Agent的FileTransferGrpcService.Driver，发现传输实例Id由Guid.NewGuid单独生成，与持久化执行Job ID不同。原观察器以执行ID拼接服务名，找错了服务；旧测试也把两个ID写成一样，未覆盖真实契约。之前扩大文件、减少查询的调整不能解决这个根因，不补签通过。

## 修正候选

观察器枚举固定purges服务命名范围，逐一验证真实apply argv、Root UID、Invocation/cgroup及与本轮原操作匹配的两把锁FD、容器根FD和当前HDD数据FD，完整暂停后证明保留。传输随机ID只标识进程实例，不再充当原操作授权。两把原操作锁及计划/来源/当前存储证明仍绑定原执行，其他helper不会获得暂停权限。回归夹具明确用不同传输ID和执行ID，旧错误实现不能通过；关联124工具再次全通过、零跳过。新的实机结果另行追加。

执行中退出、撤权、重启、自然租约、Unknown/跨管理员处置、共享写入、留存策略/归档恢复、维护连续性及其余59项继续。正式0/59、阶段0/6、工程量粗估25%–35%，酒馆/Live2D功能0%保持；17项移出不算完成。
