# 容器所有者：真实页面中断、进程恢复与Unknown接管

> 2026-11-02后续：d1fcca8已覆盖并通过真实组移除/账号停用、正常7场景及独立核验，见[最新证据](CONTAINER_OWNERSHIP_REVOCATION.md)。下方候选/待实机表述保留为原批次事实，不据此恢复未部署状态；其余故障与递归/ACL/删除继续。

验收执行：2026-10-31；记录补齐：2026-11-02。四类故障及首轮Unknown使用已装d5e62ca/Agent1.48；目视发现Unknown小屏确认按钮问题后，修复e91a770正常覆盖并再次完成真实Unknown与独立复核。没有自动回滚。本页仅签收下述非递归所有者故障子门槛，不代表全部所有者、递归、ACL、删除或完整文件模块完成。59项主任务保持，17项移出不算完成。

## 真实切点与授权边界

原管理员通过正常Authentik表单登录，在实际容器文件页面填写目标2200:3200、生成预检并明确确认。先停当前Worker令确认落为Pending，再启动同CID/镜像Worker。观察器仅在原执行ID、完整Root/SQL计划、目标UID/GID、原inode九字段指纹、内核执行身份、容器根FD和操作锁FD都匹配时，以pidfd暂停对应已安装固定apply transport。

实际切点为Root Preparing且historyVerified=true、HDD元数据已持久化、源仍完全未变；不是fchown之后或回执丢失切点。没有安装产品测试钩子、改写SQL/Root/租约/时钟、注入会话或替换HTTP。SIGSTOP只定位切点，验证器不向该helper发送SIGKILL；随后必须观察产品因取消/连接断开而结束它。Worker/Agent故障前排除其他活动任务、终端、锁、发布屏障和外来transient。

## 四类实际页面与进程故障

完整父命令退出0，验收源码cf0dcba，产品d5e62ca；另一个只读进程重新验证原SQL/Root/HDD与审计、准确资源清理。

| 场景 | 实际动作与结果 | helper退出观察 |
| --- | --- | --- |
| 页面取消 | 手机页面先继续等待，随后一次明确取消；原意图/执行Job Canceled，Root Aborted | 5.413秒 |
| 正常退出 | 真实用户菜单完成WD/Authentik退出，原登录撤销；Canceled，Root Aborted | 5.436秒 |
| Worker强杀 | 准确Worker容器SIGKILL，恢复同CID/镜像；原自然租约到期后Failed/Interrupted，Root Aborted | 0.116秒 |
| Agent重启 | systemd实际重启，维护入口PID不变；Failed/Interrupted，Root Aborted | 0.030秒；完整服务drain 30.396秒 |

helper退出时钟在动作前启动，并发观察pidfd，遵守绝对15秒窗口，不以较晚服务drain结束时间替代。Worker场景保存原租约及三次未到期时Applying/保护保持的观察，完成时间不早于原租约期限，没有提前接管或数据库编辑。

四例每次真实页面登记/确认各1、文件内容PUT为0；只有取消例发送1次取消。仍登录页面明确显示原任务取消/失败终态，再正常双端退出。每例SQL双Job/Outbox/事件、唯一原主体结果审计、Root Aborted/ownerConfirmed=false与物理HDD元数据摘要一致；完整2097159字节、原inode/UID/GID/mode/mtime/ctime全部未变。7张实际页面PNG保留，320暗色取消终态已目视核对。

## Unknown与不同管理员真实手机处置

首轮d5e62ca实际父命令和独立复核均通过，helper由退出触发产品结束，观察为5.115秒。随后目视发现确认按钮在视口外，不能据自动滚动点击宣称小屏体验完成。修复后的e91a770重新完整执行通过，退出观察5.284秒，以下最终证据对应该轮：

- 原helper停在已验证HDD且源未变的切点，只删除准确的非root无网络临时容器，绑定源保留；原用户通过实际页面正常退出，原临时Authentik身份实际删除后，才创建不同管理员。
- SQL保持Unknown、原执行Job Running、ownerConfirmed=null、publishedVersion=null、HDD已验证及永久发布保护；原Root自然变为永久Unknown，原物理源全部字节和九字段指纹未变，不伪造“已确认未修改”。
- 另一临时容器的真实目录写入已登记，三次观察仍Pending、RootStarted为空、目录不存在、nextAttempt实际延后，证明被原保护推迟。
- 新管理员正常登录320暗色文件页，真实查看原UID/GID/目标、执行FSUID/FSGID及HDD事实；先“暂不处置”零确认，再一次明确“确认处置并释放保护”。原意图Acknowledged、原执行Job Failed，原Root仍Unknown且ownerConfirmed仍null；不把管理员处置显示为所有者修改成功。独立审计确认处置者为新主体，原执行主体保持旧主体，处置审计唯一。
- 等待目录仅在处置释放保护后才实际创建，RootStarted不早于原CompletedAt；新管理员随后正常双端退出。
- 从固定受限helper提交原operationId/owner/plan及正确行协议换行的迟到Apply，仍返回原Unknown/replayed；Root完整SHA、HDD元数据及物理源不变，未再次fchown。
- 父命令和独立只读核对均退出0。准确源/CID和临时身份清理，活动/租约/屏障0，永久SQL/Root/HDD保留。独立进程不声称源删除后重新读到了物理字节。

