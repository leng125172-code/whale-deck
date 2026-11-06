# 清理执行中取消：精确切点与失败记录

2026-11-06，产品保持已发布9694af1c。承接[Ready正常退出与自然到期](CONTAINER_FILE_RECYCLING_PURGE_READY_LOGOUT.md)，本批在保留三次失败及安全收尾后，用全新资源通过真实页面确认后的原生Reserved切点取消整链。仅签该精确组合，不代表整项、阶段或全部清理故障矩阵完成。

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

第三次私有证据container-recycle-purge-inflight-cancel-c2b86f207e66494291d840bd208669f6，127冻结源码/3真PNG，165条文件散列另加manifest，摘要de2d5231a1e15c2d0691c4ca89e9cd3205991a4e0805fd6a683f9ef913348403。

进一步核对Agent的FileTransferGrpcService.Driver，发现传输实例Id由Guid.NewGuid单独生成，与持久化执行Job ID不同。原观察器以执行ID拼接服务名，找错了服务；旧测试也把两个ID写成一样，未覆盖真实契约。之前扩大文件、减少查询的调整不能解决这个根因，不补签通过。

## 修正候选

观察器枚举固定purges服务命名范围，逐一验证真实apply argv、Root UID、Invocation/cgroup及与本轮原操作匹配的两把锁FD、容器根FD和当前HDD数据FD，完整暂停后证明保留。传输随机ID只标识进程实例，不再充当原操作授权。两把原操作锁及计划/来源/当前存储证明仍绑定原执行，其他helper不会获得暂停权限。回归夹具明确用不同传输ID和执行ID，旧错误实现不能通过；关联124工具再次全通过、零跳过。下面记录修正候选的全新资源实机结果。

## 修正后全新资源真实整链通过

验证4d25ccfc，unit whaledeck-qa-purge-cancel-4d25ccfc / Invocation db8b4b88d96541a6a51fa8d0da8e77e4，最终active/exited、Result success、ExecMainCode1/ExecMainStatus0。冻结127验证源码，安装helper/六组件仍9694af1c，无覆盖发布或Worker/Agent重启，只暂停这次通过严格证明的原apply helper。

新1,048,583字节文件经真实Authentik/Vue回收，父进程核对Source/HDD/Ready再允许明确确认清理。观察器绑定独立随机传输单元和原操作四FD，暂停后完整证明Native Reserved、来源Trashed、marker Reserved、无pending/unlink，原inode和HDD保持。SQL此时Applying且预留/Apply入口已经持久化，回执仍缺席；没有据此声称已删除。

父进程先启动原pidfd退出观察，再放行页面取消。浏览器动作单调时间落在观察起点与退出之间，3.015795秒观测原进程退出。原意图/执行Job Canceled，Root Aborted、purgeApplied=false、sourceFenced=false、ownedFence=false；确认一次、取消一次及对应执行审计一次，围栏/租约释放、Outbox归零。原规划Job保留完整Ready/Succeeded历史；预留和Apply入口事实保留，未改写成没有进入执行。

取消证明通过后，页面仅为QA收尾走该来源的独立恢复预检和明确确认；父进程先重核原取消终态/来源保持，再放行。实际同一原inode返回原路径，Restore Succeeded/Completed及SQL、claim、Root/HDD逐项一致。独立进程重开物理命名空间证明通过，才移除准确测试文件、空private及自有容器。HDD不删除；准确清理后父进程、独立进程和第三次独立最终读取均通过，captureState仅由专用恢复Trashed→Restored，数据inode/字节/属性/存储保持。Aborted purge原件以取消时归档证明，不拿恢复后的来源历史反写旧取消事实。

真实Jobs列表/详情的16字段对应原SQL摘要，明确显示Canceled/Aborted、删除入口曾记录但清理完成事实为否，没有通用重试。3真PNG，抽看320宽取消页及桌面Job。两次正常BFF/OIDC双端退出；同Subject新登录，原ID/key、来源和QA恢复共4旧引用404。真实普通用户5GET/4POST共9拒绝、匿名5GET均401，原退出后的旧入口另验401；凭据没有写入源码、日志或图谱。

旧41回收/23独立恢复/5清理完整业务行SHA和旧Root/HDD/2346保护保持，11核心healthy。最终42回收、24独立恢复、6清理，活动0；SourceRoot/HDD91/95（包含第二次失败保留的.partial）、恢复Root65、目录Root98、PurgeRoot12。前三次失败不会被第四次通过覆盖或补签。没有重放旧Apply；未删除任何HDD归档或原生历史。

成功私有证据container-recycle-purge-inflight-cancel-d77754e623b54cd59f279e4576f35961，127冻结源码/3真PNG，184条文件散列另加EVIDENCE_SHA256.json，摘要ef07ecf73dc559791e2214b50dd4a2d47e1cedd47ac67b84a30c0efb58989996。准备证据container-recycle-purge-cut-candidate-53695c7f412e4d41bb79d3ca87ea3e28为9条散列另加manifest，摘要c2c90a1d35fcfdcaa255708f656841acb4b852f6fbb3a5a70118b081fec3d2ee；初期夹具/静态断言失败另保留。所有manifest逐项重算，原件root私有权限保持，只逐字复制两图到另一私有复核目录。源与HDD同device的既有限制仍在，不计独立物理磁盘灾备。

执行中退出、撤权、重启、自然租约、Unknown/跨管理员处置、共享写入、留存策略/归档恢复、维护连续性及其余59项继续。正式0/59、阶段0/6、工程量粗估25%–35%，酒馆/Live2D功能0%保持；17项移出不算完成。
