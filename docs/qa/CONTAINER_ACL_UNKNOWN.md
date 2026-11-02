# 容器ACL：目标消失Unknown、不同管理员处置与跨容器目录延期

2026-11-03。运行仍 `633a2ca42012bbd6f388a67bd128fe3125bfab76`、Agent 1.51，无重新发布或生产迁移。**修正后的冻结 `d977c7a`（proofVersion2、33份验收依赖/11份安装模块）严格主门禁与独立只读核验通过。** 首轮 `cab4dbc` 曾由旧检查器报Passed，但复核发现计数错误，已明确拒绝其“四次延期”结论、保留全部原件；不能称本轮整体首轮通过。两轮用不同新独占资源，未重复原已处置操作。

本批只签收**写ACL前Preparing/HistoryVerified，准确自有容器消失后形成Unknown**及另一容器目录的共享保护组合。[写前中断](CONTAINER_ACL_RESTARTS.md)、[写后成功对账](CONTAINER_ACL_POST_CHANGE.md)、[实时撤权](CONTAINER_ACL_REVOCATION.md)各自证据不混用。不是实例替换、实际ACL写后Unknown、所有共享写类型、递归/历史恢复或删除的完整验收；59主项保持部分实现，17项移出不算完成，master未交付。

## 严格复验的真实链路

两套新准确随机容器均用固定既有镜像、非root1000:1000、无网络、只读根和唯一自有HDD bind。第一套源包含原original.bin及acl-file.bin、acl-directory/existing.bin；两个ACL目标文件各2097159字节，原UID/GID2100:3100、模式0640/目录0750、固定mtime及普通user.whaledeck-acl属性。实际可见Authentik登录→Vue填写access ACL完整条目→Ready→正常停止原Worker→页面确认原唯一执行Job→启动同CID/镜像Worker。

精确pidfd、固定ACL传输argv/root身份、原容器root FD、原操作0600空锁FD同时绑定，原计划/摘要和完整inode、两类ACL、普通属性均重新证明，才在Root Preparing/HistoryVerified暂停。只通过fixture核对CID/镜像/标签/挂载后移除该自有非核心容器；host源不删，暂停的helper仍活并已再次确认源清单不变。原页面从原用户菜单正常双端退出；产品约**4.981秒**终止helper，QA未杀helper，没有修改Root/SQL/缓存/时钟或安装钩子。

产品得到原业务Unknown、执行Job Running、原屏障保留、aclConfirmed=null、publishedVersion为空、HDD历史已验证；原源全内容/九字段/access/default/xattr仍与最初一致。**QA看到源未变，不等于产品能够证明已消失实例的当前对象，因此不能把null改报false。** 原账号在正常退出和核验后准确删除，原Subject/登录绑定/回执继续留在业务审计中。

另一新临时管理员通过正常身份登录，Subject不同。它在另一自有容器登记真实目录任务 `work/ACL未知保护后的目录`。保护期间四个完成延期的快照版本为 **[2,4,6,8]**，均Pending、无RootStarted、无租约/发布屏障、无实际目录或私有准备目录、无该任务Root回执；原ACL Unknown/原Root字节及完整源清单保持。观察时间依次为21:03:02.853446、21:03:09.462563、21:03:16.787676、21:03:23.421886 UTC；每次后续观察均晚于前一到期时间，下一到期时间至少相隔5秒，不是快速重复轮询或初始排队。

不同管理员在真实320×600暗色Vue全局“容器ACL修改异常处置”先核对原CID及`/work/acl-file.bin`、ACL尚未确认和HDD事实，选择“暂不处置”。此时ACK POST0；父进程再次核对Unknown行、原Root与源不变、等待任务仍未放行后，才允许第二次选择并明确确认。实际ACK POST1，原ID作为幂等键，版本与当前原Unknown版本相等，Origin/confirmed字段核对。未自动重发。

原业务最后为Acknowledged，**原执行Job Failed，不代表ACL修改成功**；原actor_subject仍是已删除账号，acknowledged_by及唯一unknown-acknowledged审计属于新管理员。原SQL第一份receipt_json/receipt_hash、原命令/计划/执行ID、aclConfirmed=null、历史摘要与HDD元数据均不改；处置观察独立保存。原Root及HDD原始字节保持Unknown，未恢复ACL、删除原证据或续做原操作。

ACK完成于2026-11-03 05:03:36.531435+08:00，另一容器**同一原目录任务**RootStarted为05:03:37.101932+08:00，随后Succeeded。真实Vue容器文件页滚动到新目录行，并通过完整视口断言/截图；没有以新任务替换旧ID。目录实际空inode、UID/GID1000:1000、0750、Root Completed/版本/准备目录身份由独立文件证明核对。最后迟到同一原ACL操作Apply仍Unknown、aclConfirmed=null、replayed=true，原Root和源不变。

## 原件、权限、清理与实际页面

严格批次1个ACL意图、1个目录意图、3个唯一Job、7条审计：ACL计划接受/Ready/确认/Unknown结果各1，当前管理员Unknown处置1，目录接受/成功各1。原ACL计划Job Succeeded、执行Job Failed；目录Job Succeeded。两种任务均在最终处理Outbox、清除租约/屏障后才清理测试资源。匿名/普通用户接口拒绝沿前批证据，不将其冒称本轮重跑。

