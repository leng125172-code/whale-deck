# 递归ACL Unknown 跨容器目录延期与释放

2026-11-04，承接[同CID实例变化](CONTAINER_RECURSIVE_ACL_INSTANCE_CHANGE.md)。最初为候选；下文保留两次原总失败、独立恢复以及最终新资源完整实机验收，只有已证明的组合计入分项。

新的原容器真实首项ACL修改后目标消失/正常退出，保留Unknown与原Root/HDD；另一位当前管理员通过真实Vue为另一新隔离容器规划和登记0750目录。要求四个不同版本、不同期限的自然延期：原Unknown/SQL/Root与完整物理保持，等待目录无RootStarted/租约/发布屏障或完成，目标与目录Root原件不存在，原文件保持；不改SQL/时钟或强制重试。

当前管理员先暂不处置原Unknown、再一次明确ACK；只在处置后原目录ID实际开始并Succeeded，核对原主体/登录绑定/计划/请求摘要/原ID、Root指纹/版本与真实inode/owner/mode。返回同容器页面必须显示原任务和实际目录，不能用API登记冒充真实页面登记。准确证明后才清理两套自有资源，原ACL历史与新目录Root均留存。等待计划令牌只保存SHA-256，不保存原文。

110相关工具root测试零跳过、8范围、两Node语法及diff检查通过；仍需干净冻结后的真实身份/页面/Worker/Agent/磁盘与独立核验。产品仍b880208/1.53，没有产品发布。其他跨写入/路径磁盘依赖、递归恢复、删除及其余59项继续。

## 两次原总门禁失败（保留）

51fe911首次Invocationa661784171c44d5a892271ee5aa16c8c，17:50:57–17:52:09 CST退出1，未捕获部分写入cut，原操作真实Succeeded/Completed及4/4/0/0/0。原件 `container-recursive-acl-partial-a13442c065384cee8dfc092e66c742b7`；等待容器登记0，未发生Unknown/ACK。仅只读补核SQL、5Root/5HDD、完整物理后精确清理；SQL投影明确非原浏览器完成回执，不补签正常退出。补核首次统计误用不存在resource_id，只读查询失败另存，更正后通过；539旧保护文件及45旧目录Root保持。

同源码新资源Invocationbdda06ea0eac4b5ca7fd6ec31e2afb76，17:56:19–17:58:06 CST退出1；原件 `container-recursive-acl-partial-d6a8627144bd402ea4ccb7443dc6ee8e`。实际Unknown及另一容器真实Vue计划/登记已发生，但采样时正常短暂claim租约被工具误判。修正只将双空租约的真实延期计入样本，单次领取不算延期；RootStarted/目标/Root存在仍拒绝。

失败恢复仅接受这个准确目录和Invocation：由新当前管理员真实页面明确处置原Unknown，并通过正常管理员HTTP请求取消已失去原临时身份/测试容器的旧等待任务，保持其原主体、登录绑定、计划和请求摘要；不替换主体或重登记，不计作跨容器目录发布验收。原两总失败永久保留，待新资源完整复验。

## 失败恢复与最终完整验收

修正冻结 `8666da0e6ecbe43875a6fc48738e034972b2c2f0`，112相关工具root测试全部通过（零跳过）、8范围和两Node语法/diff检查通过。轮询遇到合法短暂claim租约时返回“不计样本”，只有双空租约、未来重试期限与Pending无RootStarted/目标/Root的记录才计延期；保存的四样本仍严格拒绝活租约。没有修改产品重试时间、SQL、Root或时钟。

失败专用恢复Invocation `82f85613a16f44c8a09fd643ee1302bf`，18:07:10–18:07:34 CST准确退出0，原件 `deploy/artifacts/container-recursive-acl-partial-6b573a47c3484bfc9f8bb5cded949217/`。当前管理员真实页面处置原Unknown并正常双端退出；另一次正常管理员HTTP登录，明确取消旧等待Job，原主体/登录绑定/计划/请求摘要保持，最终Canceled、Root Aborted/pendingIdentity空、RootStarted空、outbox0。准确清理原源，不重做登记、执行或删除；独立只读补核通过。`resolution.json` / `resolution-independent.json` 明确originalGatePassed=false、crossFenceAcceptance=false，不把失败收尾当作成功发布。

最终第三套新资源完整主门禁及独立核验通过：`deploy/artifacts/container-recursive-acl-partial-e46440091e9f44e480b84332c058ae5f/`，unit `wd-qa-recursive-acl-cross-directory-final-8666da0.service`，Invocation `5e56fb5cca614fed881b4384d18cdefb`，18:09:28–18:11:38 CST准确退出0。原两次退出1及成功恢复/最终门禁均在停止对应已结束QA unit前保存准确Invocation、MainPID、Result、ExecMainCode/Status和原日志；失败unit只在归档后停止/reset，不删除旧失败回执。

