# ACL历史恢复写入前Worker强杀与Agent重启实机验收

2026-11-03。产品继续运行`cad61828b4be064bc0a5e868ecbbdaf9ee8883bd`/协议1.52。本批不重新发布、不安装宿主或迁移、不重启主机、不回滚；实际执行一次准确原Worker SIGKILL及一次Agent服务重启，维护入口不重启。正常恢复、取消/退出和实时撤权分别见[常规六场景](CONTAINER_ACL_RESTORE_REAL_UI.md)、[取消/退出](CONTAINER_ACL_RESTORE_FAULTS.md)、[撤权](CONTAINER_ACL_RESTORE_REVOCATION.md)，不重放旧操作。

## 工具与门禁

冻结工具`12c789c5454765b98dab05abcfc4c84764d0d4dd`，40份工具源码逐字与SHA核验，安装helper字节匹配cad6182。新增独立schema9/version3，仅worker-crash/agent-restart；保留schema7取消/退出、schema8撤权及原普通ACL配置，拒绝未知、混用或错版本。完整来源证明仍绑定新恢复UUID与自己的原捕获，不回退为普通ACL修改。

预执行补齐两处证明：恢复终态通过实际`/files/acls/restores/{id}`读取`{intent,source}`，完整来源与选中捕获一致后才读取intent；重启中断恢复审计必须是Failed，不能用Canceled或Succeeded替代。原租约采样还核对restore_source_json、请求/计划/执行hash与原登录绑定，每样本保存原来源摘要。17来源/配置/审计证明、42原故障/租约边界、10恢复证明、8范围共77项检查通过，无默认跳过，Python/JS语法通过。

