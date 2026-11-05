# 独立回收恢复原生探针与原件核验候选

2026-11-06，候选9eb6e6c9718b9fac03a2e34445776fd1bfbc64b9（初稿9524272d），生产保持58b8174/Agent1.56。本批承接[协议候选](CONTAINER_FILE_RECYCLING_RESTORE_RPC.md)，完成探针与独立核验工具候选。尚未安装1.57，也没有调用新的恢复RPC、捕获新来源或创建实机资源；不签永久业务或真人页面恢复整链。

## 原生探针与独立身份

TerminalProbe新增container-recycle-restores模式，仅固定本机UDS和work/recycle-rpc.bin自有验证路径，限制完整头、64条指令、单帧64KiB和12分钟总期限。必须先观测major1/minor>=57、原回收与独立恢复两项能力，才读取固定私有签名密钥；密钥finally清零，不输出凭据或签名。

探针支持原来源plan/apply/inspect，再执行新恢复plan/apply/inspect/discard。原回收计划、来源操作、新恢复计划和新恢复操作分别在本进程完整登记；新OperationId/Context.JobId相同且不等于原来源ID。不能传任意状态路径、宿主路径或外部来源引用，不能通过新模式执行旧Restore。父门禁必须在每条写入指令之间独立采集原件与物理事实，探针不会自行重试或清理。

新计划边界预置16次拒绝，新操作预置16次拒绝，覆盖无签名、错误签名域/方法/时限、来源/owner/HDD摘要/留存身份/上下文篡改、新旧ID混用和不存在的Inspect。正向计划重算完整原生指纹；回执由Contracts的精确optional三态规则检查。以上正向和拒绝指令已编译，但尚未在新安装态执行；不把代码中的预置数量写成实机通过数量。

## 独立原件与物理核验边界

新增Python工具不导入恢复产品实现来证明恢复结果，独立验证完整来源计划、新恢复计划域指纹、ID/owner、精确整数、Root记录和来源claim，以及原回收Root与实际HDD元数据/数据摘要。自洽重算但属于另一来源的HDD摘要、错误claim或另一任务完成的恢复均不能冒认为本任务Completed；Unknown事实必须缺席，不能转为false。

只读采集固定恢复Root、claims和原回收Root，要求私有目录root:root/0700、原件root:root/0600/单硬链接及无符号链接；通过有界FD读取和HDD attestation采集，按原字节封存。缺席claim也单独保存null原件，不创建新HDD历史。物理核验另行比较原inode、九字段、字节/用户属性、目标/留存互斥和私有目录identity；普通记录投影明确返回physicalNamespaceChecked=false。

record_proof只接收已返回的终态RPC回执与静止原件。AbortedSaved强杀切点可能已写Aborted而尚未清claim，没有成功返回的取消回执；此时必须由所属执行走Inspect/恢复后再核验。工具本身只读，不清claim、不把该中间快照签为已完成取消。Unknown物理故障需专属门禁，通用物理函数拒绝签收Unknown。

## 实际执行与修正记录

新增16项专项检查通过（含真实临时文件/HDD格式/FD原字节和实际命名空间冲突），现有与新增工具合计109项、原生回收/恢复组54项、范围8项均零失败/零跳过。Release探针构建零警告/零错误，标准git archive冻结源码、固定SDK镜像、自包含linux-x64发布成功；实际使用terminal-probe.ikVWpG，build身份9eb6e6c9。前一9524272d自包含构建也保留，仅作为初稿构建，不用于修订后的真实拒绝验证。

首轮12专项中的5项原件夹具失败：复用旧夹具时仅传basename，未把work/recycle-rpc.bin完整相对路径纳入计划。已修正为真实相对路径和对应版本摘要，原失败日志及exitCode=1保留；没有放宽产品路径或核验规则。

复核还发现，通用能力错误码不能单独证明拒绝是因为旧协议。修订后仅新探针错误帧记录实际major/minor及两项能力布尔值；独立工具拒绝把minor>=57但缺其他能力、网络错误或启动失败当作旧版本验收。此次真实探针观测major1/minor56、原回收能力true、新恢复能力false，签名前退出；头内CID仅为合成标识，不创建容器，不发资源操作。这只签实际旧版本只读拒绝，不签1.57正向恢复或16+16次实际拒绝。

临时测试使用明确namespace/设备证明替身，不是实际Docker/HDD或Authentik。原始HDD与来源同设备的既有边界不变；没有异盘灾难恢复、全新实例重绑定、HDD重建或长期清理的完成声明。

## 生产保持、证据与后续

专属unit whaledeck-qa-recycle-restore-probe-b871f717、Invocation6761884a992d4c3c8abb47b2173a13d5，准确Result=success/ExecMainStatus=0。前后快照和独立进程重读均逐值等于前批已签基线：2346保护文件、11核心healthy、原回收Root28/HDD31、目录Root96及旧来源/恢复记录保持，生产和两宿主仍58；独立恢复namespace仍缺席，没有安装或真实回收/恢复写入。

证据deploy/artifacts/container-recycle-restore-probe-c471e4a142ef4e2e9388dbabc6fabdf4，共246文件、221份冻结源码；EVIDENCE_SHA256.json摘要5e786b70ac65d78d0fea3810b447a44ff73f54ca6f5ebeee062da682fff41158，所有文件逐一重算通过。初稿失败/构建另保留于container-recycle-restore-probe-b871f717822b48aaa7c9290b297a7031，并按原字节复制到最终证据。

前序f333a69a准确CI尚有一条frontend-browser分片未终态；新工具CI在推送后另看，不能把本地检查当CI全绿。接下来完成安装态完整 owning gate、准确CI及标准安装，再签新来源/取消/迟到调用/原inode返回与Root/claim/HDD整链；然后永久编排/真实PG/共享保护/Worker/API/Vue、真实身份页面和故障矩阵，以及长期清理与其余59项目标。17项移出不算完成。整体25%–35%仍为工作量粗估，完整主项0/59、阶段0/6、AI子计划0%保持。

项目图谱whale-deck/container-recycle已按本批稳定eventId回执同步为active（非整个阶段完成）；本地pending记录已移至synced。