## 实际延期、明确处置与原任务发布

两套新的非root/无网络/只读根隔离容器和两位临时管理员。原容器真实Authentik→Vue预检/确认，首项ACL真实变化、Completed/原HDD证明及其余三项原样后，只移除准确自有容器；原上下文正常退出，产品4.325秒结束准确FD/cgroup/pidfd绑定helper。原Root/SQL Unknown五计数0/0/0/0/4，原首项实际修改与Completed原字节单独留存，不能把当前四项待证明解释成零修改。

另一管理员在另一新容器的真实Vue文件页面输入目录、选择0750、生成计划200并确认登记202/Pending；只一次计划/一次登记，Origin和幂等头实际存在，没有脚本API登记或上传。只保存实际POST的计划令牌SHA-256、确认标记及幂等key；摘要与永久SQL plan_hash直接比较，不存令牌原文。

四次自然延期分别为版本 **2、4、6、8**，期限各不同且按真实时间推进至少5秒；每次均Queued Job/Pending意图、无RootStarted/发布屏障/完成、租约双空、目标目录与目录Root原件缺席、original.bin字节九字段保持。与此同时原Unknown完整SQL、Root字节及完整实际物理清单保持，没有自动解除保护或续做原ACL。

另一管理员真实共享异常入口显示原Unknown/实际五计数，原owner端点404；先“暂不处置”ACK0且原SQL/源/Root/HDD保持，再一次明确ACK202→GET Acknowledged，原执行/计数/Root/HDD不变，唯一unknown-acknowledged审计为不同主体，保护释放。原等待目录ID、主体/登录绑定、CID/实例、路径、原计划及请求摘要始终保持；目录实际RootStarted发生在原ACK完成 **6.001秒后**，随后Succeeded/目录创建/Job Succeeded/outbox0，唯一受理与完成审计，未重新登记或换任务。

实际核对目录Root Completed的原operation/owner、计划指纹/目标证明/父版本、原命令/0750、发布版本与真实空目录inode、1000:1000属主、0750模式及私有工作目录身份。随后返回同一容器Vue页面，原任务显示成功、实际目录可见；原waiting-pending与waiting-succeeded两图是实际页面，不是测试接口。恢复管理员正常菜单双端退出，原目录私有GET401，取消/上传0；原用户也正常双端退出及原GET401。

共7张真实320×600明暗PNG（原ACL进行中、原退出、Unknown计数/确认/处置结果、另一容器等待及发布结果），已目视核对等待与发布、确认按钮；完整物理证明后才精确删除空目标和空私有目录，再由fixture清理原文件及两自有容器。原1聚合+1子项HDD/2Root、新目录Root均保留；两临时身份删除。独立核验重读SQL、Root/HDD、原四次延期、处置/目录开始时间关系、唯一审计、真实UI计数及准确源/容器/身份缺席；不声称删除后重读源字节。

## 最终状态与未验范围

产品仍 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent1.53，没有产品发布、迁移、Agent/维护入口重启或权限扩展；112工具/8范围不冒充本批全量C#/Vue重跑。旧实例变化门禁只读兼容通过，不重放旧重启或改写旧原件。Agent4044173/维护3598736及来源保持，11核心running且健康检查healthy，活动0；user NOPASSWD:ALL按授权保留，凭据只经stdin/服务端内存。

起始539、第二批549、最终批553个配置账本/ACL Root/HDD保护文件均保留原字节九字段；当前8配置账本、145普通ACL Root、117递归Root、153普通ACL HDD、134递归HDD，共557个保护文件，另47个目录Root（45旧原件、1失败取消栅栏、1新完成原件）。最终批46个既有目录Root保持。递归ACL数据库30意图：9Succeeded/5Canceled/1无执行Expired/3既有中断Failed/12Acknowledged、28执行/58相关Job；本批另两目录Job分别失败收尾Canceled和真实发布Succeeded，不混作递归意图。

源与逻辑HDD实际仍同设备2049，不签物理跨盘或完整NVMe/HDD布局。推送曾遇TLS握手、远端Internal Server Error及直连低速，保留TLS校验的有界重试后8666da0已推送；上一批9fe980e CI41806186808完成success，本批8666da0 CI41812644148最后查询in_progress，尚无成功终态。

本批只签“目标消失Unknown→跨容器单目录四次自然延期→不同管理员明确ACK→原目录ID成功”这一组合。其他跨写入类型/路径磁盘依赖/时点与迟到RPC、递归ACL历史恢复、可恢复删除/长期清理、维护入口完整连续性及其余59项仍待；17项移出不计完成，酒馆/供应商/Live2D和紧凑UI子计划保持，不提高整项完成率。

项目图谱首次连接失败，pending保留后以同事件 `whale-deck.container-acl.8666da0.cross-directory-fence` 重试成功并同步active；准确回执保存，不提升完整目标状态。