永久保留1份ContainerAclMetadata原ACL HDD元数据；原计划/实例/父inode/执行身份、原ACL、targetMode/NoChange与原Root/回执逐项绑定，attest只读重读HDD并重算规范计划/历史摘要。产品读取路径验证原回执MAC，QA不另读Secret重算MAC。历史元数据不含文件内容，不代表异盘灾备或NVMe验收。

父进程在清理前完整读取两个文件2097159字节及九字段身份、两类ACL与普通属性；源、目录及既有子项全程保持。只清理已证明的自有ACL文件/空目录和另一容器新建目录/准备目录，fixture再检查两份original.bin原内容/身份后准确移除源/CID。Root/HDD原件不删，Unknown处置不构成产品删除功能验收。不同管理员、原账号各自原浏览器上下文均正常BFF/Authentik双端退出，另HTTP登录也正常退出；没有注入Cookie或删账号伪装退出。

严格批次五张实际PNG：原进行中1440×600亮色、目标CID/路径、处置确认、Acknowledged说明与目录发布均320×600暗色，已逐张目视核对。原目标、确认/暂不处置按钮及新目录行有实际完整视口证明；新目录页没有登记/内容写请求。独立新进程重读原SQL/Root/HDD、原切点/移除证明、四次自然延期、唯一当前管理员审计、ACK前后原事实不变、晚到重放、实际目录Root和准确源/CID/临时身份消失。清理后不声称重新读取了已删除源字节。

只有Worker按两轮各自预检需要正常停止/启动，原CID/镜像保持；其他10核心容器ID/镜像/状态/StartedAt及8数据库平台不变。Agent PID3288068、MaintenanceHost PID3122598及实际exe来源633a2ca保持，最终11核心实际healthy，两宿主active，全局Job/终端/锁及各类容器活动/屏障/租约0。user NOPASSWD、密码、产品最小授权和内部隔离未改，无dev自动回滚。

## 首轮拒绝、修复与严格签收边界

首轮源码cab4dbc的主脚本及旧独立观察器原输出Passed保留，但实际采样版本为[0,2,4,6]。version0来自注册时`created+6秒`的初始排队，真实只保存了三次完成的自然延期；旧检查器只核对未来到期时间与版本递增，没有排除初始值，也没有充分约束观察时间。目录名虽通过真实Vue DOM可见性检查，首张目录截图却停在上段，不能算目录行位于小屏视口。复核后已记录review-rejected.json：strictGatePassed=false，不把旧Passed当最终签收。

修复未降低“四次”门槛：排除version<2，要求每次观察晚于前一到期时间、观察及下一到期时间间隔至少5秒；新增两个回归否定用例。恢复页增加CID/路径断言和目标截图，新目录显式scrollIntoViewIfNeeded与完整InViewport断言；proofVersion2强制使用新证明形状。随后仅在**两套全新资源**上跑完整流程，严格主门禁与独立核验均通过。原两套资源、意图、处置及目录操作未重复，旧SQL/Root/HDD失败原件保留。没有把失败清理或部分重跑算通过。

最终**10项Unknown/ACK/延期边界＋34项ACL观察器边界＋8项范围=52项通过**，Python -W error、Node语法及diff检查通过，新工具已接CI。它们是证明工具的离线边界，不替代严格真实门禁；本轮未重跑全量后端/前端/生产预览，不声称远端CI全绿，没有新部署。失败清理虽允许当前管理员通过正常BFF精确处置本案Unknown以恢复保护，但必须另记gatePassed=false；两轮该fallback分支均未触发。

严格root私有0700证据目录 `deploy/artifacts/container-acl-unknown-d5360a6154684613aa351838a44dd073/`，40份原件的SHA目录，JSON/日志/PNG均0600；包含33依赖/11安装来源、基线/原切点/双FD绑定/准确移除/原SQL-Root-HDD/四次延期/暂不处置父子握手/ACK与等待任务最终结果/晚到Apply/全源物理清单/五PNG、independent-final和final-summary。首轮被拒绝的完整目录 `deploy/artifacts/container-acl-unknown-38b09682690e4e5fa0a348021506f4a4/` 另存原弱检查器输出、review-rejected.json及原件SHA。日志 `/tmp/wd-acl-unknown-first.log`、`/tmp/wd-acl-unknown-independent-first.log`、`/tmp/wd-acl-unknown-strict.log`、`/tmp/wd-acl-unknown-independent-strict.log` 留存。凭据仅内存/隐藏环境，不入仓库、日志或密码表单截图。

剩余：ACL实际写后撤权/Agent重启与写后或实例变化Unknown、其他共享写入延期组合，ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D；本批只签当前容器消失+另一容器目录这一组合。

文档终检：231份Markdown、1441处相对链接无缺失，目标第1节逐字不变、前批写后QA原始正文逐字保留。项目图谱事件 `whale-deck.container-acl.d977c7a.target-missing-unknown-directory` 已取得projectId/key/eventId一致回执并移入synced，阶段保持active。
