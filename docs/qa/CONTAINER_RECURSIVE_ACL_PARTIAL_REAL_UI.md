# 递归ACL部分写入取消、不同管理员处置及保护释放实机验收

2026-11-04，承接[Worker完整复验及Partial观察器候选](CONTAINER_RECURSIVE_ACL_WORKER_RECHECK.md)。**新隔离资源上真实发生首项access ACL修改，再经原页面取消，永久Partial保留保护；另一管理员先暂不处置，再明确确认Acknowledged并释放保护，完整实机与独立核验通过。** 这不是写前取消，也不把Acknowledged解释为整树修改完成或ACL已恢复。

## 冻结与实际切点

工具冻结 `8e6e472`，运行前核对完整Python/MJS静态闭包及实际安装helper原字节。产品保持 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent 1.53，没有发布、迁移、Agent/维护重启、回滚或授权扩展。创建一套新非root/无网络/只读根容器及自有四对象树、两个新管理员；凭据仅stdin，不进命令参数、日志或Git。

原管理员真实Authentik→Vue填写独立文件/目录access目标→预检→确认。仅停启同Worker以安排观察窗口；固定transport/root UID/cgroup、容器root FD及原操作锁FD共同绑定pidfd。观察器仅允许首项已完成、其他三对象完整原样的窗口，stop后再核对：`sub/中文.bin` 的child Completed、ACL确认、HDD原件证明、inode/UID/GID/size/mtime/版本匹配，模式与完整access目标正确、ctime推进，default/普通属性保持，Root与其他对象九字段/ACL完全未变。

为避免把“第二项开始登记但尚未写”误当首项前或整树成功，候选现允许finishedThrough=0且startedThrough为0或1，仍要求后三对象物理完全原样；这是比原85c2228候选增加的明确边界，第二项已实际变化即拒绝。本次真实cut为startedThrough=finishedThrough=0，仅首项发生修改。没有安装测试hook、修改时钟/SQL/Root回执或任意PID输入。

## 真实取消与Partial事实

320×600暗色原页面先“继续等待”（取消POST0），随后明确请求取消。产品在5.020秒内结束准确helper，验证器只SIGSTOP/SIGCONT，没有杀helper。终态保留实际进展：Root Partial，SQL Partial且publish_barrier=true、completed为空、lease双字段为空；执行Job仍Running、等待明确处置，原outbox仍待处理，不能伪装Canceled或已完成。

五计数为 **1已完成 / 1实际修改 / 1中止 / 2未开始 / 0待证明**。首项child Completed，第二项在cut后登记并最终Aborted，另两目录未开始。原Root目录保持原样；首文件只有被明确要求的access ACL、模式/ctime变化，完整内容、inode、所有者、nlink、size、mtime、default及普通属性保持；其余三对象完整原样。不推断第二项具体内部调度路径，不把原cut改写成startedThrough=1。

永久保留1聚合HDD清单、1已完成子项HDD、3个Root记录（聚合、已完成、已中止）。中止项没有子HDD历史，清单仍覆盖全部四项；每个child ID、源计划/指纹、原两类ACL、目标、实际版本、outcome/history/manifest摘要及计数与SQL/回执交叉一致。源文件内容不进ACL元数据。

原Vue显示“部分修改，保护已保留”，无再次确认入口；原GET200/no-store，全部五计数在小屏视口实际核对并截图。原上下文正常用户菜单双端退出，随后原任务匿名GET401。登记/确认/取消各1次，内容上传0，没有自动续做或回滚。

## 不同管理员明确处置

第二个独立Authentik登录从`/files`进入递归ACL异常处置；访问原owner接口实际404，共享恢复入口显示原ID、Partial及同一五计数。真实点“暂不处置”后ACK POST为0，父过程再读完整SQL/Root/HDD与实际源，仍与原Partial相同、保护保持。

然后通过真实确认按钮仅发送一次acknowledge，202后页面主动GET原处置并核实Acknowledged。SQL只完成这次处置：publish_barrier=false、completed已记、acknowledged_by为第二Subject，执行Job终态Failed/phase Acknowledged、outbox已处理；原执行ID/原Started/命令/计划/绑定、Partial Root、五计数/outcome/history证明及实际文件不变。唯一partial-acknowledged审计属于第二管理员、原执行Job、结果Partial。

Root Discard是对原执行ID的永久fence，已终态Partial的Root及HDD原字节前后完全相同，不再Apply，不恢复或删除目标/历史。Inspect与Discard的Replayed/BindingCurrent/HistoryAvailable属于观察字段，允许按真实回执更新；比较时不把这些字段变化当作修改原计数。acknowledgement_json与新实际回执相同，原Partial回执另行归档，不能假设新旧receipt_hash必然相同。

亮色320×600页面明确显示“已处置…不代表整树修改成功，不会续做或自动恢复ACL，不删除原对象”。第二原上下文也正常双端退出，随后共享恢复GET401。两个临时身份、准确源及容器全部清理；所有Root/HDD保留，没有自动ACK失败清理或隐藏补偿。

## 独立核验与运行记录

独立只读过程重读永久SQL/Job/outbox、Root/受信HDD、源计划/实际五计数/outcome、不同主体ack审计、冻结源码/安装helper、两原上下文logout及5份真实PNG，全部通过。物理字节由父过程在清理前完整核对，独立过程不声称删除后重读已删除源。已实际查看Partial五计数和Acknowledged图片，暗/亮两主题小屏均可读，明确确认按钮在视口内。

唯一unit `wd-qa-recursive-acl-partial-first.service`，Invocation `e21c3f70ec344190aa84c8969ac6f163`，15:17:30–15:18:52 CST，准确active/exited、MainPID0、Result success、ExecMainCode1/ExecMainStatus0。状态、Invocation日志与独立核验归档后，只停止该已完成QA unit。

私有证据：`deploy/artifacts/container-recursive-acl-partial-36251c249aab48178a0704a02e75a77e/`。保存scope/baseline、原登记确认与cut/FD证明、helper退出、Partial与ack两阶段SQL/Root/HDD/物理、两浏览器/退出、暂不处置信号、五张图片、独立核验、postflight及unit退出/清理，不进入Git。

106项相关离线边界、8项范围、Node语法和diff空白检查通过；现有CI wildcard已包含新增4项workflow证明测试。新测试是拒绝释放Partial保护、错误Job终态/零修改、换执行/计划/主体/篡改事实、路径/SQL写入或响应注入等边界，不用fixture替代上述实机。工具CI终态另核对，无产品C#/Vue/协议变更，不把旧产品测试报成本轮重新运行。

旧495文件全部保持字节SHA/九字段，普通Root145/HDD153不变，递归Root84→87、HDD105→107，恰好新增上述3 Root/2 HDD。现18意图：8 Succeeded、5 Canceled、1无执行Expired、3预期中断Failed、1 Acknowledged，执行16/相关Job34、活动0。11核心healthy；两宿主Agent3795144/维护3598736与实际b880208保持，仅同Worker StartedAt因安排cut改变；配置/账本及user NOPASSWD保留，最小Agent授权不扩。

源/HDD仍同device，不签物理异盘或冷启动；本轮不签其他写入的延期/释放链、进行中退出/实时撤权/重启的Partial组合、Unknown或磁盘/路径/依赖故障。下一步补这些矩阵，再递归ACL历史恢复、可恢复删除/长期清理及其余59项；原失败记录保留，整体任务和阶段完整签收计数不升。
