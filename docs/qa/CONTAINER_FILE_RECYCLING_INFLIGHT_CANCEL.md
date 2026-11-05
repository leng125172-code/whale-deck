# 容器回收执行中取消：已验证HDD、尚未移除源文件的真实切点

2026-11-05，产品58b8174，最终门禁9e7500f4。承接[精确观察器候选](CONTAINER_FILE_RECYCLING_CUT_CANDIDATE.md)，用全新自有1,048,583字节文件、真实Authentik/Vue确认和取消，首次完整签收该切点。前两次门禁失败保留，不算通过，不重放原Apply。

## 已签实际行为

在真实异步Ready后，父进程按原SQL执行ID观察安装态helper。原Root为Preparing、historyVerified=true，已绑定空私有目录；源路径仍为完整原inode。固定命令/root身份、实际cgroup/MainPID/InvocationID、原操作锁与容器根两个内核FD及pidfd复核通过，才发送SIGSTOP。原件完整大小/SHA/user属性/UID/GID/模式/mtime/ctime与计划一致，不因单个状态字段猜测切点。

父进程在允许UI取消之前并发观察原pidfd。页面真实点击请求取消并明确确认后，原进程1.753703秒内退出；计时包含允许动作后的UI准备时间，没有晚开始等待漏计。产品停止原helper，观察器没有杀进程或编辑SQL/Root/HDD。原任务最终Canceled，唯一原执行Job为Canceled，Root Aborted、两个源事实false（未移入回收区或恢复），当前保护/租约释放，HDD真实完整原件仍已验证、captureState=Canceled。源完整inode/字节/属性保持。

原规划/唯一确认审计、Outbox清零、唯一原执行ID及14字段任务摘要核对通过。实际Jobs页面选中原ID并显示取消安全摘要。管理员正常双端退出；普通用户4GET/4POST403、匿名4GET401和临时身份删除确认通过。

独立进程在清理前重读SQL、原始Root/HDD JSON、完整HDD数据和实际源文件；清理后再次重读永久记录并核对自有容器/文件缺席。仅清理本次已证明原件及空私有目录/自有容器，历史保留。HDD与源为同设备，不签异盘灾备。2346保护文件、11核心和指定旧业务保持；本成功批Root8→10、HDD11→13，前两批原记录/部分捕获不变。

## 两个失败门禁

1. 初版64MiB文件在页面60秒Ready等待期间超时，未确认执行、未到切点。临时身份清理后规划Canceled，无execution/root-start/cancel-request/receipt，源保持；自有资源精确清理，失败仍保留。不是产品取消验收。
2. 第二版16MiB取得执行ID，但25秒观察窗口内未取得HDD已验证切点，页面未发送取消。身份清理导致产品停止后，原任务Canceled/Root Aborted、cancel时间为空，源未移除，HDD留下未验证Preparing metadata及2,719,744字节partial。已保留两者和原Root/SQL，只清理源与自有容器；不把身份清理的副作用算成用户取消成功。

随后将观察上限调整为有界105秒（内核driver本身110秒），浏览器等切点115秒，并采用常规已验证的小文件规模；没有放宽原进程绑定、退出时限、源证明或回执断言，不签64/16MiB性能或完整1GiB能力。调整等待参数后静态测试曾因硬编码调用文本失败，按“切点必须先于取消动作”的顺序规则修正后47项全过。

## 证据与未验范围

47项关联工具测试（含3新门禁专项）以ResourceWarning为错误通过，8范围及Python/Node语法通过。原失败及成功三个目录均封存：

| 目录后缀（deploy/artifacts下container-recycle-inflight-cancel-） | 文件数 | 清单摘要 |
| --- | ---: | --- |
| 047546f455e74e918003bc34bc6091c5 | 107 | bd017ac0818da503f115844844e959d77bfcab33a30c711a53618615021ab1ee |
| 2656a28945b748a682336db18ae49435 | 111 | dd4356e627238698ac33a06aa339d129cbb9e62b99368e13ea3517194471be66 |
| ac5665ed2dbf4d8cb8f3b8451a11d1d4 | 127（含96源码） | d8478f0982489113528baf9095a8fcdbcf64d672cf368617b9112a0ce3aeec86 |

仅签Preparing/HDD已验证/源未移除时的取消，不签移除后、退出、主动撤权/停用、Worker/Agent重启、Unknown/跨写入等组合。独立恢复业务、目录/完整元数据、长期保留/彻底清理及59项继续；整体25%–35%粗估、整项/阶段完整签收和AI子计划0%保持。新门禁CI终态未在本批核验。