准确提交的[CI run41638326702](https://github.com/leng125172-code/whale-deck/actions/runs/41638326702)已观察backend、installer、native-postgres、native-valkey四路completed/success；frontend仍in_progress，不签五路终态。CI用隔离依赖，不替代本批真实身份/文件/HDD；本批也没有构建或发布新产品镜像。

## 新资源真实流程

`validate_container_acl_restore_faults.py cad6182… restart`唯一主验首轮完整通过、退出0，原件位于root私有忽略目录`deploy/artifacts/container-acl-faults-13bfae3e0e664a059eb5811a9142e203/`。两场景各用新独占容器、源树和临时管理员，正常可见Authentik→实际Vue先完成一次普通ACL修改，形成自己的旧基本ACL捕获；再从历史页面选定它，生成并明确确认新的access恢复。准备捕获确有写ACL，不能把本批全过程描述成零物理修改。

恢复目标是旧基本ACL，当前为具名UID2201/mask4 ACL，noChange=false。观察器只绑定该次UUID/owner/完整plan、容器/实例/源inode、操作锁及两个内核FD，在SQL Applying、Root Preparing/HistoryVerified=true、当前ACL原件已备份而目标尚未写入时保留准确helper现场。重启前再次排除其他活动Job、终端、九类文件意图与无关transient/cgroup；没有安装钩子、注入Cookie/HTTP响应、修改SQL/Root/时钟或由验收器杀死helper。

| 场景 | 实际事实 |
| --- | --- |
| Worker强杀，1440×600亮色 | 仅对准确原Worker容器SIGKILL，同ID/镜像重启；产品0.109秒结束原helper。真实等待原租约86.846秒，41次采样保留原token/expiry/源摘要/完整文件/Root字节/保护，原到期后才完成恢复 |
| Agent重启，320×600暗色 | 准确重启whaledeck-agent.service，产品0.012秒结束原helper；Agent1214295→1399935，同源cad6182，维护PID1214273不变。重启窗口153次有界no-store HTTP采样均200/healthy/Ready，最大相邻间隔0.209秒 |

Worker分项未缩短/换租约，也未修改数据库时间；自然观察与独立时序核验要求租约最初仍余25–95秒，记录单调时钟与真实UTC一致、到期前不提前释放或重认领。来源全文及全部权威hash/原登录绑定保持，样本均有相同来源摘要。重启后没有续做目标ACL写入或新建执行ID。

两场景最终都是原恢复意图/执行Job Failed、resultCode=CONTAINER_ACL_INTERRUPTED，Root Aborted、aclConfirmed=false、HistoryVerified=true，无发布版本，取消POST=0/SQL取消时间null，租约/保护释放。当前ACL备份仍保留，不能把Aborted说成无HDD或把中断失败说成用户取消。

每次完整读取两个2,097,159字节文件，恢复切点前后原inode/九字段指纹、uid/gid/mtime、两类ACL、普通xattr及现有子项不变。2份所选旧来源和2份新当前ACL前像HDD分列，旧SQL/Root/HDD捕获逐次保持；4意图/8Job，恢复专属来源hash审计每场景4条共8条，不存ACL正文或凭据。主验实际比对新HDD中的当前具名ACL与旧历史中的基本ACL，不用历史目标冒充当前原件。

两原登录上下文均从真实页面查询同一恢复失败终态/完整来源，再正常页面双端退出，BFF=401与Authentik=401/403及Provider导航分别留证。4张真实PNG含两个运行态和两个失败终态，状态与动作区视口断言、无横向溢出通过；人工复看320暗色失败页，“ACL修改未完成”及原ACL可读。本批没有重跑旧任务详情抽屉，不补签任务中心失败摘要UI。

## 独立核验与收尾

`verify_container_acl_restore_faults.py <本批绝对目录>`退出0、aclRestoreFaultIndependent=passed，独立读取永久SQL/Root/受信HDD，核对实际页面保留的正确恢复wrapper/原来源、原自然租约/样本/源摘要、Worker实际ID/镜像/StartedAt、两宿主来源与PID、维护窗口观测、原上下文正常退出及准确清理。两个临时身份、容器与源树均已不存在；源字节由主验逐次读取，独立观察器不声称清理后重读已删除源（sourceBytesRereadAfterCleanup=false）。

新观察器还只读验证前批e77/schema8撤权证据，通过且其87份SHA归档文件逐字保持；没有再次修改旧身份或重放旧恢复。真实原件、SQL/Root/HDD、截图、审计和SHA清单都在私有忽略目录，不进入仓库或日志。

最终11核心healthy，8数据库平台及API/Gateway的ID/镜像/StartedAt保持；Worker同ID/镜像停启，只有Agent实际PID变化且cad6182来源保持，维护PID不变。发布账本及7份配置/能力密钥/sudo文件SHA/uid/gid/mode保持，活动任务/终端/锁/屏障/未完成意图全0。user NOPASSWD按授权保留，Agent最小权限不扩大。153次HTTP只覆盖实际Agent重启窗口，不冒充整个主验过程连续可用或全程无指标缺口。

本批仅签单文件access恢复在“当前原件已备份、目标未写”切点的Worker强杀/原自然租约和Agent重启。仍需写后中断/撤权、Unknown/实例或来源变化、来源离线/替换、磁盘异常及共享写入组合；目录default故障、递归ACL/恢复、可恢复删除/长期清理及其余目标继续。源与HDD同device不算异盘灾备，59主项完整签收和25%–35%粗估不变，17项移出不算完成，master未交付。

收尾8项范围检查、854条变动MD相对链接通过；目标第1节逐字保持，前批撤权QA原文保留后追加接续。项目图谱已确认原`whale-deck/container-acl`的`whale-deck.container-acl.12c789c.restore-restarts`事件，阶段仍active，按原eventId从pending移入synced。

## 后续写后分项（2026-11-03）

以上写前重启/原租约原文及原件保持。3957cd7随后在两套新资源完成目标ACL已写后取消/Worker强杀，原执行按实际事实Succeeded/Completed并独立核验通过；新观察器只读兼容本批schema9且92文件字节保持。详见[写后证据与剩余](CONTAINER_ACL_RESTORE_POST_CHANGE.md)，不以此补签其他写后故障、Unknown、来源变化、递归ACL或删除。
