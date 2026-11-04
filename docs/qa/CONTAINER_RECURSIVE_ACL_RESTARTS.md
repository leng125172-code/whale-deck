# 递归ACL写前进程中断、原租约与Agent重启实机证据

2026-11-04，承接[实时移组与停用](CONTAINER_RECURSIVE_ACL_REVOCATION.md)。**Agent重启单项完整实机/独立核验通过；Worker强杀的永久结果、实际源和原租约分项已只读补核，但原双场景总门禁因记录器错误失败，缺失的结构化重启前inspect及精确退出耗时不补造。** 没有重复已发生的Worker强杀，也不把两次分项拼成原单轮全绿。

## 安全门禁与冻结来源

产品保持 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent 1.53，无新发布、迁移或自动回滚。只建立自有非root/无网络/只读根容器、四对象树和临时管理员，真实Authentik→Vue预检/确认，停启同一个Worker来捕捉Root Applying/HDD已验证、首项未开始窗口。凭据仅stdin，不进入参数或日志。

新增门禁覆盖上传、目录、复制、移动、普通权限、所有者、两种旧递归、ACL及递归ACL共10类活动/屏障/租约，并检查其他Job、终端、宿主路径锁；只豁免准确原意图和执行Job。所有相关transient unit必须为空或是准确FD绑定helper及本QA unit；QA豁免还需当前PID、cgroup及Invocation一致。没有任意PID或服务名输入，只能强杀同ID/镜像的固定Worker，或重启固定`whaledeck-agent.service`。维护宿主不重启。

工具冻结b510cfb首次运行；记录器修复b28350b及恢复边界00ec3f7随后冻结。源闭包与实际安装helper逐字节核对；原失败所用源码、信号、SQL/Root/HDD及图片不改写。后续Agent仅用新资源运行`agent-restart`，不会再次执行Worker强杀。

## Worker实际分项、原总失败及只读恢复

原unit `wd-qa-recursive-acl-restarts-b510cfb.service`，Invocation `5cb84e9913304237be3b59381535c551`，准确Result=exit-code、ExecMainCode=1/ExecMainStatus=1。Worker强杀后同容器/镜像恢复；原执行ID/原Started/命令计划/MAC绑定不变，在原租约自然到期前保留Applying和写入屏障。Root在helper退出时已安全Aborted，并非必须保持Applying。

实际自然等待86.610秒、41次样本：原租约token/到期时间、SQL屏障、Root原字节SHA、完整源字节/九字段/两ACL/普通属性持续保持，没有改时钟或SQL。到期后原意图及执行Job为Failed / `CONTAINER_RECURSIVE_ACL_INTERRUPTED`，Root Aborted，实际计数0完成/0修改/0中止/4未开始/0待证明；聚合HDD保留、child历史0。真实原页面显示“递归ACL修改未完成”，正常双端退出及匿名原任务401的原回执已落盘。

**失败发生在产品结果、物理证明和浏览器退出都核对后，写最终工具JSON时。** `verify_terminal`中名为expected的终态字符串被child文件名集合覆盖，导致JSON序列化TypeError。修复改为从已经验证的SQL行生成独立、严格类型的可序列化摘要，child名称另用变量；新增回归覆盖无child和首项Aborted两种计数，并在之后的运行中提前落盘service-restart和helper退出回执。错误属于验收记录器，不改产品行为或删除失败。

00ec3f7恢复工具只允许这个准确失败目录，要求原unit退出1、Agent未开始、源父inode与原计划一致、原完整树及普通文件字节/九字段匹配、永久SQL/Root/HDD/原租约/浏览器原件通过后，才精确清理保留源。没有重建原容器或重发任何Job/强杀/写入。恢复结论明确`originalWholeGatePassed=false`、`originalStructuredServiceRestartReceiptSaved=false`、`exactHelperExitDurationPersisted=false`。原控制流已执行小于15秒的退出guard和重启验证，但具体耗时/重启前inspect没有结构化保存，不能从后续上下文补签；额外只读查询Docker保留事件返回空，也不作为强杀事件新证据。

因此只签已保存的永久/原租约/源/页面事实和精确清理；Worker完整结构化故障门禁仍有证据缺口。原失败unit的状态和Invocation日志归档后准确停止/reset，不影响产品服务。

## Agent重启单项完整通过

新unit `wd-qa-recursive-acl-restarts-00ec3f7.service`，Invocation `1cc4eb470b764d01a7315018c31a2819`，14:45:01–14:46:07 CST；准确active/exited、MainPID0、Result success、ExecMainCode1/ExecMainStatus0。原状态、日志及独立核验归档后，只停止这个已完成QA unit。

新资源经真实预检/确认到写前cut，再通过全部空闲门禁重启Agent。Agent PID3598741→3795144，实际来源仍b880208；维护PID3598736及来源完全不变。helper结束被pidfd实际观察为0.054秒，Agent重启窗口约0.652秒；5次维护样本覆盖开始前/进行中/结束后，均HTTP200、healthy/Ready、no-store。该有限采样不签瞬时零中断，也不覆盖旧维护窗口的连接失败。

原执行ID保持，Failed / INTERRUPTED、Root Aborted，五计数0/0/0/4/0，取消时间为空；没有重放Apply或新执行ID。完整源树字节、九字段、两类ACL及普通属性保持，1聚合HDD/0子历史留存。Vue实际显示失败状态，原GET200/no-store及原上下文正常双端退出、随后匿名GET401通过；两张1440×600亮色真实PNG尺寸核对，终态图片已查看。

独立只读核验重新读取永久SQL/Job/Root/受信HDD、源码闭包/安装来源、重启空闲门禁、原执行绑定和维护窗口/两个宿主身份；全部通过。精确源/容器及临时身份清理完成。独立核验使用删除前的父过程物理快照，不声称删除后重读源字节。此项独立通过不改变先前Worker总门禁的失败状态。

## 测试、保持项与下一步

新增前95项相关检查通过；记录器修正及准确失败恢复限制加入后97项递归探针检查、8项范围检查、Node语法与diff空白检查通过。原选择器拒绝restart的旧测试在扩展合法场景时先失败，已改为验证严格新枚举和拒绝额外参数；不放宽进程/路径/身份边界。此次没有产品C#/Vue/协议变更，不把此前产品测试或fixture浏览器报成本批实机全量；工具CI终态另行核对。

本批开始的489个旧文件，以及Agent开始前的491个文件，均保持字节SHA/九字段。现普通Root145/递归Root83、普通HDD153/递归HDD104：本批总共增加2聚合Root及2聚合HDD、无child历史；配置/权限文件及部署账本保持。生产现16意图：8 Succeeded、5 Canceled、1无执行Expired、2预期进程中断Failed；执行14、相关Job30、活动0。11核心healthy，仅同Worker的StartedAt及上述Agent PID按授权改变，维护进程保持。user NOPASSWD:ALL保留，Agent授权不扩。

私有证据分开保存：`deploy/artifacts/container-recursive-acl-faults-c78f9daecb5c443cb2eb3694474409f3/`为原失败、只读恢复和缺口；`deploy/artifacts/container-recursive-acl-faults-97ad278165964a6c91e7ef599f09ec0a/`为Agent独立完整签收、退出和postflight。所有原件保留、不进Git。

源/HDD为同device，不签物理异盘、断电或冷启动。下一步包括Worker缺失结构化证据边界、写后取消/退出/撤权、Partial/Unknown与跨写入、路径/磁盘/依赖等矩阵，再递归ACL历史恢复、可恢复删除/长期清理及其余59项。完整任务/阶段签收计数不因分项增加。