以上业务均为HDD来源和物理HDD元数据，不冒称本批NVMe业务或异盘内容备份；早期NVMe引擎证据独立保留。

## 小屏修复与覆盖发布

e91a770为所有者异常处置弹窗添加动态视口高度、正文内部滚动和固定头/底部，手机上下留12px；不缩小文字或隐藏警告。实际验收器新增确认与暂不处置按钮完整处于视口的断言。最终真实320暗色确认截图已目视复核：完整确认按钮和取消均可见，原事实及警告保留。

生产预览首轮40项命令为32通过/8失败：新增长路径夹具把同一段写为超过255 UTF-8字节，产品正确拒绝无效状态。修正为多个合法长路径段后，320×600/800×明暗×正常/丢处置响应的8项专项全部通过，包含确认/暂不处置及原处置查询按钮可见；只修改夹具，未放宽产品路径校验。此为同一CSS构建的32+8项不同用例覆盖，不把失败命令写为完整40全绿。原失败日志/一份截图与上下文保留。

类型/生产构建、lint和src格式检查通过后冻结e91a770，干净运行checkout从本地dev快进，唯一发布单元whaledeck-dev-publish-e91a770.service，InvocationID fd2cbd7260954509a63d201ceb8785f6。Result=success/0、同次journal成功终态及六组件实际二进制/镜像/HTTP来源、宿主MainPID exe/DLL映射、生产迁移和隔离核验通过；11核心healthy，数据库平台未重建，API/Worker无宿主端口/Docker Socket/privileged。没有自动回滚。随后重新完成真实Unknown与独立核验、宿主/动态入口检查，不重复四类早期故障或旧常规7场景。

## 验收器失败保留

8a9bd5d首轮误将Python内置PermissionError改为不存在的OwnershipError，观察器NameError，未取得有效暂停切点，整轮失败。真实业务实际完成，保存Succeeded/Completed及HDD、完整源证明后准确清理，不改记故障成功。cf0dcba修复异常名并加入cmdline/FD权限拒绝与消失分支检查，16项Linux-root边界零跳过；原PID/FD/计划/目标UID/GID/执行身份/原文件/锁/时间窗断言全部保留。产品没有为适配验收器放宽权限。

## 检查与私有证据

- 新16项观察器、既有所有者引擎/传输33项和原普通权限观察器12项Linux-root均执行零跳过；8项范围检查通过，Python -W error和JS语法通过并接入CI。后端3581 Unit/247 Integration及前端755等前批证据保留，没有声称此次重复全跑；GitHub CI未取得全绿终态，不签全绿。
- 四类故障：deploy/artifacts/container-ownership-faults-e0d0be01490245edaacf8ec8ed4b44f2/；baseline/cut-root/bound-helper/final SQL/Root/HDD、自然租约样本、7 PNG/摘要、验证源码SHA及independent-final。
- 首轮失败：container-ownership-faults-7cc5b67595e7473cad2109ee757610f0/，原失败、实际成功业务/原文件验证及failure-cleanup-verified保留。
- 原d5e62ca Unknown：container-ownership-unknown-7c02375beec0419ea4f00a4d2eefc18e/；业务/独立通过与原小屏布局问题均保留。
- 最终e91a770 Unknown：container-ownership-unknown-9963f0566a7d4efcb33bb5478623c709/；原/新主体UI、Unknown/最终SQL、Root/HDD、等待目录时序、迟到Apply、3 PNG及摘要、冻结源码与independent-final。
- 修复发布：container-ownership-recovery-layout-deploy-e91a770/，before/provenance/verified及原Invocation/unit journal。生产预览夹具失败样本在ui-candidates/ownership-recovery-layout-first-error/。
- 主日志/tmp/wd-ownership-faults-all-{first,second}.log、wd-ownership-faults-independent.log、wd-ownership-unknown-first.log、wd-ownership-unknown-independent.log、wd-ownership-unknown-layout-fixed.log、wd-ownership-unknown-layout-independent.log、wd-owner-recovery-layout-{browser,unknown,build,lint,format}.log及宿主检查日志。以上证据私有、不入Git；记录补齐不是2026-11-02重新运行这些实机操作。

## 仍需实施与验收

所有者仍需真实组撤权/账户停用、fchown之后/回执丢失、目录及特殊对象/实例重建/跨盘等其余故障组合；本批正常退出只证明原登录撤销。继续递归权限、ACL、可恢复删除、长期清理及完整资源隔离；其余59项、UI统一风格、C#酒馆/集中供应商/AI Live2D仍按主目标推进，master未正式交付。

2026-11-02记录补齐检查：8项范围测试通过，5份修改MD的496个本地链接无缺失，git diff --check通过；没有重新发布或重复真实文件验收。

2026-11-02接续：[实时权威复核源码候选](CONTAINER_OWNERSHIP_LIVE_AUTHORITY.md)已完成并通过分层测试，未部署或真实组撤权/账号停用验收；本页旧安装与实机记录保持原批次事实。
