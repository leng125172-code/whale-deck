# 递归ACL Worker强杀新资源完整复验与Partial观察器候选

后续接续：8e6e472已将观察器接入[真实Partial取消及不同管理员处置整链](CONTAINER_RECURSIVE_ACL_PARTIAL_REAL_UI.md)。下文85c2228仅离线候选是当时事实，不改写旧证据或把新场景并入旧Worker运行。

2026-11-04，承接[原进程中断证据及记录器失败](CONTAINER_RECURSIVE_ACL_RESTARTS.md)。**使用已修记录器和全新隔离资源，Worker写前强杀/原租约恢复门禁及独立核验通过，当前缺失的结构化证据已在新场景补齐。** 原c78批次总门禁依然失败，原任务、旧文件和缺失回执不改写、不重放。部分写入观察器另列为仅离线候选，不当作Partial真实验收。

## 新Worker场景的实际来源与控制

产品仍 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent 1.53，无覆盖发布、迁移、Agent/维护宿主重启或回滚。工具冻结 `020e25f3323b3d1b1b97783b00633f46e435411d`，包含00ec3f7记录器修复；运行前完整Python/MJS闭包及安装helper原字节核对通过。

唯一unit `wd-qa-recursive-acl-restarts-020e25f.service`，Invocation `4c079324438a42e2a72900ff163b22b5`，14:53:12–14:55:37 CST；准确active/exited、MainPID0、Result success、ExecMainCode1/ExecMainStatus0。退出与Invocation日志归档后，只停止这个已完成QA unit。

使用新的非root/无网络/只读根容器、四对象树与临时管理员，正常Authentik可见登录→Vue独立文件/目录ACL目标→预检→确认。仅暂时停启原Worker来捕捉准确root FD/操作锁FD、固定transport/cgroup与pidfd绑定的写前窗口；Root Applying/HDD已验证，cut时startedThrough=finishedThrough=-1，整树仍为原状。10类写入/屏障/租约、其他Job/终端/宿主锁及transient unit门禁全零，QA豁免要求自身PID/cgroup/Invocation一致。

然后只对固定、原ID/原镜像的Worker发SIGKILL并启动同容器。新结构化回执保留：重启前StartedAt `2026-11-04T06:53:41.638653462Z`、重启后 `2026-11-04T06:54:04.921848847Z`，image/ID保持；准确inspect与service-restart原件落盘。绑定helper由产品在0.111秒内结束，退出耗时独立原件已提前保存，验证器没有杀helper。

## 原租约、永久结果、页面及独立核验

原租约到期时间保持 `2026-11-04T14:55:31.867809+08:00`。实际自然等待86.457秒、41次样本；SQL保持原执行ID/原Started、命令/计划/MAC/登录绑定、原lease token/期限与写入屏障，Root已Aborted的原字节SHA和整个源树持续不变。时钟和SQL不修改，到期前不重领或续做原操作。

原意图/执行Job最终为Failed / `CONTAINER_RECURSIVE_ACL_INTERRUPTED`，Root Aborted，五计数0完成/0修改/0中止/4未开始/0待证明，取消时间为空。保存1份四对象聚合HDD及Root，child Root/HDD为0；没有新增执行ID、Apply重放或回滚。全树字节、九字段、普通属性及两类ACL保持。

真实原Vue页面显示“递归ACL修改未完成”，原任务GET200/no-store；两张1440×600亮色PNG通过尺寸/视口检查，终态图片已查看。原上下文正常用户菜单双端退出，POST302/303、OIDC end-session、BFF401、身份端401/403及最终登录页通过，随后原任务匿名GET401；登记/确认各1次，取消POST/内容上传0。

独立只读核验重新读取永久SQL/Job/outbox、Root、受信HDD、全套结构化Worker身份、退出耗时、原租约样本、源码闭包和安装来源，全部通过。两宿主PID/实际来源保持Agent3795144/维护3598736及b880208。临时身份、准确自有源及容器已清理；独立核验使用父过程删除前完整物理快照，不声称删除后重读已删除源。

私有证据：`deploy/artifacts/container-recursive-acl-faults-42bf6b2536174698a911dcce889166ae/`，包含scope、baseline、cut/FD绑定、重启前后SQL与inspect、service-restart、product-helper-exit、natural-lease、最终SQL/Root/HDD/物理、浏览器/退出/图片、独立核验、postflight及unit退出/清理。凭据仅经stdin，不进参数、日志、Git或证据。

旧493个受保护文件全部保持SHA和九字段（7配置/权限文件、账本、145普通Root、83递归Root、153普通HDD、104递归HDD）。当前递归Root84/HDD105，只新增本次聚合各1，普通记录数不变；17意图为8 Succeeded、5 Canceled、1无执行Expired、3预期中断Failed，执行15/相关Job32、活动0。11核心healthy，只有同Worker StartedAt改变；NOPASSWD按授权保留，Agent授权不扩。

## Partial观察器候选及未验边界

并行完成候选 `85c2228`：新增独立QA观察器，无CLI/安装hook，固定同一自有树、Root命名空间/操作锁/双FD/pidfd，只接受Root仍Applying、HDD已验证、startedThrough=finishedThrough=0。要求首项 `sub/中文.bin` 的child为Completed、真实ACL确认/原件历史及版本匹配，实际inode/UID/GID/size/mtime保持、ctime推进、POSIX模式/完整access目标正确、default及普通属性保持，其他三对象完全原样。错过窗口失败并恢复观察进程，不注入产品checkpoint或发送任意PID信号。

该候选只有5项离线证明测试，覆盖真实形状的首项结果、未完成/晚到/未验证历史拒绝、ACL/default/owner/inode/其他对象变化拒绝、子记录计划/版本绑定和任意host路径拒绝。fixture不是实际文件/HDD证据；尚未运行这个新cut，也未接Partial取消/当前管理员处置、持续保护与最终SQL/HDD/真实页面整链，不能计为Partial已签收。

本轮原97项递归工具/8范围检查通过；加入上述5项后102项递归工具及8范围检查、Node语法与diff空白检查通过。没有产品C#/Vue/协议改动，旧产品测试不报告成本轮重跑的全量；工具CI终态另核对。源/HDD仍同device，不签物理异盘、断电/冷启动或维护瞬时零中断。

下一步接入部分写入后的取消、不同管理员明确处置与保护释放，继续写后退出/撤权/重启、Unknown/跨写入/路径磁盘依赖等矩阵，再递归ACL历史恢复、可恢复删除/长期清理及其余59项。原失败保持历史事实，当前Worker新门禁已补齐；整体完整签收/阶段计数不增加。
