# ACL历史恢复写后取消与Worker回执中断实机验收

2026-11-03。产品仍`cad61828b4be064bc0a5e868ecbbdaf9ee8883bd`/协议1.52，本批不发布、安装、迁移、重启Agent/维护入口/主机或回滚。仅准确原Worker容器按门禁正常停启，并在一个场景实际SIGKILL。写前原自然租约等证据见[前批重启](CONTAINER_ACL_RESTORE_RESTARTS.md)，本批使用全新资源，不重放旧恢复。

## 工具与门禁

冻结工具`3957cd725938a0836ec611a1b6abbb6b18e69f42`，40份源码逐字及SHA核验，安装helper字节匹配cad6182。独立schema10/version4仅post-acl-cancel/post-acl-worker-crash；旧schema7–9范围保持。来源绑定、新执行ID、正确恢复wrapper和旧/当前ACL前像证明继续完整执行，写后审计必须记真实Succeeded；取消请求另有Accepted审计，不用请求状态覆盖最终磁盘事实。

19来源/配置/审计证明、43原故障/真实ACL切点边界、10恢复证明及8范围共80项检查通过，无默认跳过，Python/JS语法通过。新增Linux-root临时文件测试证明观察器能读取“access xattr缺失时的基本ACL”语义；**不将其当作产品执行removexattr的证据**。实际切点证明规范ACL目标、原inode及ctime/属性，不宣称特定syscall形式。

