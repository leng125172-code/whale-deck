# 容器移动的实际部分字节中断验收

2026-10-29，产品运行时 `a59ec973478471745a1b7a5aa41da16dae20d426` / Agent 1.46；验证工具冻结为 `dda816250e8673d3feb2abdf4818ca031e611b93`。本轮只增加验证工具、CI和证据，不重复部署产品。59项继续，17项移出不计完成。

## 范围与切点

每例使用独占、无网络、只读容器根、非root 1000:1000测试容器，唯一可写bind内生成134,218,176字节文件。正常Authentik登录和Cookie/CSRF/Origin调用实际BFF，登记永久预检任务、明确确认并产生独立执行Job。

观察器只接受准确测试目录、容器实例、SQL执行ID/owner/命令及完整内层文件计划。以pidfd绑定固定安装的move apply传输进程，同时要求其持有准确容器root FD和本操作锁FD。仅当Root Running、内层Preparing、源原始9项指纹不变、目标不存在、私有暂存文件inode已登记且0<已写字节<计划总量时SIGSTOP，再在进程实际停止后重新核对所有证明。

没有安装产品暂停参数，不改SQL/Root/租约/时钟，不用验证器SIGKILL辅助进程；产品必须停止它。退出时间由并行pidfd观察器记录，不把Agent服务drain或健康等待误算成辅助进程退出时间。

## 已通过：明确取消与双端退出

实际BFF两例均在部分字节存在时触发。明确取消4.008秒、正常退出4.041秒停止原helper；业务及执行Job均Canceled/CONTAINER_MOVE_CANCELED，Root Aborted。目标没有发布，源文件完整SHA256、普通属性和原始inode/ctime等9项指纹不变；没有私有留存或暂存载荷残留。

结果的destinationPublished/sourceRemoved/sourceRetained在SQL与Root均为false：表示此次移动未发布目标、未移除源、未把源转入私有原件；不是说原路径的源文件丢失。原意图/执行ID、永久Root内外回执和审计保留，Outbox完结、发布屏障和租约释放。

证据 `deploy/artifacts/container-move-inflight-e542eb4accc948f782527db591814a19/`，包括baseline、partial-cut、Root原件归档和results/verified/independent-final。日志 `/tmp/wd-container-move-inflight.log`；独立复核 `/tmp/wd-container-move-inflight-independent.log`。

## 已通过：Worker强杀与Agent重启

第二批 `container-move-inflight-97b1c63f2a464259b3fcf7fe3cef050e` 的两个实际部分字节切点通过。Worker明确SIGKILL后显式启动，CID与镜像不变；产品结束Root helper用时0.116秒，新Worker在原90秒租约到期前连续保持原Applying/屏障，不提前接管，最终完成时间不早于原租约到期。原意图/执行任务均按既有规则核定Failed/CONTAINER_MOVE_INTERRUPTED，没有新执行ID、重做移动或改写租约。

Agent实际systemd重启，辅助进程0.268秒退出；完整Agent优雅重启耗时30.617秒，二者分别观察。维护服务PID没有改变，Agent实际新apphost构建来源仍a59ec97，API继续可用。恢复后同样Failed/CONTAINER_MOVE_INTERRUPTED。

两例Root均Aborted，SQL/Root三事实为false；源134,218,176字节完整SHA及原inode、UID/GID、模式、纳秒mtime/ctime等9项指纹不变，目标不存在、无私有载荷。准确测试源/CID在核验后清理，永久SQL双任务/Root原件/审计保留。

| 场景 | 已写私有字节 | 计划总字节 | 原helper退出秒 | 业务终态 |
| --- | ---: | ---: | ---: | --- |
| 明确取消 | 327680 | 134218176 | 4.008 | Canceled |
| 正常双端退出 | 196608 | 134218176 | 4.041 | Canceled |
| Worker SIGKILL | 163840 | 134218176 | 0.116 | Failed / Interrupted |
| Agent systemd重启 | 262144 | 134218176 | 0.268 | Failed / Interrupted |

本表是验证证据的精确字节数；不作为产品页面的单位展示格式。

日志 `/tmp/wd-container-move-inflight-restarts.log`。两批完成、wrapper删除临时身份后，运行独立 `verify_container_move_inflight.py` 同时复核四例；日志 `/tmp/wd-container-move-inflight-independent-all.log`，两份目录均有原SQL/Root和独立检查的 `independent-final.json`。全局移动意图9条（4成功/3取消/2中断失败）、活动/租约/屏障0、临时身份0、活动transient unit0、11核心healthy。

## 观察器与独立复核

14项实际Linux-root边界测试通过，已接CI：完整SQL内层绑定、原执行/owner/命令、源替换及父inode、部分文件inode、空/完整/已发布拒绝、错误argv即使持双FD仍拒绝、非测试目录、符号链接/外来私有对象、原子回执替换，以及独立退出时钟/超时不误报成功。测试不替代实际BFF切点。

第三个独立只读进程在两批全部结束后复核四例原SQL/Root原件及内层规范摘要、准确源/CID已清理，临时身份0、活动任务/终端/路径锁/移动屏障与租约0、活动transient unit0、11核心healthy。它不创建身份、不操作容器、不改业务状态、不在文件清理后冒称重新读取了已删除的物理字节。

## 未完成边界

本轮是实际HTTP/BFF文件部分复制阶段的中断，不冒称可见浏览器进行中交互、目录树发布/源留存阶段、跨盘BFF或Unknown管理员页面已全部验收。后续仍需这些故障组合、权限复核/完整用户隔离、容器删除/权限变更、长期清理及其余59项。

## 后续目录部分源留存Unknown验收

2026-10-30，目录目标完整发布后部分原件留存中断、不同当前管理员真实手机页面处置、跨容器等待任务和晚到原固定helper栅栏已通过，见[目录Unknown证据](CONTAINER_MOVE_UNKNOWN.md)。不将此前“仍待”当作当前未实现描述，也不冒称全部目录故障已验收。
