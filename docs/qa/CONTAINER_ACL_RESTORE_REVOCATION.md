# ACL历史恢复实时移组与账号停用实机验收

2026-11-03。产品仍为`cad61828b4be064bc0a5e868ecbbdaf9ee8883bd`/协议1.52；本批不发布产品、不迁移、不安装或重启Agent/维护入口、不重启主机、不回滚。仅原Worker同ID/镜像正常停启，取得准确自有恢复helper切点。正常恢复见[六场景](CONTAINER_ACL_RESTORE_REAL_UI.md)，取消/退出见[上一批故障证据](CONTAINER_ACL_RESTORE_FAULTS.md)，不重做其中的UUID操作。

## 工具配置、修正与验证

工具冻结`e77c1a6dad896191a2c0b92e1c76c78f2dc23288`，40份源码逐字及SHA核验，安装helper逐字匹配cad6182。新增独立schema8/version2，只允许group-removal/account-disable；旧schema7/version1仅cancel/logout，未知/跨配置/混用/布尔版本拒绝。原schema1–6不得冒带恢复标志；旧普通ACL流程保留。

预执行复核发现两处工具缺口，均在真实身份变更前修正：版本2曾会落回普通ACL目标分支，现所有显式恢复配置先校验再进入完整来源证明；撤权读取曾可能走通用ACL路径，现必须为实际`/files/acls/restores/{id}`，独立观察器亦拒绝错误或外来ID路径。14项来源/配置证明、41项原故障及撤权边界、10项恢复证明和8项范围共73项通过，无默认跳过；Python/JS语法通过，CI继续接入原测试入口。

准确工具提交的[CI run41635531772](https://github.com/leng125172-code/whale-deck/actions/runs/41635531772)已观察backend、installer、native-postgres、native-valkey四路completed/success，frontend仍in_progress；不签五路终态，也不用产品cad6182先前五路成功替代新工具CI。

## 两套独占资源真实整链

唯一主验命令为`validate_container_acl_restore_faults.py cad6182… revocation`，首轮完整通过、退出0；私有忽略目录`deploy/artifacts/container-acl-faults-b4825a42c504419483b1e98a1baf1421/`。两个场景各创建独占隔离容器/源目录及临时管理员，通过正常可见Authentik登录和实际Vue生成一次新的普通ACL修改/旧基本ACL捕获，再通过历史页面选定该捕获，登记并明确确认新的恢复意图。准备捕获确有一次ACL修改，不能把本批全部操作描述成物理零写入。

恢复来源为原基本access ACL，恢复前当前为具名UID2201/mask4 ACL，两者分别核对且noChange=false。精确观察器绑定新UUID/owner/完整plan、容器/实例/源inode/锁及两个内核FD；SQL Applying、Root Preparing且HistoryVerified=true，当前ACL的HDD原件已留存，目标ACL尚未写入。没有安装测试钩子、注入Cookie/HTTP响应、修改SQL/Root/时钟或由验收器杀死helper。

`OwnedAuthentikRevocation`仅对本次生成身份执行一次PATCH，先核验username/UUID/数字PK/原管理员组及全部原字段，拒绝其他账号；响应丢失也不重发变更。移组只写groups=[]，停用只写is_active=false，分别读回核验。原登录、原主体、未过期正权限快照保留，不用重新登录、手工取消或等待快照过期代替实时撤权。

| 场景 | 实际观测 |
| --- | --- |
| group-removal，320×600暗色 | 权限快照仍余7138.461秒；产品4.938秒停原helper；原恢复GET403，/auth/me=200且isAdministrator=false，原页面/forbidden，引用清空 |
| account-disable，1440×600亮色 | 权限快照仍余7137.025秒；产品4.858秒停原helper；原恢复GET403，/auth/me=403，原页面/login，引用清空 |

两场景均为原意图/执行Job Canceled、resultCode=CONTAINER_ACL_CANCELED、Root Aborted、aclConfirmed=false；取消POST=0、SQL cancel_requested_at_utc=null，无发布版本，租约/保护释放。被撤权上下文**没有正常双端退出回执**（normalDualLogout=false），不把失权/停用冒称用户正常退出。

恢复切点前后完整文件字节、九字段指纹、两类ACL、普通xattr与现有子项保持；两文件各2,097,159字节，不作首尾抽样。原捕获SQL/Root/HDD逐次保持，所选旧基本ACL与新HDD保存的当前具名ACL分列，来源全文/MAC/计划/执行及不同history digest核对。共2份旧捕获+2份新当前ACL原件HDD、4意图/8Job，8条恢复专属来源hash审计（每场景4），不存ACL正文或凭据。目标恢复ACL未写入不等于没有当前原件备份。

4张本批真实PNG为两张进行中及两张失权页；原页面运行状态完整入视口、取消按钮在视口内，重载后原任务区域消失、sessionStorage引用为null、没有横向溢出。人工复看320暗色403页，权限反馈可读。无模拟接口、Cookie注入、浏览器trace或凭据截图；临时密码/服务令牌只留进程内存/子进程环境，不写仓库、日志或命令参数。

## 独立复核与收尾

`verify_container_acl_restore_faults.py <本批绝对证据目录>`退出0、aclRestoreFaultIndependent=passed，独立读取永久SQL/Root/受信HDD，核对来源/当前原件分列、准确原主体变更/未过期快照、停止时长、正确恢复路径403、失权页面/引用清除与精确清理。两个临时身份、容器及源树均已不存在。物理字节由主验每次操作前后读取，独立工具不声称在清理后重读已删除源（sourceBytesRereadAfterCleanup=false）。

新观察器还只读复核前批7990b84/schema7取消/退出证据，正常通过，原81份SHA清单文件逐字保持；没有重放旧恢复、再次修改身份或生产写入。新批与旧批的原件分别保存，不覆盖旧验收结论。

最终11核心healthy，8数据库平台及API/Gateway的ID/镜像/StartedAt保持；Worker仅同ID/镜像正常停启。Agent1214295/维护1214273与cad6182来源保持，发布账本不变，7份受保护配置/密钥/sudo规则的SHA/uid/gid/mode保持，活动任务/终端/锁/发布屏障/未完成意图全0。user NOPASSWD按授权保留，Agent最小授权未扩。私有原件、审计、归档SHA及权限检查不入Git。

本批只签**单文件access历史恢复、当前原件已备份而目标尚未写入**时的两种实时撤权。仍需Worker强杀/Agent重启与原自然租约、写后中断/撤权、Unknown/实例变化、来源丢失/替换、磁盘异常与共享写入组合；目录default故障、递归ACL/恢复、可恢复删除/长期清理及其余目标继续。源与HDD同device不算异盘灾备，59主项完整签收和25%–35%粗估不变，17项移出不算完成，master未交付。

收尾8项范围检查、847条变动MD相对链接通过；目标第1节逐字保持，前批取消/退出QA原文保留后追加接续。项目图谱已确认原`whale-deck/container-acl`的`whale-deck.container-acl.e77c1a6.restore-revocation`事件，阶段仍active，按原eventId从pending移入synced。