准确工具提交的[CI run41642109643](https://github.com/leng125172-code/whale-deck/actions/runs/41642109643)已观察backend、installer、native-postgres、native-valkey四路completed/success，frontend仍in_progress，不签整条CI终态。CI隔离测试与下列实际身份/业务/磁盘验收分列。

## 两套新资源真实写后切点

唯一主验`validate_container_acl_restore_faults.py cad6182… post-change`首轮完整通过、退出0；原件位于私有忽略目录`deploy/artifacts/container-acl-faults-1ffe6303a131432bb855a28664e7d80b/`。两场景各自正常Authentik页面登录，通过实际Vue先产生一次普通ACL修改和自己的旧基本ACL捕获，再从历史页面选择该来源、新建预检并明确确认恢复。准备捕获和目标恢复都实际改过ACL，不把全过程描述成无物理写入。

恢复前当前为具名UID2201/mask4 access ACL，来源目标为原基本ACL，noChange=false。观察器严格绑定本次UUID/owner/完整plan、容器/实例/源inode、锁与两个内核FD；只有**目标基本ACL已经实际可读、不同于原具名ACL、ctime增大、Root仍Applying/aclConfirmed=null且当前原件HDD已验证**才保留原helper现场。SQL仍Applying、原执行尚未收到永久完成回执。其他ACL/普通属性/文件身份不允许夹带变化；不注入Cookie/HTTP响应，不改SQL/Root/时钟或安装钩子。

| 场景 | 实际结果 |
| --- | --- |
| post-acl-cancel，320×600暗色 | 真实页面先“继续等待”不发取消，再明确一次取消POST；产品5.278秒结束原helper。按已写目标事实完成原意图/执行Job Succeeded、Root Completed，保留新发布版本 |
| post-acl-worker-crash，1440×600亮色 | 仅准确原Worker SIGKILL并以同ID/镜像重启，产品0.114秒结束helper。原租约实际等87.034秒、41次采样后，以原执行ID对账为Succeeded/Completed，未续做目标写入 |

两场景resultCode=CONTAINER_ACL_COMPLETED、aclConfirmed=true、HistoryVerified=true、PublishedVersion与真实新指纹一致、BindingCurrent=true；取消场景SQL取消时间保留，强杀场景取消POST=0/取消时间null。请求取消没有自动撤回已经写入的ACL，也没有被误报为Canceled/Aborted。

Worker等待期间保留原token/expiry、发布保护、来源全文/请求-计划-执行hash/原登录绑定及来源摘要。Root原Applying字节保持，物理目标基本ACL及新ctime/版本保持；原到期前不提前释放或换租约，不修改数据库/系统时钟。独立观察器核对真实UTC、单调时钟、41次样本与终态完成时间不早于原租约到期。

每个场景完整读取两个2,097,159字节文件。正文、原inode、uid/gid、mtime、普通xattr、另一类ACL及现有子项保持；目标access ACL与ctime发生并保留预期变化，切点至终态完整新清单不变。2份所选旧基本ACL捕获与2份新当前具名ACL前像HDD分列，旧SQL/Root/HDD逐次保持；4意图/8Job，恢复来源hash审计取消5/强杀4共9条，不存ACL正文或凭据。HDD前像为ACL元数据，不含文件正文。

终态后各对原operation/owner/plan调用一次既有固定helper Apply，均返回原Completed回执、replayed=true、原version/historyDigest，Root/HDD及完整新文件清单不变。此迟到探针是固定helper原操作重放，**不是新增签名RPC**；前面的实际Vue/API/Worker执行已经经过签名Agent链路。没有用重新登记或新执行ID代替恢复原操作。

两原登录上下文真实读取`/restores/{id}`的完整source和Succeeded intent，随后正常页面双端退出，BFF=401、Authentik=401/403与Provider导航分别留证。4张真实PNG含运行态和完成态，状态与动作区视口/无横向溢出断言通过，人工复看320暗色完成页。本批没有重跑旧任务详情抽屉，不补签任务中心更多摘要UI。

## 独立复核、清理与剩余

独立`verify_container_acl_restore_faults.py <本批绝对目录>`退出0、aclRestoreFaultIndependent=passed。只读核对永久SQL/Root/受信HDD、原来源/当前前像、已写目标与新指纹、原租约/保护/来源hash、实际页面保留的恢复wrapper、迟到原Apply及原上下文退出和准确清理。两个临时身份、容器及源树已不存在；源物理字节由主验逐次读取，不声称清理后由独立观察器重读已删除源（sourceBytesRereadAfterCleanup=false）。

新观察器只读兼容前批schema9重启证据，通过且原92份SHA归档文件逐字保持，无新生产写入或旧身份操作。当前及旧批原件分别保留，不覆盖旧结论。

最终11核心healthy，8数据库平台及API/Gateway的ID/镜像/StartedAt保持，Worker仅同ID/镜像停启。Agent1399935/维护1214273及cad6182来源保持，发布账本和7份配置/能力密钥/sudo文件SHA/uid/gid/mode保持；活动任务/终端/锁/屏障/未完成意图全0。user NOPASSWD保留，Agent最小授权不扩大。私有证据与SHA不入Git或产品日志。

本批只签单文件access恢复在“目标已写、Root仍Applying”切点的取消和Worker强杀。仍需写后实时撤权/Agent重启、Unknown/实例或来源变化、来源离线/替换、磁盘与共享写入组合；目录default故障、递归ACL/恢复、可恢复删除/长期清理及其余目标继续。源与HDD同device不算异盘灾备，59主项完整签收和25%–35%粗估不变，17项移出不算完成，master未交付。

收尾8项范围检查、858条变动MD相对链接通过；目标第1节逐字保持，前批写前重启QA原文保留后追加接续。项目图谱已确认原`whale-deck/container-acl`的`whale-deck.container-acl.3957cd7.restore-post-change`事件，阶段仍active，按原eventId从pending移入synced。

## 后续撤权与历史读取故障（2026-11-03）

以上写后取消/Worker强杀原文保持。随后写后移组仅一个分项实机通过，停用及新批在历史列表503时失败，未签整批；已定位维护入口5秒预算截断并提交b69302b限定代理/软分页候选，未部署。见[实际失败、根因与剩余](CONTAINER_ACL_HISTORY_TIMEOUT.md)，不将候选、内部直达诊断或重试算真实入口通过。
