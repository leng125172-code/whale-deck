# 递归ACL恢复预检退出、重新登录隔离与自然到期

2026-11-05，dev；承接[常规恢复](CONTAINER_RECURSIVE_ACL_RESTORE_REAL_UI.md)与[真实跨页](CONTAINER_RECURSIVE_ACL_RESTORE_PAGING.md)。产品保持eeb24dd，不扩大Agent权限、不发布产品、不修改SQL或时钟，不把本分项算为完整59项。

## 执行前事实与候选（原时点）

源码明确：未确认Ready无执行Job、无发布屏障/RootStarted，Worker只在原ReadyExpiresAtUtc或显式取消时重新领取。BFF/Authentik正常退出清除真实会话和前端opaque引用，不直接改Ready行。Repository.OwnedAsync同时绑定Subject和LoginBinding，因此同账号新登录不能接管原ID/key/确认。原五分钟预检自然到期后FinishPlanningAsync封存Expired、PLAN_EXPIRED，原成功规划Job保持Succeeded，不伪造Canceled。

新增专属ready-logout入口，复用已验26对象来源捕获、安装字节/已发布探针SHA256及完整文件/Root/HDD保护；分页旧门禁默认不改变。新身份真实Vue选择全新来源、登记/Ready、原引用重载、只读首25项来源预览，然后正常双端退出，原上下文6GET/确认POST应401，引用清除。正常重新登录相同Subject后原ID/key应404、原计划确认应404，页面没有旧任务入口；原SQL仍Ready且完整未变。

父级不取消、不确认、不改期限，按原ReadyExpiresAtUtc自然等待；有界私有观察逐次验证执行/回执/恢复历史缺席及不可变字段，周期重开完整26对象与原历史。最后Expired且CompletedAtUtc不早于原期限，规划成功事实保留、3条精确规划审计（Accepted/Ready/Expired）。新登录管理员只读恢复查询原过期事实后正常退出，再普通/匿名拒绝及正常退出。清理前另进程实读仍存在的源文件，之后才精确清理；清理后只重读保留证据，不称再次实读已删除源字节。

新增11项专项及关联10分页/10业务/10历史、8范围检查通过，均零跳过；Python/Node/YAML和diff检查通过。反例覆盖提前到期/伪取消、任何执行/回执/历史/屏障、原命令/来源替换、缺失重复审计、成功Job篡改、时序/布尔计数、证据路径及信号逃逸。候选复核中修正容器drawer必须使用含名称的标题匹配，并明确新旧登录绑定区别；没有因这些离线修订重发真实请求。确认token/CSRF只驻留浏览器进程内存与请求，Ready归档confirmation=null，不写入Git/证据/日志。

候选冻结时尚未运行真实新门禁；实机结果见下节，以上代码检查及合成投影本身不能冒充实机。此分项不替代执行中退出/撤权/重启/Partial/Unknown/来源异常、26对象恢复写入、删除/长期清理、维护连续性和其余59项，17项移出不算完成。


## 实际签收

门禁冻结 `91fa69360cebca63b9ac48c2f564e05fc1de72e1`，97份验证源码逐Git/工作区核对。复用原已发布d15b698捕获探针及完整目录SHA256，不新建探针发布或更换产品。唯一QA unit `whaledeck-qa-restore-ready-logout-91fa693`，Invocation `ff9e9103741c4c4e93e248831f3178c9`，实际Result=success、SubState=exited、ExecMainCode=1（CLD_EXITED）、ExecMainStatus=0。准确状态/journal归档后仅停止已退出QA unit，产品宿主/Worker均未重启。

一份全新自有容器/目录、26对象签名来源捕获Completed，54份Root/HDD聚合/子项原件完整核验。普通业务仍54意图/49执行/103Job；本批来源由RPC捕获，不冒称由真人业务页面/Worker新增一次来源写入。新真人身份正常Authentik登录，经Vue选源、登记Queued/Ready、原引用重载和首25条恢复目标ACL预览；只读页offset0，不重签本批真实跨页，上一批跨页原证据保持。

### 退出及新登录隔离

真实管理员在原上下文通过用户菜单正常BFF/Authentik双端退出，观察顶层logout POST302/303及provider end-session导航，BFF me401/provider me401或403且实际登录表单按钮可见。原ID/key、恢复查询、历史等6GET全部401，旧计划确认HTTP POST401；前端opaque引用清除，没有页面确认、取消或上传。

