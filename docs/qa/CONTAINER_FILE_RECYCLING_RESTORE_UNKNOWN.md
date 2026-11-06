# 独立恢复归属索引丢失：真正Unknown与不同管理员处置实机验收

2026-11-06。工具冻结 `7bee0baab323bf7ae48568812cabdf8481ce0d2c`，生产仍093f44a9。全新资源中，在原件已返回、但本恢复日志仍Applying时，仅隔离本轮匹配的claim索引；产品自行重建围栏并保存Unknown。不同真实管理员先暂不处置、再一次明确确认，Unknown/null归属事实保持，清理前后独立核验通过。这不是把既有Completed/Superseded改写成Unknown。

## 故障范围和真实不确定性

实际临时Authentik身份通过可见Vue流程完成1次来源回收预检/确认、1次独立恢复预检/确认。returned cut重核本恢复Applying、来源Restored、原inode/字节已返回、原helper停止态、Root及两操作锁三份内核FD、精确MainPID/cgroup/Invocation与当前HDD。

仅对固定claims目录中、本来源操作和本恢复操作共同标识的索引，完整读取raw JSON/摘要及root:root/0600/单链接身份，确认与cut的claim匹配后，使用同目录renameat2 NOREPLACE原样移到严格派生的自有隔离名。目标必须不存在，保持两把操作锁，旧索引inode/字节保留；不编辑SQL、Root JSON、来源日志或HDD，不删除原件，也不解除其他操作的索引。

再次确认旧路径缺席、隔离件不变、原helper仍绑定原pidfd/三FD，才对该原pidfd发送SIGCONT继续当前调用，没有重发Apply。Root recover_locked看到missing且claimed的非终态记录，先重建同一来源围栏，再保守保存Unknown/sourceOutcome null；不会用内存中的Restored来源状态直接推定本恢复完成。若此前本恢复已经Completed，原生对照证明其状态保持Completed，不能进入本故障的Applying门禁。

## 实机时序和结果

unit `whaledeck-qa-restore-unknown-620bd560` / Invocation `bd3f18582e8c4351bcd54b4440b35278`，exited/Result success/ExecMainCode 1/ExecMainStatus 0。原pidfd约2.444秒结束，计时从索引移动前开始，包含受控继续；原helper结束、SQL Unknown且租约清空后才放行原上下文正常退出。不能把重建claim后、保存Unknown前的另一次中断算作本次通过。

当前Root Unknown、sourceOutcome null；API/SQL的sourceState、sourceRetained、sourceRestored、restoredByThisOperation均null，bindingCurrent false；historyVerified/Available仍true。来源Root仍Restored，原件实际存在且是原inode/内容/属性，HDD保持，这正说明Unknown不等于“没有发生恢复”。重建claim完整语义匹配旧索引，inode不同；旧隔离索引保持原raw字节。允许产品JSON属性顺序不同，不把重建索引声称为原inode或原raw序列。

未知阶段意图Unknown、执行Job Running/AwaitingContainerRecycleRestoreReceipt、17字段摘要保留publishBarrier=true，原投递仍待处理。原会话退出后匿名401；不同管理员的原owned-ID请求404，但专属恢复历史200。真实Jobs页显示未知和未证明归属，通用重试按钮缺席；文件页恢复异常行显示尚未证明本操作归属及HDD曾验证。

另一管理员先点击“暂不处置”：确认请求/管理写入0，原保护、未知字段、Root/claim/HDD和原件完整保持。后续源码复核确认当前ClaimAsync排除Unknown；本轮原始初始/暂缓SQL也实际完全相同，跨写入工具已加强为完整SQL保持，不能声称有自然调度推进。API回执的replayed纯观察位仍与归属事实分开；没有修改SQL时钟或定时器。再次刷新原任务后，仅一次明确ACK，唯一审计属于不同主体且结果Unknown、原CID/执行及trace存在。

确认后意图Acknowledged、Job Failed/Acknowledged、publishBarrier=false、投递处理完毕；Root仍Unknown，归属仍null，原记录/claim/来源Root/HDD及原件事实不变。没有恢复/重做原操作、没有删除HDD或把未知压成false。原所有者及观察者各一次正常BFF/OIDC双端退出，普通9个、匿名5个请求拒绝，取消和retry为0；恢复观察者唯一业务写入就是ACK。

## 独立核验、清理和准备期失败

独立live进程重新读取Unknown、暂缓决定、ACK的SQL/Job/API绑定和Root/HDD，核对旧隔离索引与产品新建围栏分别存在、不同inode，以及原件实际返回快照。仅在此通过后删除本轮已验证旧索引副本及测试原件、空私有目录、自有容器/种子；产品重建的claim和Unknown Root/HDD全部保留。final再次读取重建claim实际身份/摘要、确认旧隔离名缺席，并读取保留历史/HDD及先前live原件证明，不声称读取已经删除的源。

准备期有3项修正记录：初版原生对照误期待已Restored的旧入口抛RESTORE_CLAIMED，实际是只读幂等历史回执，改测无重复rename；旧摘要验收函数固定期待publishBarrier=false，改成实际行保护位并拒绝Unknown摘要丢保护；Node生成正则转义语法错误改为路径分段判断。均发生在实机启动前，没有生产影响；失败事实按本轮工具回执摘要保留，未补造完整旧失败源码/日志。修正后4项原生临时文件、5项Unknown投影及65项关联Linux root检查通过、零跳过，9项CI合同/8项范围与Python/Node语法通过。隔离namespace/SQL提供者有明确替身，和本页实机整链分列。

实机首次总门禁通过。私有证据 `deploy/artifacts/container-recycle-restore-unknown-c73b35bd810943779c0e4efa2984af85`，实际冻结137份源码闭包，共193文件清单摘要 `8c12c0cc3308061b801cfa0efa0cf7066c0c7b19d77c303f4d16e0e4d52e8d6f`。准备期事实摘要 `container-recycle-restore-unknown-preparation-34030d5643224105b6c11402eefabcf5`，2文件清单摘要 `b02816c1a0dbf4db74f26b3bd051b474c03a6b56fa509e9b0246fbd7de8c666b`。实际逐文件核对，父目录0700、文件root:root/0600；凭据不入Git、日志或项目图谱。

最终35回收/21恢复意图、活动0；回收Root70/HDD73、独立恢复Root56。2346保护文件、11核心服务、旧完整行SHA及093f44a9两宿主/PID保持（Agent2974845、Maintenance2974836）。源/HDD仍同设备2049，不称独立物理盘灾备。这里只签已返回但归属围栏丢失这一Unknown和跨管理员确认；跨写入组合、长期清理及其余59项继续。17项移出不算完成，整项0/59、阶段0/6、酒馆/供应商和Live2D功能0%保持，master完整交付尚未完成。
