# 独立恢复Unknown下共享宿主bind的跨容器目录延期与释放

2026-11-06。工具冻结 `6098176f1e0b33533286fb0e53292533381d7afd`，生产保持093f44a9。全新Unknown恢复保留保护时，第二个不同CID实际绑定同一宿主测试目录，目录意图自然延期4次；不同管理员唯一ACK后，同一个目录意图才开始并成功。清理前后独立核验通过。只签本组合，不代表其他跨写入或长期清理已完成。

## 两个CID确实共享目录

主fixture生成唯一自有WDV目录和非root/只读根/无网络的固定预算容器。别名容器使用另一个UUID名称及CID，但其唯一/work可写bind的Source精确等于主fixture目录；前后frame验证Image、Mounts、HostConfig相同，User1000:1000、validation label、CapDrop ALL/no-new-privileges及资源预算逐个检查。宿主目录dev/inode另存并独立复核两个实际当前容器。

旧回收跨目录工具的两个容器绑定不同子目录，不能据其标题推断共享bind；本轮新增别名容器上下文，不使用旧独立目录假设。别名只拥有其容器，不拥有、不会删除共享目录。主fixture保留目录清理所有权；任一未核实状态都保留主fixture和别名。

## Unknown与自然目录延期

真实临时身份可见Vue完成来源回收和独立恢复。沿用已验证的Applying/returned三FD/当前HDD切点，只隔离本轮claim索引，受控继续原helper；产品重建围栏并给Unknown/null归属。原pidfd约2.504秒结束，计时从索引移动前开始且包含受控继续。原件、来源Root及HDD保持，不编辑SQL/Root JSON、不强制改写已有成功；原上下文正常双端退出。

不同真实管理员在别名容器文件页生成目录计划并确认，路径work/文件恢复保护后的目录、模式0750。实际计划/登记各1次，Origin/幂等键校验成立；固定意图ID、actor/login binding、计划/请求/幂等键及原实例不变。5个专项和77项关联Linux root检查零跳过通过，Python/Node语法、9项CI合同/8项范围检查通过；离线fixture与下列真实链条分列。

目录版本2/4/6/8的四个不同自然延期样本通过：Pending/Queued、无root_started_at、租约已清、未来next_attempt；相邻due/观测间隔至少5秒，后一次观测不早于上次due，所有样本早于ACK完成时间。没有改数据库时钟、next_attempt或调度器，没有取消/重建/重登记来凑次数。每次重读Unknown完整SQL、Root原字节、claim/HDD和返回原件，保护保持，目录目标和终态Root JSON回执均缺席。只读Inspect/能力检查可以发生，不称“没有进入任何Agent RPC”。

源码复核确认当前ClaimAsync排除Unknown，故其完整SQL应保持。本轮同时加强共享stable_unknown和跨写入证明；上轮已封存193文件中的初始/暂缓SQL也实际逐字段完全相同。原说明中“可能自然推进Unknown版本/租约”已据实纠正；本轮自然推进的是等待目录任务。

## 唯一确认与原任务释放

观察者先暂不处置，ACK0，已接受的管理写入仅目录计划/登记2次；Unknown保护和原事实保持。四次延期证明完成后才放行一次明确ACK，意图Acknowledged/保护释放，Root仍Unknown、归属仍null，唯一异主体审计；不把处置称为恢复成功。

等待目录的同一ID在ACK完成约3.448秒后首次进入Root并Succeeded。Root指纹/原计划/父目录版本、实际新目录inode、1000:1000/0750、空目录、publishedVersion及持久化/审计/投递全部核对；没有替换意图、计划或幂等键。真实页面重新打开原任务，显示目录已创建，实际列表可见，私有GET带no-store；正常BFF/OIDC退出后匿名目录GET401。原Unknown/ACK任务页、不同管理员owned-ID404、普通9/匿名5拒绝、取消/重试0和两次正常双端退出均通过。恢复观察者共3次业务写入：目录计划、目录登记、ACK；原所有者另有4次回收/恢复预检确认。

## 独立清理与证据

独立live进程复核共享目录inode、两个当前容器frame、四次延期、ACK边界、原目录意图/Root回执/真实产物及Unknown/null事实。通过后先只删除已验证空目录及其私有目录，再依原证明移除旧claim隔离副本和测试原件/空私有目录；产品重建claim、Unknown Root及HDD保留。别名finally只移除别名容器，主fixture最终才验证种子并移除主容器/共享目录；未递归删除未知内容。final读取保留的HDD/历史和原live证明，不称读取已删除的源。

unit `whaledeck-qa-restore-cross-dir-b723f6b3` / Invocation `803b6f2873a34806a7b39b9f44b607cf`，exited/Result success/ExecMainCode1/ExecMainStatus0，实机首次总门禁通过。私有证据 `deploy/artifacts/container-recycle-restore-cross-directory-4281fef385dc4590a52d39f1792aecc7` 冻结147份实际源码闭包，共217文件清单摘要 `c8ff2a659d1292c6f7ed66c5417187ff09ef26b1d491af4e10b160ee1e3b9eac`，逐文件复核通过；父目录0700、文件root:root/0600，凭据不入Git、日志或项目图谱。

最终36回收/22恢复意图、活动0；回收Root72/HDD75、恢复Root59、目录Root98，原业务完整行/旧目录历史及其他Root/HDD保持。2346保护文件、11核心服务、093f44a9两宿主/PID保持（Agent2974845、Maintenance2974836）。源/HDD仍同设备2049，不称独立物理盘灾备。接下来推进其余适用故障/跨写入、长期清理及59项；17项移出不算完成，整项0/59、阶段0/6、酒馆/供应商与Live2D功能0%保持，master完整交付尚未完成。