同一浏览器正常重新登录相同Subject，新的登录绑定不能接管原任务：原ID与原key均404，携原确认token/版本的确认POST404；页面旧任务入口0、引用仍空。确认token/CSRF仅在Node内存/实际请求中使用，未写入JSON/Git/日志；归档Ready的confirmation=null。重新登录后的真实SQL与原Ready投影**逐字节一致**，不是退出导致取消或新登录续领。三个拒绝确认HTTP请求与页面计数分开，不记为有效确认或执行。

### 自然期限及持久化

不点击取消、不编辑任何SQL/时钟/回执，按原五分钟ReadyExpiresAtUtc等待。退出/重登后的有界观察共**112次**，持续**266108ms**：此前均Ready，executionJobId=null、confirmed0、RootStarted空、发布屏障false；周期重开26对象全字节/九字段/access/default/xattr和全部原Root/HDD，保持来源捕获后的真实状态。最终Worker在原期限后**1140ms**自然封存Expired/CONTAINER_RECURSIVE_ACL_RESTORE_PLAN_EXPIRED；这是本批实测延迟，不推断瞬时撤销或更广SLO。

原命令、登录绑定、请求/计划MAC、来源摘要/清单/前像绑定与全部非状态字段保持。原规划Job完整投影仍Succeeded；执行Job/outbox确认/RootStarted/receipt/history字段缺席，确定性执行ID的恢复Root/HDD/子目录无记录。3条实际规划审计精确对应plan-accepted Accepted、plan-result Ready、plan-result Expired，原主体/容器/Job/脱敏详情绑定一致，没有confirmed/result/cancel-requested。4条历史读取审计（2库存扫描+选源+首25项预览）与原HTTP投影对应。

新登录管理员通过实际只读恢复API看到原Expired、无确认/回执，随后正常双端退出。普通身份6GET均403、原计划确认POST403、私有页面请求0/历史及恢复审计0；普通再正常双端退出。原管理员退出、新登录管理员退出及普通退出后相同6GET均401，共3次正常双端退出，不能把账号清理当退出证据。页面登记1/确认0/取消0/上传0；本批不签执行中退出。

### 独立核验及保留

清理前另进程实际重新读取仍存在的26对象全字节和属性、原Root/HDD、真实SQL/112观察/审计/三正常退出及身份缺席，physicalTreeReread=true；父级再次验证后才逐项移除自有源和容器。清理后独立进程退出0，physicalTreeReread=false、sourceBytesRereadAfterCleanup=false、sourceAndContainerAbsent=true。5张320×600暗色真实PNG保存，人工查看原退出登录表单及新登录后无旧任务的容器文件页，临时查看副本准确清理。

旧1628保护文件及全部源/恢复历史九字段/字节保持，普通保护总1682（本批仅新增54来源文件）。来源Root1300；恢复Root82/HDD76全量与批前保持、目录Root94保持。生产恢复现8意图/5执行/活动0，仅新增原Ready自然Expired。11核心ID/镜像/StartedAt/healthy及两宿主PID1379812/1379807和实际eeb24dd映射保持，无产品发布/回滚。源/HDD仍同device2049，不算异盘恢复，旧维护连续性失败不覆盖。

私有证据 `deploy/artifacts/recursive-acl-restore-ready-logout-10a1c9ec9f2d4317ad2f3261df1b7120/` 保存真实RPC/HTTP、原Ready与退出后相同SQL、Expired SQL、自然观察、54原始记录、五PNG、保护基线和独立前后/准确unit回执。87文件SHA256清单逐项验证，清单SHA256 `f63730becfaa51cd0bc441df1606311420ed9698f0982c426ce23e30688dcccb`。不改写前批已封存证据。

## 后续边界

本批新增“Ready退出/同Subject新登录隔离/自然到期”实机分项，不是执行中取消或退出。后续优先专属RESTORE首子项前退出/取消：必须按原执行ID/FD/pidfd/systemd单元准确绑定，证明完整当前ACL前像HDD已留存而首项未写；不能套用仅Completed或无历史Aborted的旧证明。之后继续实时撤权、Worker/Agent中断、Partial/Unknown/来源异常/跨目录保护、大目录恢复写入、可恢复删除/长期清理、维护连续性及其余59项；不提高整项完成率，17项移出不算完成。


收尾：最终8项范围复跑通过；本轮修改MD的731个本地链接存在、缺失0，原跨页QA正文按Git原字节前缀保留，diff检查通过，主开发Git索引1000:1000/664。91fa693工具候选已普通推送成功，本批未验证新工具/文档CI终态，不称CI全绿；运行产品eeb24dd保持原已验发布。

项目图谱事件 `whale-deck.container-acl.91fa693.recursive-restore-ready-logout` 取得project/key/event匹配且duplicate=false成功回执，pending移入synced，container-acl保持active。
