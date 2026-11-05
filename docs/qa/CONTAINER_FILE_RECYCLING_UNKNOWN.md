# 容器回收源路径消失Unknown与跨管理员明确处置

2026-11-05，产品保持58b8174，最终门禁dd2cd970。承接[重启分项](CONTAINER_FILE_RECYCLING_RESTARTS.md)，以全新自有容器/1,048,583字节普通文件完成“已验证HDD、源未移除时，QA移动源路径后正常退出”的真实组合。原首轮门禁失败保留，API补救不补签原UI门禁；完整通过来自另一组全新资源，不重放旧Apply。

## 精确故障与Unknown事实

真实Authentik/Vue异步Ready、唯一确认后，复核原执行ID/完整计划、当前systemd MainPID/Invocation/cgroup、容器根和原操作锁两个内核FD及原pidfd，才暂停准确helper。Root为Preparing、historyVerified=true、完整源仍在原路径且私有空目录已绑定。

QA只在固定自有apps目录、原计划父inode、精确文件快照匹配且原helper仍活时，以目录FD及renameat2 NOREPLACE将recycle-rpc.bin移为recycle-displaced.bin。原设备/inode/模式/UID/GID/链接数/长度/mtime、字节SHA和user属性保持，ctime允许因重命名增加；后续对移位快照九字段逐字保持。不是删除用户数据或把原件移动到产品回收区，目标已存在时原子拒绝覆盖。

页面经正常菜单BFF/IdP退出。原pidfd的退出观察在允许UI动作之前开始，并在观察真实BFF退出导航POST时再次确认原进程仍活；成功组原helper1.801528秒内退出。无取消POST、无第二次确认/Apply、无SQL/时钟/Root/HDD编辑。

产品随后保存原任务Unknown、原执行Job Running/AwaitingContainerRecycleReceipt、原Root Unknown，completed为空、publishBarrier=true、租约已清，原执行Outbox仍有1条待处理。sourceRetained/sourceRestored为null；原路径与产品私有留存项均缺席，QA移位原件仍在，HDD完整数据/摘要已验证。QA知道原件的位置，不意味着产品可以把Unknown写成“没有移除”。

## 另一管理员撤销决定与明确确认

原会话退出后旧GET401、提示引用清空；隔离的新浏览器上下文正常输入另一临时管理员凭据，确认不同Subject。旧登录所属意图GET404，仅通过当前管理员恢复接口和真实文件页核实原意图/原执行。

实际Jobs页面先显示原执行Running及“结果未知，仍需核实原任务”，源事实为“未证明”。320×600明色异常面板显示原ID、原路径、Unknown/保护保留与已验证HDD，确认和“暂不处置”按钮完整可见。第一次选择“暂不处置”后，ACK/管理写入为0；父进程重新读取完整意图/Job SQL快照和Outbox计数、原Root/HDD字节及实际移位原件，与首次Unknown证明完全相等，才允许继续。

另一管理员随后实际点击“确认处置并释放保护”，唯一ACK POST202。原任务变为Acknowledged、原执行Job Failed/phase Acknowledged、Outbox清零、publishBarrier=false、完成时间及AcknowledgedBy写入，版本递增。原请求/登录/计划/执行ID和RootStartedAt保持，原Root仍Unknown，源事实仍null。HDD captureState=Pending标记与完整数据保持，不把它改写成Trashed/Canceled或解释成回收成功。

原Root/HDD、实际原路径缺席、空私有目录和移位原件均保持；ACK调用原操作Discard取得永久围栏，不重新Apply、不恢复或删除文件。唯一审计动作containers.files.recycle.unknown-acknowledged、原执行ID、不同管理员Actor、Result=Unknown、精确容器Target及非空Trace核对通过。实际Jobs页再显示同一原执行Failed和“管理员已明确处置未知结果”，无通用重新预检重试按钮；普通8拒绝/匿名4拒绝和两次正常双端退出通过。

清理前独立进程重读SQL、ACK审计、Root/HDD原始JSON、完整HDD数据和真实移位原件；仅通过后精确移除自有移位文件、已绑定空私有目录/容器，清理后再次独立核对永久原件和资源缺席。临时账号删除确认通过。源/HDD同设备，不签异盘灾备。成功组Root22→24/HDD25→27，2346保护文件/11核心/两宿主及全部指定旧业务保持，旧未验证partial也保持。

## 首轮失败、修正及独立补救

首轮50819f2d成功取得真实Unknown和1.825194秒原helper退出，但取证断言错误地要求pending Outbox=0；实际Unknown是非终态，原执行Outbox应保留1，ACK才清零。原“0条”断言已通过只读重现，SQL仍Unknown/Running/保护保留。随后Node等待父级标记时不能及时响应SIGTERM，清理wait超时将原断言遮盖成TimeoutExpired；原失败.json及journal保留。

修正为Unknown待处理1/ACK后0，并为精确Popen Node子进程增加有界TERM后KILL兜底，保存错误帧；不对任意PID或Root helper加杀进程入口。补充测试验证未处理条数的状态区别，以及实际自有忽略TERM子进程的有界回收。

原失败任务由新临时管理员通过现有BFF/API明确ACK一次，单独核对原Root/HDD、移位inode/字节及审计后，由另一进程独立补核并精确清理自有资源。Root20→22/HDD23→25保留，原Apply不重做。这是API补救与独立清理分项，不是原UI门禁通过，也不补造原browser.json/verified.json。

## 测试与封存

修正后78项回收工具测试以ResourceWarning为错误通过，范围8项、Python编译与Node语法通过。包含真实临时文件重命名/扩展属性、自有子进程pidfd、原子不覆盖、身份/内容漂移、Unknown错误false事实、同主体或重复ACK、撤销决定不变、未知Outbox保留及有界子进程清理负测；这些不是安装态UI证据的替代。

两目录均位于deploy/artifacts，私有权限封存，各100份冻结验证源码，逐文件SHA复算通过：

| container-recycle-unknown-后缀 | 原门禁 | 文件数 | EVIDENCE_SHA256.json摘要 |
| --- | --- | ---: | --- |
| c83c699ade544b6d8cb185e31612c10d | 失败，API单独补救/清理通过 | 131 | 918b2c3d47fd53426e2f78ce0319cbc2a1445e9dd5c2173033b472e663eb8958 |
| 1caba7661f0449d4a5174136eeebd164 | 全新资源真实UI及独立门禁通过 | 139 | cfbc0d90b576a0f41bc3fc296b03d91249791b59b8449c645274cf9f683f78ef |

失败unit whaledeck-qa-recycle-unknown-bc7042e818e8、Invocation c63035cc918341de8ff999bdeda1df08原退出1保留。成功unit whaledeck-qa-recycle-unknown-01b60d810191、Invocation 1bfa514600794d698132a3aaecfcb02b准确Result=success/ExecMainStatus=0。成功原helper PID1365646/Invocation96c4f9d31f8047e9a9a770a03fa142a3，意图283c7d7f-d2dc-cff8-ef5f-20df0b311615、原执行d818c294-cc74-86a7-43ac-45461950f5d3；五张真实页面截图保留。

仅签源路径消失+正常退出及另一管理员撤销/明确处置，不签容器实例变化、跨写入延期/释放、移除后故障、独立恢复业务、长期保留/彻底清理或59项完整交付。新候选准确远端CI终态仍待核验。整体25%–35%粗估、整项0/59、完整阶段0/6、AI子计划0%和旧代码量快照保持；17项移出不算完成。
