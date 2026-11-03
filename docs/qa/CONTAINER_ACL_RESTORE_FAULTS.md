# ACL历史恢复进行中取消与正常退出实机验收

2026-11-03。产品继续运行`cad61828b4be064bc0a5e868ecbbdaf9ee8883bd`/协议1.52；本批没有宿主安装、生产迁移、全栈发布、Agent重启、主机重启或回滚。仅原Worker容器按空闲预检正常停启，以准确捕获实际恢复helper。此前正常恢复六场景见[原验收](CONTAINER_ACL_RESTORE_REAL_UI.md)，本批不重放其操作。

## 工具与证据边界

新增`validate_container_acl_restore_faults.py`、`verify_container_acl_restore_faults.py`及来源/页面证明模块，显式schema7/restore模式复用原故障控制；旧普通ACL/schema1–6配置和失败清理保留。仅允许cancel/logout，不把其他场景自动纳入签收。40份工具源码逐字与SHA冻结，安装helper逐字匹配cad6182；没有注入Cookie、拦截HTTP响应、修改SQL/Root/时钟或添加产品测试钩子。

先以`bdcfdef`运行：两场景真实物理/永久业务和独立复核均通过，停止helper为5.419/5.567秒。复看3张截图发现长来源详情把运行/终态文字挤出截图；动作按钮可见，但该轮未断言状态在视口内，因此**不以这轮补签状态可见性**。原件保留在`deploy/artifacts/container-acl-faults-7a245dd6cf084ea29efd9690a9365e0a/`，包括原回执和单独边界说明。

`7990b84580dba40984770d64f900ea71f68cc6b7`只加强截图前状态滚动/完整视口断言及CI语法门禁，未改产品。随后两套**全新**自有资源完成严格复验，不重做原UUID操作；证据目录`deploy/artifacts/container-acl-faults-2074ed4462ff4b8baa51fa1d452044d1/`。本机10来源防混用、40原故障边界、10恢复证明与8范围检查通过，无默认跳过；JS/Python语法通过。

准确7990b84的[CI run41633354049](https://github.com/leng125172-code/whale-deck/actions/runs/41633354049)已观察到backend、installer、native-postgres、native-valkey四路completed/success，包含新增证明和语法检查；frontend的lint/format/unit/build及任务结果组已通过，ACL/恢复组及后续仍在运行，不签整条CI终态。产品cad6182的五路成功属于此前发布证据，不能替代新工具提交的CI事实。

## 严格轮实际操作

每个场景先正常可见Authentik登录，通过Vue执行一次新的普通ACL修改：将基本640 ACL改为具名UID2201/mask4，留下自己的旧基本ACL捕获。主验完整检查两个2,097,159字节文件、inode/uid/gid/mtime/ctime与普通xattr、另一类ACL/子项，证明所选历史确实来自该次Root/HDD；再通过历史页面读回来源并生成新的恢复预检。准备捕获确实修改过ACL，不能把本批全部操作描述为“物理零修改”。

恢复目标由旧基本ACL派生，恢复前当前ACL为具名ACL，两者明确不同/noChange=false；不是预检前取消。实际Vue确认后，SQL进入Applying，Root为Preparing且HistoryVerified=true，**当前具名ACL已完成HDD备份、目标基本ACL尚未写入**。观察器仅绑定本次UUID/owner/完整plan、准确容器/实例/源inode、锁及两个内核FD的原helper，以pidfd保留该切点；由产品处理用户动作并结束helper，验收器没有杀死helper或改写回执。

| 场景 | 页面动作与实际结果 |
| --- | --- |
| cancel（320×600暗色） | 先“继续等待”无取消POST，再明确确认一次取消；产品5.434秒结束原helper，原意图/执行Job Canceled，Root Aborted，aclConfirmed=false |
| logout（1440×600亮色） | 关闭原详情，从原上下文正常页面双端退出；取消POST=0，产品5.575秒结束原helper，原意图/执行Job Canceled，Root Aborted，aclConfirmed=false |

两场景均有真实Vue来源选择/登记/明确确认，恢复各1登记/1确认，上传正文PUT=0。取消场景原页面再查询终态并正常双端退出；退出场景保留BFF=401、Authentik=401/403和Provider导航回执，不用后造上下文替代原登录退出。严格3张真实PNG含运行状态/取消终态及动作区；新增滚动与完整视口断言通过，人工复看320暗色进行中截图，状态和取消按钮均可见。

每场景恢复切点前后完整源清单保持：文件字节/九字段指纹、两类ACL、普通属性和现有子项不变；原Root/HDD捕获逐次保持，恢复执行的新UUID/当前ACL原件与所选旧ACL分别校验，来源全文、计划/执行绑定与history digest不混用。严格轮共2份所选旧来源+2份新当前原件HDD、4个意图/8个Job；恢复专属审计cancel5/logout4共9条，只存来源hash及受限元数据，不存ACL正文。前一轮另有自己的4份HDD，均保留，不把两轮操作合并成一次首轮全通过。

## 独立复核、清理与剩余

严格主验退出0；独立`verify_container_acl_restore_faults.py <严格证据绝对目录>`退出0、`aclRestoreFaultIndependent=passed`。独立只读复核永久SQL/Root/受信HDD、旧来源与新当前前像、40份来源SHA/实际安装字节、切点/计时/原登录退出和准确清理；2个临时身份、容器及源树均已不存在。源物理字节由主验每次操作前后读取，独立工具不声称清理后重读已删除源（sourceBytesRereadAfterCleanup=false）。

最终11核心healthy，8数据库平台和API/Gateway的ID/镜像/StartedAt不变；Worker仅同ID/镜像正常停启。Agent1214295/维护1214273及cad6182同源保持，发布账本不变，7份私密/权限文件SHA/uid/gid/mode不变，活动Job/终端/锁/屏障/未完成意图全部0。user NOPASSWD保留，Agent最小授权不扩大。两轮私有原件及SHA清单不入Git，不保存密码、Cookie或令牌；推送第一次TLS中断后重试成功，不能把连接失败算测试失败。

本批只签文件access恢复在上述“当前原件已备份、写ACL之前”切点的取消和正常退出。仍需恢复进行中实时移组/停用、Worker强杀/Agent重启与自然租约、写后中断、Unknown/实例变化、来源丢失或替换、磁盘异常、共享写入组合；目录default故障、递归ACL/恢复、可恢复删除/长期清理及其余目标继续。源与HDD同device不算异盘灾备，59主项完整签收数和25%–35%粗估不变，17项移出不算完成，master未交付。

收尾8项范围检查及844条变动MD相对链接通过，目标第1节逐字保持、前批正常恢复QA完整原文保留后追加接续。项目图谱已确认事件`whale-deck.container-acl.7990b84.restore-inflight`归属原`whale-deck/container-acl`，阶段仍active；按原eventId从pending移入synced。
