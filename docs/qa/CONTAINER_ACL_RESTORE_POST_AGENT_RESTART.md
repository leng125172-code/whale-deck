# ACL历史恢复写后Agent重启：来源摘要修正与实机验收

## 首轮失败与工具修正（复验前记录）

2026-11-03。产品保持b69302b/1.52，原Agent1688516、维护1688513。工具44a60de的profile6/schema12首次门禁在来源捕获校验阶段退出1、FileNotFoundError：该场景要求jobResultsVersion=1，普通来源捕获的verify_case因而要求`capture/access-jobs.json`，但prepareRestoreFault此前没有读取并保存这两个来源Job回执。浏览器等待父进程放行后超时退出；不是产品恢复写入或Agent重启失败。

原件`deploy/artifacts/container-acl-faults-76e36b9939ac46ac83ac4924383e7b12/`完整保留。只读SQL按准确CID查到仅1个Succeeded普通ACL来源意图、恢复意图0/活动0；两宿主PID/source保持，Agent重启未尝试。容器准确移除，未证明源树按失败策略保留，未生成整批通过回执，不重放已完成来源捕获。

修复保留全部摘要核验条件：仅profile6的来源捕获由真实原会话GET两个Job，先保存source-capture-jobs.json，再发source-captured信号。父进程核对受保护文件/大小/格式/来源ID后保存capture/access-jobs.json，随后共享verify_job_summaries逐字段比较实际SQL与API；缺失、错来源/错Job或错误摘要仍失败。独立verify_restore_binding显式从原capture目录读取摘要，observed目录只承载新观察结果，不能改写原件。旧profile1–5未设置该标记时不要求新增文件；没有删除标记、跳过SQL校验或扩大产品权限。

28项恢复证明、44项Linux-root切点/失败清理范围、10项恢复浏览器证明、8项59项范围，共90通过，Python/JS语法及git diff检查通过。新增回归覆盖缺回执/错来源或执行Job/错误成功事实、旧profile5不读取新文件、独立重验准确原目录。测试首轮新增临时文件清理重复unlink导致1错，已修正；保留失败事实，不称首轮全绿。隔离证明测试使用合成API/SQL输入，不能算实机Agent重启或页面通过。

本节记录修复提交时点，尚未登记新的恢复意图或重启Agent。随后必须冻结修复工具，以全新资源正常Authentik/Vue捕获与恢复、准确已写切点/固定Agent重启/维护入口和双原Job页面、HDD/正常原上下文双端退出及独立只读核验；实机结果在后续章节接续。产品无需为工具补证重发，user NOPASSWD保留，Agent仍最小授权，不自动回滚dev或重启主机。

## 修复后全新资源真实验收通过

工具冻结`96b6bda8caca959f666377bee1d97141c95de0bf`及40份源码，产品保持`b69302b8cb32048038d7dbeed193af205c70fd11`/1.52，无重新发布。主门禁post-acl-agent-restart退出0，原件`deploy/artifacts/container-acl-faults-42a613ddd70d41e4837d5820a397e9e0/`，profile6/schema12。

全新普通UID、无网络、只读根容器和HDD源，通过正常可见Authentik登录→Vue生成本轮普通ACL捕获→真实来源两个Job GET/SQL安全摘要核验→公共8080历史List/Read选择旧ACL→新恢复预检与明确确认→PG/Worker/签名Agent。来源与恢复意图/执行ID不同，旧来源和本次当前ACL前像分别核验；没有复用首轮失败捕获、拦截API、注入Cookie、改SQL/时钟或改Root回执。

在目标基本access ACL已写、当前前像HDD已验证且Root仍Applying的精确切点，逐项确认其它Job/终端/锁/屏障/九类文件写入0及仅本轮Root helper活跃后，只重启固定whaledeck-agent.service。原helper由产品0.010秒结束，验收器未杀helper；Agent PID1688516→1772448、来源仍b69302b，维护PID1688513与来源保持。**Agent完整重启实际30.622秒，不把0.010秒helper结束时间冒充整体重启耗时。**

重启前/中/后152个维护入口样本全部HTTP200、healthy/Ready、no-store，最慢0.0082秒，时间连续性通过。这只证明维护入口，不泛称Agent离线期间全部业务和指标采集连续可用。原恢复ID最终Succeeded/Completed、aclConfirmed=true，新ACL/版本保持，未续写或自动回滚；迟到原Apply只返回Completed/replayed，原Root字节、HDD和现场不改。

两文件各2097159字节、原inode/属主/mtime、普通属性、另一类ACL与现有子项分别核对；目标ACL/ctime按已写结果核对，不笼统说所有源字段不变。1旧来源+1本次当前HDD、4条专属恢复审计及原计划/执行Job、完整canonical来源/授权hash保持。来源捕获的两个Job API回执保存在原capture目录并由独立工具再次与SQL核对，没有跳过首次暴露的摘要要求。

320×600暗色真实Vue完成原恢复wrapper查询、进行中/完成状态与计划/执行两个原Job详情抽屉；安全结果逐字段与API/SQL对应，不显示路径/身份/原始绑定等私密字段，通用取消/重试写按钮不存在，任务页面写请求0。4张真实PNG（inflight、terminal、plan-job、execution-job），手动复看两个任务抽屉：长ID换行、状态与摘要可读、纵向滚动保留且无横向溢出。原上下文正常用户菜单双端退出通过；本场不是撤权退出，取消POST0，历史重读0。

独立`verify_container_acl_restore_faults.py`退出0、independentAclRestorationFaults=true，重新读取永久SQL/Root/attested HDD、来源和任务摘要、恢复审计、维护观测及正常退出，准确确认容器/源/临时身份均不存在。独立工具未在清理后重读已删除源字节，完整物理证明来自主验逐轮读取；未为独立通过重做任何写入或重启。

## 全局保持与剩余范围

只读六组件probe同源b69302b，最终11核心healthy；8数据库平台及API/Gateway保持开始时ID/镜像/StartedAt，Worker只为精确切点正常停启。7份受保护配置/能力密钥/sudo规则SHA/uid/gid/mode与可信发布账本保持，当前Agent1772448/维护1688513；user NOPASSWD保留，Agent最小授权不扩大，所有活动任务/租约/屏障0。

前批128份HDD元数据逐份字节及九字段保持；库存131份准确分为原128+本轮首次失败的普通来源1+本次成功来源/当前前像2。三批旧失败源树仍保留，不记为长期清理已完成。本批失败和成功原件、源码、日志及SHA清单均root私有忽略目录；预检/最终边界在`deploy/artifacts/acl-restore-post-agent-preflight-b227ecc9fbee4991919339cba99bc13f/`。

下一步单独实现并实测历史恢复Unknown/实例或来源变化、不同管理员显式处置和跨容器目录保护；不能套用本场Completed或普通ACL审计冒签。之后推进磁盘/共享写入、递归ACL/恢复、可恢复删除/长期清理及其余59项。源与HDD同device，不算异盘灾备；59主项及六阶段完整签收数不提升，17项移出不算完成，master未交付。

收尾8项范围测试、877条变动MD相对链接通过，目标第1节逐字保持，3份QA原文仅追加接续。项目图谱确认原whale-deck/container-acl的`whale-deck.container-acl.96b6bda.restore-post-agent-real`事件，阶段active，原pending移入synced，没有将本分项计为59主项全部完成。
