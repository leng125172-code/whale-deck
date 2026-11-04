# 递归ACL恢复执行中正常双端退出

2026-11-05，dev。承接[执行中首项写前取消](CONTAINER_RECURSIVE_ACL_RESTORE_PREWRITE_REAL_UI.md)，使用全新26对象来源、身份和执行ID；旧执行不重放。产品仍eeb24dd，以下候选准备不代表实机验收通过。

## 产品事实与候选门禁

`ContainerRecursiveAclRestoreDispatcher.Guarded` 每6秒核对原登录绑定、当前管理员和租约，正常退出使原登录失效，须终止原Apply后观察/结算。Aborted且原身份不再Allowed映射Canceled；这与显式页面取消有相同终态，但不得伪造取消请求或审计。

候选复用已验证的来源捕获、120秒边界观察、完整当前实体/来源/frame/manifest及HDD前像核验。只有原pidfd/双FD/Invocation准确且首项未写时才放行正常用户菜单退出；不通过清cookie、直接调用退出接口或模拟身份取得通过。产品须自行结束原helper，QA不SIGKILL，不自动重放或回滚恢复。

退出后原上下文6GET须401、恢复引用清除；父级保存原SQL/Root/HDD结算及零写证据。正常同账号重登须同Subject但新绑定，原ID/key均404且旧任务入口缺席；当前管理员只读恢复回执200，正常任务页打开原执行详情核对同Job/Canceled、取消及通用重试入口缺席，不重新确认或取消。再次正常双端退出，普通用户6GET/2POST403及退出后401继续核验。

SQL严格要求取消请求时间为null、原owner/login/计划/执行ID保持、四条原工作流审计（没有cancel-requested），双Job及五计数/摘要与原件吻合。原明确取消默认模式仍要求非空取消时间和五审计，不能借新模式放宽旧门禁。清理前独立实读完整26对象，清理后独立重开保留原件；失败仍先保留目标并等待原执行结算。

8项新增身份投影/模式/取消原因反例及75关联，共83测试零跳过通过。首次旧UI字符串哨兵未随共享脚本的模式字段更新而失败，修正为分模式字段后整组通过；复核另修正verifier局部logout循环变量遮蔽模式参数。当前工具只读重开上批155封存对应原件及实际SQL，旧明确取消证明完整通过，没有写入封存或重发操作。语法、CI YAML及diff通过；模拟身份投影不是实际登录，临时ACL/HDD与pidfd测试也不替代此新门禁。

仍待冻结候选并运行全新实机整链；执行中撤权/停用、重启、写后/Partial/Unknown/来源异常、可恢复删除和长期清理、维护连续性及其余59项继续。源/HDD同device和旧维护失败限制保留，17项移出不算完成。


## 479ffa9首批实机：原退出分项取得，总门禁失败

冻结479ffa9，105份验证源码；唯一unit whaledeck-qa-restore-logout-479ffa9，Invocation 7943fc2259c14b54b50db663f6c5f0ce，最终Result=exit-code/ExecMainStatus=1。全新来源/正常Authentik与Vue/真实确认后，观察器25.793秒取得Applying首项前边界，准确pidfd/双FD/Invocation、完整当前树及HDD前像验证通过。

实际用户菜单正常退出：BFF提交302/303、实际Authentik end-session导航、两端会话失效和最终登录页逐步回执保留。产品5.359113820944913秒结束原helper，原SQL Canceled/Root Aborted，五计数0/0/1/25/0，取消请求时间null、无取消审计、四条原工作流审计、租约与发布保护释放；没有发送页面取消或另一次恢复。原登录退出与任务停止这一分项是真实事实，不能等同整个新门禁通过。

后续同账号重登后进入原任务详情，检查器对文字Canceled要求唯一匹配，但实际状态标签与阶段单元格都含Canceled；页面截图保留同原Job ID及Canceled。浏览器在original-job-terminal-ui阶段Error退出，未完成final-auth标记、该新登录正常退出与普通用户检查；没有browser.json/verified.json。不能依据截图或后置读取补签这些未完成步骤。真实新登录旧ID/key隔离的执行语句在此之前，但没有保存完整独立回执，亦不把不完整证据当新整链签收。

失败清理按修复流程执行：只读排空117ms取得原SQL Canceled、Root Aborted和锁空闲后才清理临时身份与容器；自有源目录按未验证规则保留。后续fb5b641固定此失败目录的核对工具无Apply/ACK/SQL更新/身份写入：重读完整26对象与54份来源原件、聚合HDD/首项Aborted原件，核对原owner/login/计划/执行/四审计及零写回执，再准确清理源树与original.bin。清理后独立只读进程重开57份保留原件，SQL保持、旧保护保持；不宣称再次读已清理源字节。

恢复累计12意图/9执行/活动0；普通业务仍54/49/103。旧1844保护字节九字段保持/总1898，来源Root1516、恢复Root192/HDD131、目录94。11核心healthy、原ID/镜像保持，只有同Worker按门禁重启，两宿主eeb24dd/1379812与1379807保持；其他活动Job/终端/宿主锁/上传目录保护均0。源/HDD同device限制及旧维护连续性失败保留。

私有证据deploy/artifacts/recursive-acl-restore-inflight-logout-049b18f87fcf49a7b9c01baf33313781，共157文件逐项SHA256通过，清单摘要a572d49779a9fb21e9aa3d9a3c25ed373532b11905f091577736785297185876。准确失败unit回执和Invocation日志先归档，再停止QA unit；原三批248/243/155封存逐项保持，无重放或回滚。

## 选择器修复及剩余边界

fb5b641将原Job终态检查限定到.el-tag状态标签；显式DOM夹具实际复现两个Canceled匹配，并验证新选择器唯一可见。这是离线浏览器夹具，不是修复后真实页面签收。新增固定失败目录范围测试，全部9专项及75关联共84测试零跳过通过；Python/Node/CI语法检查通过。

新选择器/完整退出门禁仍需全新资源复验，不能重放479ffa9。完整正常退出、实时撤权/停用、Worker/Agent重启、写后/Partial/Unknown/来源异常、可恢复删除/长期清理、维护连续性及其余59项继续，不提升整项完成率。


收尾：84测试通过后元数据再经8范围检查通过；10份Python AST、两份Node语法、CI YAML与diff检查通过，变更MD的736个本地链接缺失0，Git索引1000:1000/664。项目图谱事件whale-deck.container-acl.479ffa9.restore-logout-failure-reconcile回执project/key/event匹配、duplicate=false，pending移入synced，保持active。新候选CI终态尚未取得，不称全绿。
