# 递归ACL恢复预检退出、重新登录隔离与自然到期

2026-11-05，dev；承接[常规恢复](CONTAINER_RECURSIVE_ACL_RESTORE_REAL_UI.md)与[真实跨页](CONTAINER_RECURSIVE_ACL_RESTORE_PAGING.md)。产品保持eeb24dd，不扩大Agent权限、不发布产品、不修改SQL或时钟，不把本分项算为完整59项。

## 执行前事实与候选

源码明确：未确认Ready无执行Job、无发布屏障/RootStarted，Worker只在原ReadyExpiresAtUtc或显式取消时重新领取。BFF/Authentik正常退出清除真实会话和前端opaque引用，不直接改Ready行。Repository.OwnedAsync同时绑定Subject和LoginBinding，因此同账号新登录不能接管原ID/key/确认。原五分钟预检自然到期后FinishPlanningAsync封存Expired、PLAN_EXPIRED，原成功规划Job保持Succeeded，不伪造Canceled。

新增专属ready-logout入口，复用已验26对象来源捕获、安装字节/已发布探针SHA256及完整文件/Root/HDD保护；分页旧门禁默认不改变。新身份真实Vue选择全新来源、登记/Ready、原引用重载、只读首25项来源预览，然后正常双端退出，原上下文6GET/确认POST应401，引用清除。正常重新登录相同Subject后原ID/key应404、原计划确认应404，页面没有旧任务入口；原SQL仍Ready且完整未变。

父级不取消、不确认、不改期限，按原ReadyExpiresAtUtc自然等待；有界私有观察逐次验证执行/回执/恢复历史缺席及不可变字段，周期重开完整26对象与原历史。最后Expired且CompletedAtUtc不早于原期限，规划成功事实保留、3条精确规划审计（Accepted/Ready/Expired）。新登录管理员只读恢复查询原过期事实后正常退出，再普通/匿名拒绝及正常退出。清理前另进程实读仍存在的源文件，之后才精确清理；清理后只重读保留证据，不称再次实读已删除源字节。

新增11项专项及关联10分页/10业务/10历史、8范围检查通过，均零跳过；Python/Node/YAML和diff检查通过。反例覆盖提前到期/伪取消、任何执行/回执/历史/屏障、原命令/来源替换、缺失重复审计、成功Job篡改、时序/布尔计数、证据路径及信号逃逸。候选复核中修正容器drawer必须使用含名称的标题匹配，并明确新旧登录绑定区别；没有因这些离线修订重发真实请求。确认token/CSRF只驻留浏览器进程内存与请求，Ready归档confirmation=null，不写入Git/证据/日志。

尚未运行真实新门禁；以上代码检查及合成投影不能冒充已实机通过。后续应保存准确Invocation终态、实际SQL/HTTP/五PNG/自然期限观察及独立前后证明。此分项不替代执行中退出/撤权/重启/Partial/Unknown/来源异常、26对象恢复写入、删除/长期清理、维护连续性和其余59项，17项移出不算完成。
