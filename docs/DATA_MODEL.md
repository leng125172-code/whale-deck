# WhaleDeck 数据模型

2026-11-04 28062da实际来源摘要/Unknown明确处置及三次读取核验通过；生产restore_source_json、20261103085710迁移及18CHECK实查。普通200 JSON null/恢复7字段、canonical来源和请求/计划/执行/owner绑定保持，旧135HDD保持、总137两新前像分列，无新schema。 见[实际来源UI与发布边界](qa/CONTAINER_ACL_RECOVERY_SOURCE_REAL_UI.md)。

2026-11-04 恢复来源摘要候选（未部署）：仅读取原container_acl_intents.restore_source_json及既有完整性保护；不新增表/列/迁移，不修改普通Intent或Job schema。独立ContainerAclRestoreSourceSummaryDto仅captureId、changedAclType、kind、createdAtUnixMs、originalMode、accessEntryCount、defaultEntryCount，普通意图返回null。HDD丢失后仍可读登记时快照，不能据此认定实际HDD存在、当前文件状态或恢复成功。 见[候选与验收边界](qa/CONTAINER_ACL_RECOVERY_SOURCE_UI.md)。

2026-11-04 实机接续：b69302b不重发，恢复写后同CID实例变化与原正常退出组合、不同管理员明确处置和跨容器目录保护通过；PID/StartedAt变化但原实例/来源及Root Unknown/null确认不改，133旧HDD原件保持。无新生产schema、RPC或授权扩大，见[实例变化事实与未验](qa/CONTAINER_ACL_RESTORE_INSTANCE_CHANGE.md)。


2026-11-04 接续实机：b69302b不重发，恢复写后目标消失的Unknown/不同管理员明确处置与跨容器目录延期释放已验收；canonical来源和恢复专属审计/原执行Failed、Root Unknown与null确认保持，管理员通用面板尚无来源专属展示。无新协议/生产schema/授权，见[实际证据与范围](qa/CONTAINER_ACL_RESTORE_UNKNOWN.md)。


2026-11-03 接续实机证据：b69302b不重发，历史恢复写后Agent重启及原任务摘要/正常退出、独立核验通过；当前Agent1772448、维护1688513，152维护样本正常，旧128份HDD元数据保持。来源捕获缺Job证据的首轮失败与工具修正保留，无新协议/数据库schema或授权扩大，见[真实事实与未验范围](qa/CONTAINER_ACL_RESTORE_POST_AGENT_RESTART.md)。


2026-11-03 实际接续：b69302b限定历史GET超时/软分页修复已标准覆盖，六组件与16helper同源、生产恢复列/18CHECK保持；公共8080完整128份读取及真实Vue恢复写后移组/停用、独立只读复核通过。Agent最小授权/user NOPASSWD保持；无新协议minor或数据库schema。失败探测和独立账号检查重叠分别保留，其他故障、递归ACL/恢复及删除仍待，见[实际发布与范围](qa/CONTAINER_ACL_HISTORY_TIMEOUT_RELEASE.md)。


**2026-11-03 历史读取超时定位无schema/迁移/生产数据修改**：写后移组分项完成事实保留，失败停用CID仅有Succeeded普通来源捕获，无恢复意图/执行/撤权；新批也未登记恢复，两个失败源树与HDD保留。b69302b只改限定代理预算和只读分页，不减库存/条数上限或原件校验；真实124份候选只读保持，默认488Integration/7原生跳过分列。生产仍原18CHECK/cad，尚未覆盖或完成双场景签收。[事实](qa/CONTAINER_ACL_HISTORY_TIMEOUT.md)。

**2026-11-03 恢复写后完成事实与取消/强杀实机通过**：无schema/迁移变化，4意图/8Job、2旧来源+2当前HDD和9条来源hash恢复审计保持。目标已写后两原恢复均Succeeded/Completed并发布实际新版本，取消时间仅取消场景非空；强杀原token/expiry/来源与全部权威hash、保护保持87.034秒/41采样，到期后原执行对账。完整新清单/迟到Apply保持，独立核验/原上下文退出和清理通过；旧schema9只读92原件不变。[实际事实](qa/CONTAINER_ACL_RESTORE_POST_CHANGE.md)。

**2026-11-03 恢复强杀/重启原租约与永久事实实机通过**：无新schema/迁移，4意图/8Job、2旧来源+2当前HDD及8条Failed恢复审计保持。Worker强杀原token/expiry/来源/请求-计划-执行hash及原登录绑定保留，86.846秒/41自然采样，到期后原执行Failed/Interrupted/Root Aborted，无发布版本，取消时间null、释放保护；Agent分项同样原失败终态。独立核验、正常原上下文退出和准确清理通过；旧schema8只读兼容87文件不变。[数据与范围](qa/CONTAINER_ACL_RESTORE_RESTARTS.md)。

**2026-11-03 恢复实时移组/停用永久事实已实机验证**：无schema/迁移变化，两新资源各真实捕获旧ACL并新建恢复，切点当前原件HDD已验证/目标未写入时撤权。4意图/8Job、2旧来源+2当前HDD及8条来源hash恢复审计保持；原恢复Canceled/Aborted、取消时间null、无发布版本且释放保护/租约，完整源不变。独立核验通过，旧schema7只读兼容及81原件字节保持；其他故障/递归ACL/删除继续。[实际事实](qa/CONTAINER_ACL_RESTORE_REVOCATION.md)。

**2026-11-03 恢复进行中取消/退出永久事实实机通过**：无新schema/迁移；两套严格资源各先完成真实普通ACL来源捕获，再执行恢复到已留当前原件/尚未修改ACL切点。共4意图/8Job、2旧来源与2新当前HDD，恢复cancel5/logout4共9条来源hash审计；原恢复Canceled/Aborted、保护/租约释放、全源保持，独立复核和准确清理通过。首次语义已过但截图状态未入视口记录保留，后用新资源严格复验。剩余故障/递归恢复/删除继续。[实际数据边界](qa/CONTAINER_ACL_RESTORE_FAULTS.md)。

**2026-11-03 ACL恢复来源生产迁移及真实永久业务已验证**：cad6182标准覆盖已应用20261103085710_ContainerAclRestoreSources，生产restore_source_json存在/CHECK=18。新独占6意图/5执行、11恢复Job SQL/API、23来源hash审计与完整canonical来源逐项核对；旧捕获与5新当前ACL原件Root/HDD分列且保持，独立只读Passed。普通null兼容/不可变与Down保护沿用已验证候选；没有改SQL状态取得验收。故障组合/递归恢复/删除仍待。[实际迁移与证据](qa/CONTAINER_ACL_RESTORE_REAL_UI.md)。

以下旧日期条目保留各自当时的版本与“未部署/未验”边界，按时间顺序阅读，由上条本批记录接续，不作为当前运行状态。

**2026-11-03 ACL恢复发布准备无新schema/生产写入**：修复历史读取FD身份并新增真实恢复SQL来源全文/新执行/23专用审计和新旧Root/HDD分列证明工具；29反例证明与8范围通过，不是实际生产恢复证据。生产来源列仍不存在、17 CHECK，发布后迁入先前已有来源迁移；未以重放旧捕获或改SQL取得验收。[当前发布边界](qa/CONTAINER_ACL_RESTORE_RELEASE_PREPARATION.md)。

**2026-11-03 ACL历史恢复管理员API/Vue候选3fd7f5f接入，未部署**：传输新增{intent,source}授权响应，复用此前来源不可变列和原Job/Outbox/共享锁，不新增迁移或历史缓存。浏览器仅保存key/ID、Subject/CID与restore模式提示，ACL/capture/digest不入sessionStorage；原登录刷新只GET原引用。4361 Unit/488 Integration（7默认跳过）和1059前端Unit通过，HTTP仓储/Root为夹具，本批未重跑真实PG或实际HDD恢复。生产来源列仍不存在、17 CHECK；47e3ba1隔离PG仅保留原冻结来源。[详情与边界](qa/CONTAINER_ACL_RESTORE_WEB.md)。

**2026-11-03 ACL历史恢复永久化候选47e3ba1，生产未迁移**：迁移20261103085710在原container_acl_intents新增可空restore_source_json及第18个CHECK，完整选定来源绑定请求/计划/执行MAC；独立触发器禁止改变/移除/后加来源，原不可变与共享保护保留。原普通请求null不改变旧规范JSON；恢复仍原ACL JobType/两Job/Outbox，固定restore审计只放来源hash，无ACL正文或capture。任何恢复意图存在时Down拒绝丢失来源。双隔离真实PG1执行/1通过/0跳过验证约束/事务/旧MAC/锁/降级拒绝及准确清理，Root和来源合成；运行列仍不存在、17 CHECK、未发布，API/Vue和实盘恢复待验。[详情](qa/CONTAINER_ACL_RESTORE_PERSISTENCE.md)。

**2026-11-03 ACL历史实际PG审计已随08b2bb8页面整链验证**：严格轮2 List+8 Read的10条audit_events与真实actor/资源、唯一Trace、路径/实例hash及元数据version/digest匹配，无原ACL/plan或字节摘要。真实jsonb为对象，工具文本假设失败已修正；原失败与两轮原件保留。无新schema/缓存表/历史恢复意图，源与HDD同device不算异盘。[实际数据与剩余](qa/CONTAINER_ACL_HISTORY_REAL_UI.md)。

**2026-11-03 ACL历史浏览候选e83bad0，未部署**：实际HDD元数据继续作为权威，不新增SQL历史缓存、重复账户或schema迁移；读取审计复用audit_events及现有EF提交，两项固定action只留路径/实例hash与库存version或ACL摘要，不留ACL正文/原plan/key。冻结数组后再await审计，审计后仍重新授权；31新增HTTP案例使用合成审计，不补签真实PG审计或恢复持久意图。[候选与实际边界](qa/CONTAINER_ACL_HISTORY_WEB.md)。

**2026-11-03 ACL历史1.52安装态已通过真实签名RPC/HDD与独立核验**：宿主及原生探针冻结2ef06f0，唯一正常安装Invocation0552122d971a42f8a925b5455217fa89成功，Agent561098/维护561094及映射来源同源；应用仍0478262、11核心ID/镜像/StartedAt不变且healthy，无迁移/全栈发布或回滚。新独占9旧ACL操作留8份HDD，真实List10页/Read8份、29边界拒绝、源/Root/HDD全字节与身份保持、精确清理及独立Passed；实际只读unit仅DAC_READ_SEARCH/SYS_PTRACE与读租约目录可写。错Invocation拒绝/准确停止及stdin EOF两控制分项、旧所有者/两递归签名能力主与独立兼容通过；58 C#专项/8探针proof/构建语法通过，无新前端/Authentik页面。HDD同device非异盘，控制前置不冒充进行中RPC取消；历史业务/API/Vue/恢复及其余59项继续，整项完成率不提升。[真实来源、原件与未验边界](qa/CONTAINER_ACL_HISTORY_INSTALLED.md)。

**2026-11-03 ACL历史1.52签名RPC/SDK与受限只读传输候选6013aa1通过，未安装**：新增独立List/Read、全请求/方法/Subject/Trace与分页/捕获绑定、一次性短时能力；显式0/false存在位及严格输出校验，SDK必须minor≥52/专属能力，无写入降级。固定helper仅两读取动作，只有读租约目录可写、cap仅DAC_READ_SEARCH/SYS_PTRACE，安装/校验源码接入但生产未运行。冻结Release零警告错误/来源6013、4343 Unit/432 Integration（7默认原生跳过）、58新C#专项/28历史传输root/431旧文件/57维护/5安装预览通过，初编译/旧51断言/无匹配及收集器PATH失败保留。运行仍0478262/1.51、315250/315245及11核心healthy；无新实际RPC/HDD/业务/UI，不复用前批实盘补签。[候选、安全与后续](qa/CONTAINER_ACL_HISTORY_RPC.md)。

**2026-11-03 ACL历史只读内核候选818396e/891acf4通过，未安装/未恢复**：复用独立container-acl-v1真实HDD原计划，新增完整校验/预期摘要/当前容器路径绑定及有界版本分页，隐藏owner/raw plan/原inode与属主字段；无CLI/写入/恢复或权限扩展。15 Linux-root新专项及431旧容器文件回归通过；冻结候选实际新独占容器5捕获/5查询、7页/独立5HDD-Root及准确清理通过，11核心/宿主0478262/1.51与315250/315245不变，源与HDD同device不算跨盘。库存计数/版本全局，只有条目正文按CID路径过滤，不签多租户统计隔离；签名RPC/C#目录/管理员API/Vue及历史恢复仍待，不把root候选当人类页面验收。[来源、安全、实机分项与剩余](qa/CONTAINER_ACL_HISTORY_READER.md)。

**2026-11-03 0478262维护确认框修复已部署，真实Auth页面严格复验通过**：真实134fd25确认框实际是英文Cancel/OK（按钮在视口内），修复为中文取消/确认更新/确认回退、取消关闭正常返回及长计划滚动，14生产构建预览/993前端单元/类型构建lint通过；唯一标准覆盖Invocation8c8978188447476e9dd888cef34b1f79成功，六组件0478262/1.51，Agent315250/维护315245，current0478262/previous134fd25/pending空、11核心healthy/8数据库与配置sudo保持。QA37dbe58新账号真实管理员4版本卡/4预检取消、Agent页、普通5路403/两页隔离、两原上下文正常双端退出及6匿名401、11真PNG与独立指纹/准确清理通过，执行POST0；验收器错误role及早期失败原件保留，最终57离线门禁通过。未验安装态强杀/真实应用回退/物理冷启动/新文件HDD写入，P0-05/59项不升整项完成。[修复、真实页面与边界](qa/PLATFORM_MAINTENANCE_REAL_UI.md)。

**2026-11-03 P0-05新维护工具已安装，134fd25真实正常覆盖及权威历史通过**：七固定来源受控交接不重启原宿主/容器后，只启动一次标准update（Invocation9e1f5d033b5f4538843bcb7a76711663，12:55:03–12:59:31/268秒），六组件及实际进程映射/HTTP均134fd25/1.51，Agent253100/维护253095；真实账本current=134fd25、previous=306f646、pending=null，原错误previous字节留legacy，完成Root回执正确。11核心healthy/8数据库平台ID-镜像-StartedAt不变，配置/能力/sudo保持；47相关测试、安装前后validator、真实锁忙/Agent权限隔离及已安装维护二进制隔离状态验证通过。216实际维护HTTP样本均200但观察器超时空窗保留；**没有新Authentik/文件-HDD页面或物理冷启动/真实手动回退验收**。P0-05和59项仍部分实现，不自动回滚/重启。[安装、真实历史与未验范围](qa/PLATFORM_CONTROL_INSTALLATION.md)。

**2026-11-03 P0-05维护工具强杀恢复候选1176084/4eb4d15已验证，尚未安装**：新增root私有持久检查点、三固定工具完整核验/原子恢复、独立boot/timer与ExecStopPost，installer共用发布锁且手动恢复保留当前runner；恢复不执行Git/Docker/安装器或应用，不自动回滚。47相关测试、11构建回归、5安装CLI及语法通过；原样模块在两场景隔离systemd沙箱验证强杀退出钩子、锁忙跳过和影子/run丢失后恢复，最终11核心完整身份/StartedAt及两服务PID不变。**合成账本和影子目录不等于生产升级/恢复或物理冷启动验收**；生产仍306f646/1.51、Agent4135740/维护4135726，新工具/账本未安装或创建，旧错误记录保持，P0-05/59项不提升完成。[实现、原件与未验边界](qa/PLATFORM_CONTROL_RECOVERY.md)。

**2026-11-03 P0-05可信发布记录候选463e19e/1811bd3已验证，尚未安装**：不再把checkout HEAD当上一版，新增root私有原子账本，从六组件实际来源初始化；只在发布完成且实际来源一致时晋升，失败/重试/同版重发保留原记录，手动恢复选择可信记录且不降级数据库。旧previous保留为legacy证据，兼容文件不再是权威；状态原子写入且不跟随目标符号链接，前置拒绝不改已有健康状态。30新隔离测试、11冻结构建/来源回归、5安装CLI及语法通过；包含两个真实临时子进程强杀，不是生产恢复演练。实际只读六组件探测通过，运行仍306f646/1.51、Agent4135740/维护4135726及11核心healthy，生产账本未创建/工具未安装/原记录未改。真实升级、手动恢复强杀/冷启动控制入口及完整P0-05仍待闭环。[实现、失败与真实验收边界](qa/PLATFORM_REVISION_HISTORY.md)。

**2026-11-03 306f646覆盖及ACL旧版本预检拒绝已实机通过**：原唯一Invocation d38034cfaba24730ac1fe1acae325829在10:50:22成功结束（约1690秒），下载EOF/软件源超时后同次构建完成，无重复发布或回滚；六组件CLI/镜像/HTTP与实际宿主exe/DLL映射一致，Agent4135740/维护4135726、协议1.51，11核心healthy/8数据库不重建/无新迁移，配置和开发sudo保持。冻结2435d44/proofVersion1新资源只改准确FD的mtime，原Vue旧版本被真实预检拒绝：Failed/VERSION_CONFLICT、1规划Job/2审计，执行Job/Root/HDD均0，不能把null说成已修改或未修改的执行回执。15字段SQL/API、320暗色/1440亮色详情、5真PNG、正常双端退出、普通3路403/匿名2路401、独立核验及准确清理通过；89离线本轮通过。剩余执行摘要/共享写入/磁盘路径、ACL递归恢复/删除与59项继续，P0-05旧previous-revision缺陷未修。[部署、真实证据与范围](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

**2026-11-03 10:44标准覆盖发布进行中，尚未签收**：运行checkout已由user干净快进至306f646，仅启动一次`whaledeck-dev-publish-306f646.service`，Invocation`d38034cfaba24730ac1fe1acae325829`。宿主已为306f646（Agent4135740/维护4135726），API/Gateway仍447070e，11核心容器原ID/镜像/StartedAt保持且healthy，8数据库不变；私密配置与开发sudo摘要保持。容器镜像构建遭遇NuGet EOF与Ubuntu源超时，软件源安装已完成、NuGet仍重试；unit仍activating，运行中的Result=success/ExecMainStatus=0不等于发布成功。正确8080维护入口本段1052次采样均HTTP200/updating，未覆盖整个发布窗口。**无迁移/容器替换成功回执，新真实旧版本预检门禁未开始，不计部署或功能通过**。89离线边界/范围本轮重跑通过；目标59项不变。[唯一发布、原件和接续约束](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

**2026-11-03 ACL预检失败摘要候选caa9c64已验证，尚未部署**：修正planning/Failed误显示“执行失败”，改为“预检失败，未进入 ACL 修改执行”，执行失败仍保留原语义；版本冲突文案不再假定已完成预检。993前端单元、8生产预览浏览器、89相关离线门禁及类型/构建/lint/格式通过。新增独占文件FD仅改mtime→原Vue旧版本预检拒绝的真实门禁、无执行/Root/HDD断言与独立只读核验，已接CI；**新真实门禁未运行，测试接口/临时文件不能替代Authentik/实际Worker与磁盘整链**。运行仍447070e/1.51、Agent3947643/维护3947551及11核心healthy，无部署/迁移/服务重启；59主项和25%–35%粗估不变。[候选、失败与真实验收待办](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

**2026-11-03 ACL写后同CID实例变化已实机签收**：QA冻结d2ca69d/proofVersion4，产品仍447070e/1.51，无新增部署。仅重启本案容器一次，CID/镜像/绑定保持、PID4044343→4047348及StartedAt改变；旧计划/回执仍绑定旧实例、Root/SQL Unknown且aclConfirmed=null，实际已写ACL不回滚或续做。不同当前管理员真实320×600暗色页面先暂不处置再明确确认，两次任务摘要“未证明”完整入视口；跨容器目录四次自然延期[2,4,6,8]后同ID才发布。原Root/HDD/迟到Apply不变、3Job/7审计/9真实PNG、独立原件与准确清理、81相关门禁及11核心healthy通过。首轮Docker弃用提示导致断言失败和降级确认已保留，不计整体首轮通过；其他共享写入/磁盘/路径与摘要分支、ACL递归恢复/删除及59项继续。[实际失败、实例绑定与验收边界](qa/CONTAINER_ACL_INSTANCE_CHANGE.md)。

**2026-11-03 写后目标消失Unknown/处置摘要与447070e小屏修复已实机通过**：前两新资源在612f77c真实写后/Unknown/明确处置已发生，但Ack字段完整视口失败，原件与准确清理保留、不记通过。已补真实长错误码复现，447070e固定详情表格与长值换行并正常覆盖，六组件/HTTP/实际进程映射一致，Agent3947643/维护3947551、协议1.51，无新迁移。第三套全新资源严格门禁及独立复核通过：ACL已变后原容器移除，原Unknown/null/Root/HDD保持，另一管理员先暂不处置再确认，原Job Failed/Acknowledged，Unknown→Ack两任务摘要及“未证明”完整入视口，另一容器目录四次真实延期后同ID发布。75边界、27前端单元与8浏览器、9实际PNG/准确清理/11核心healthy通过；不记整体首轮通过。其他实例/磁盘/共享写入与摘要分支、ACL递归恢复/删除和59项继续。[失败、修复与真实证据](qa/CONTAINER_ACL_POST_UNKNOWN.md)。

**2026-11-03 ACL写后Agent重启及原任务摘要实机通过**：运行612f77c/1.51不重发，冻结47a1769/schema6的新独占资源在目标ACL已写/Root Applying且未回执时实际重启Agent；产品0.013秒停原helper，Agent3747527→3835700，维护3747520不变/153次HTTP200 healthy-Ready。原任务Succeeded/Completed、完整写后清单/1HDD与迟到同操作Apply不再变化；原预检Ready及执行Succeeded两份SQL/API摘要、320×600暗色真实详情和同上下文正常双端退出通过。独立原件/准确清理、48边界/范围与语法通过，11核心healthy。其余Unknown/实例/共享写入/摘要分支、递归恢复/删除与59项继续。[实机证据与界限](qa/CONTAINER_ACL_POST_AGENT_RESTART.md)。

**2026-11-03 612f77c已覆盖，ACL任务摘要真实常规整链通过**：标准update唯一Invocation689e10e7f8ba479ba2b0e97de386f33c成功，六组件/HTTP/实际宿主进程映射同源，11核心healthy、8数据库平台不重建；Agent3747527/维护3747520，协议1.51不变，无新迁移。冻结8c39d09的新独占资源九场景8完成/1预检后取消，17新摘要SQL/API/真实任务详情及1旧任务空态通过，原字节/两类ACL/xattr/8HDD、同上下文双端退出与准确清理通过。首轮独立观察器误读输出目录的FileNotFoundError保留，64f6d8b修正证据输入路径后只读复核通过，未重做真实写入。19边界/范围与语法通过；Unknown/处置等摘要分支及写后Agent/实例/跨写入剩余组合、ACL递归恢复/删除与59项继续。[实机证据与界限](qa/CONTAINER_ACL_JOB_RESULTS_REAL_UI.md)。

**2026-11-03 ACL任务安全摘要候选2491add/9b419c2**：新增预检/执行/Unknown/明确处置的15字段受限摘要，与任务/事件同事务保存；读取校验原意图/执行ID、阶段、状态和严格字段，未知/错误数据不原样返回。Vue展示真实三态、普通模式与历史回执，预检不冒充已修改，处置不冒充恢复；旧任务不补造历史，成功/失败空文案已区分。摘要源码候选与回归已验证，运行仍633a2ca/1.51，未部署、未签收真实身份/文件/HDD页面整链。[候选与验收边界](qa/CONTAINER_ACL_JOB_RESULTS.md)。

2026-11-03 ACL写后失权实机：8d93fbe原正快照余7163秒时移组/停用，约4.8秒停helper、原用户GET403及真实/forbidden或/login/私有引用清空；已发生ACL事实最终Succeeded/Completed，写后九字段/版本/迟到Apply保持、2HDD/4Job/8审计、独立原件与清理通过。不同当前管理员正常BFF只读GET200和HTTP双端退出通过，后续0b7d43c真实任务中心原执行ID/Succeeded只读页面与正常退出通过，管理写0/原SQL不变，完整结果摘要仍待。56边界/语法、6有效PNG、11核心healthy，运行仍633a2ca/1.51。[角色、历史与边界](qa/CONTAINER_ACL_POST_REVOCATION.md)。

2026-11-03 ACL Unknown严格复验：d977c7a新资源原容器准确消失，原页面正常退出后Unknown/null/原HDD与MAC/源保持；另一容器目录四次真实延期[2,4,6,8]，不同管理员小屏先暂不处置再唯一ACK，原Job Failed/Acknowledged、Root/HDD/迟到Apply仍Unknown，目录原ID才发布且Vue视口可见。52边界/语法、5 PNG、独立原件/清理及11核心healthy通过；首轮初始排队误计及视口不足已明确拒绝，非整体首轮通过。其他组合/递归恢复/删除继续。[实际失败与复验](qa/CONTAINER_ACL_UNKNOWN.md)。

2026-11-03 ACL写后实机：517c42a两新资源真实目标ACL已写、Root仍Applying时取消/Worker强杀，最终原任务Succeeded/Completed，未误报未修改；原字节/inode/属主/mtime/default ACL/xattr保持、实际新access ACL/ctime及写后九字段/版本在迟到原Apply前后不再改变。2HDD/4Job/9审计、42采样自然租约、原上下文退出/4 PNG/独立原件与准确清理通过；54边界/语法通过、11核心healthy，运行仍633a2ca/1.51。其他写后/Unknown/跨写入/递归恢复/删除继续。[写后事实与边界](qa/CONTAINER_ACL_POST_CHANGE.md)。

2026-11-03 ACL写入前重启实机：ed241e9两新资源Worker实际SIGKILL/原自然租约42采样和Agent实际重启/维护152采样均通过；产品0.116/0.015秒停helper、原任务Failed/Interrupted而Root Aborted，完整源/两类ACL/2HDD/4Job/8审计、原上下文退出及独立原件/清理通过。Agent PID3288068/维护3122598，运行仍633a2ca/1.51，11核心healthy。47边界/语法通过，写入后/Unknown/跨写入、ACL递归恢复/删除继续。[重启与实际租约](qa/CONTAINER_ACL_RESTARTS.md)。

2026-11-03 ACL原登录实时移组/停用实机：冻结f3ca007、运行633a2ca不重发；原正快照余7164秒以上，仅变更准确临时身份，产品约5.2秒停helper，旧任务403、原页面分别/forbidden和/login，引用键null、取消POST0。Canceled/Aborted、完整源/两类ACL/2HDD/4Job/8审计与独立原件/准确清理通过，四真实PNG/38边界与语法通过。仅写ACL前切点；重启/变更后/Unknown/跨写入、递归恢复/删除继续。[实际证据](qa/CONTAINER_ACL_REVOCATION.md)。

2026-11-03后续ACL故障实机：633a2ca不重发，78479ec两新独占资源在Preparing/HistoryVerified、尚未写ACL时真实页面取消/正常双端退出通过；原任务Canceled/Aborted、全源/两类ACL不变、2HDD/4Job/9审计、独立只读与准确清理通过，320×600暗色按钮在视口内。22离线与语法通过、未知登记/活动/Unknown失败清理保留源；11核心healthy，仅Worker正常启停。其余变更后/撤权/重启/Unknown/跨写入、ACL递归恢复和删除继续，不计完整权限。[证据与边界](qa/CONTAINER_ACL_FAULTS.md)。

2026-11-03 ACL实机更新：633a2ca已标准覆盖并应用生产迁移，0668215/21依赖的正常Authentik→Vue→SQL/Worker→Agent→实际ACL/HDD九场景及独立核验首轮通过。8成功/1预检取消、显式0/mask/default清空、原字节/inode/属主/另一ACL/子项、8HDD/35审计/17 Job、同上下文正常退出与实际普通/匿名拒绝通过。准确清理/11核心不变且healthy，开发sudo不改；不计异盘或完整ACL，进行中故障/Unknown/跨写入、递归/恢复、删除继续。[实际事实与边界](qa/CONTAINER_ACL_REAL_UI.md)。

2026-11-03 ACL API/Vue候选更新：062e361已接实际永久业务的管理员端点/完整条目与mask预览/原登录/明确确认/取消/原key恢复及当前管理员Unknown处置；严格0/缺字段/重复与三态原回执检查、旧写入互斥和退出提示清理保留。4272 Unit/432 Integration（7默认原生跳过）、972前端Unit及43+156两组生产预览通过，页脚越界修复；未部署，无真实Authentik/ACL文件/HDD业务整链，不把前批机器RPC和隔离PG拼接作本批真人验收。[候选与边界](qa/CONTAINER_ACL_WEB_UI.md)。

2026-11-03 ACL永久化候选更新：72d678e已接具体EF/迁移17个CHECK、原登录/原回执MAC与历史不可改、自然租约/CAS和十方双向保护；c9c9265补专用消息排除/通用重试拒绝。4272 Unit/364 Integration（7原生默认跳过）及双隔离真实PG完整门禁与独立清理通过。未部署/生产ACL表不存在，无管理员API/Vue、实际Worker或真实身份文件/HDD业务验收，原Root/身份/时钟为合成；不据此前批机器RPC拼接真人整链。[实现和边界](qa/CONTAINER_ACL_PERSISTENCE.md)。

2026-11-02后续实机：b1fafed正常覆盖已应用递归所有者migration，真实页面6意图/5执行/同原Subject23审计与5成功/1取消、5聚合20子项HDD对账，生产不再是“新表不存在”。原整套退出失败保留，独立补证/原事实回读与清理通过；不是全部故障或完整文件模块签收。[当前证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_REAL_UI.md)。

2026-11-02后续候选a19f5c4已将`ContainerRecursiveOwnershipIntent`加入PlatformDbContext并实现具体仓储与migration `20261102131222_ContainerRecursiveOwnershipIntents`：16个CHECK（含显式UID/GID范围）、双Job受限外键/唯一执行与租约/屏障/版本、原请求/计划/执行/开始与Root事实不可变、永久行删除/非空降级拒绝。九方数据库触发器及应用路径/预算双向保护已接；双隔离真实PG全套与独立清理通过。**生产新表仍不存在，尚未部署或接管理员API/Vue**。下方“尚无具体仓储/migration”等是前批事实，不按类或文档完成永久业务上线。[候选模型与实际边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_PERSISTENCE.md)。

2026-11-02内部业务候选7a94733新增Domain类`ContainerRecursiveOwnershipIntent`与请求/计划/执行/确认绑定模型。原始/目标/执行UID-GID分列，五计数、历史事实与当前观察分开；规范JSON与独立HMAC防止存储字段被篡改后取得执行权。**尚未加入PlatformDbContext、具体仓储或EF migration，没有新增生产表**；接口仅约定原子幂等/版本/租约及九方共享保护，测试Repository未注册为产品服务。下方此前“没有新增EF实体”等为原批次事实，不能据新Domain类宣称永久存储完成。[完整候选与后续约束](qa/CONTAINER_RECURSIVE_OWNERSHIP_WORKFLOW.md)。

2026-11-02递归所有者1.50候选新增强类型Command/Plan/Operation/Receipt DTO及独立protobuf：显式UID/GID presence、原公开版本、根九字段/父身份/执行身份、密封清单和五类计数/历史/当前观察进入完整签名。Root/HDD沿用独立ownership元数据域；**本批没有新增EF实体、migration或生产表**，永久意图/共享写入保护与Worker仍待接，不能套用下方已安装的recursive-permission实体计作ownership持久化。见[候选与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_RPC.md)。

2026-10-30后续设计（尚无migration/生产表）：新增`DM-AI-PROVIDER/CONNECTION/MODEL/CAPABILITY/PURPOSE-BINDING`与`DM-PET-ACTION-CATALOG/CUE/PLAYBACK-LEASE`，详见[字段与约束](AI_PROVIDERS_LIVE2D.md)。集中配置保存Secret引用、wireModelId/协议/能力与用途版本，生成冻结实际来源；动作绑定原generation/message/segment/pet/packVersion/序号/期限，客户端播放回报与服务端接受事实分列，多标签租约去重。稳定字段/外键/归属与并发需真实EF/PG约束，不持久化逐帧参数，NVMe/HDD恢复包含配置/模型包和必要状态。

2026-10-30明确追加（设计未建表）：酒馆C#重构的`DM-AI-*`与看板娘`DM-PET-*`见[实体与约束](AI_TAVERN_MASCOT.md)。覆盖角色/Persona/世界书/模板/连接、消息候选/分支/生成事件、附件/向量来源、酒馆范围内扩展版本/授权、宠物包/实例/持久睡眠及真实余额/额度快照；属于现有59项子计划，不是恢复通用插件市场或计费实体。Persona不等于人类身份，Authentik Subject归属/跨用户关系约束、Secret引用、原子版本/幂等/租约/有界事件和EF migration必须实现。NVMe对象与SQL/HDD备份需一致；Valkey/localStorage不承担永久会话真相，不增加本地密码表。

2026-10-21范围覆盖：只为[当前59项单工作站任务](SCOPE_SINGLE_WORKSTATION.md)建模，不新增节点/tenant/订阅/邮件域/DNS zone/虚拟机/插件市场实体。已有持久数据和迁移不破坏性回退；门户、空间/run、资源授权与审计继续保留Subject/资源归属、唯一约束和真实migration。下方10月13日扩展说明为历史，不能生成这些已移出业务的空表。

范围修订：2026-10-13 [P0–P5 基线](GOAL_P0_P5.md) 为新增网站/证书/运行环境、恢复/异地存储、邮件/DNS、通知渠道、Git/AI、节点/团队/订阅/配额/扩展记录增加建模工作。下方现有实体与旧预留仍是历史实现说明，新增任务实施时须补全实体、索引、版本/租户隔离/保留和 migration，不能只加 JSONB 占位；人类身份继续引用 Authentik，不创建平台本地密码表。

本文定义 WhaleDeck 自有 PostgreSQL 数据和 Valkey 临时数据。Authentik 是用户、凭据、MFA、资料和用户组的事实来源，不创建本地密码表或完整用户资料副本。

新增设计实体 `DM-DEV-WORKSPACE/ENVIRONMENT/RUNTIME/SOURCE/RUN/STEP/REPORT/SESSION/GRANT/ARTIFACT` 详见 [开发空间模型](DEVELOPMENT_WORKSPACES.md#8-数据库与存储模型)。一个空间对应多 runtime，报告绑定不可变源码/环境版本与执行记录；稳定字段关系化建模并用 EF migration 落地，Secret 只存引用。当前尚未新增这些生产表，不以文档或 JSONB 占位判为实现。

## 1. 设计约束

本轮UTF-8编辑复用现有`file_upload_operations`、共享路径锁、Job/Outbox和不可变`files.text-preview`/上传审计，没有schema变更或正文列。发布时续租沿用token及到期字段，不复活已过期owner；私有HDD数据/JSON与root回执绑定原UUID/版本和完整摘要，不是新增SQL备份历史表。审计详情只有目标哈希，不存文本、相对路径或HDD绝对路径；正文仅在单次请求和未提交浏览器内存草稿。完整历史管理/保留清理/恢复仍待实现，详见[当前证据](qa/FILE_TEXT_HDD.md)。

- PostgreSQL 保存平台配置、个人门户、资源注册、任务、审计、指标、告警、备份与同步历史。
- Valkey 保存可重建的权限快照、概览缓存、心跳、短期资源状态、锁和任务进度。
- 长期 Secret、OIDC Token、现有数据库密码、私钥、SQL Server 产品密钥和 Git 凭据不得进入 PostgreSQL、Valkey、审计或日志。数据库账号创建/轮换后生成的新密码只允许进入 5 分钟、单次消费的 Valkey Secret，读取后原子删除。
- 所有时间使用 UTC `timestamptz`，ID 默认使用 UUID。
- 每次 schema 变化使用 EF Core migration；生产环境禁止 `EnsureCreated`。
- JSONB 只用于结构变化频繁的快照/详情，不用来逃避稳定业务字段建模。

## 2. 已有实体

### `DM-CONTAINER-RECURSIVE-PERMISSION` `container_recursive_permission_intents`（源码候选，未部署）

2026-11-02新增真实EF migration `20261102044324_ContainerRecursivePermissionIntents`与独立事务仓储：原Subject/LoginBinding/幂等请求、不可变树计划及三层HMAC、两个OperationJob受限外键、90秒租约/版本、Root started/发布保护、原回执JSON、五类计数/outcomeDigest与HDD事实、明确处置人和终态。15个CHECK验证状态/期限/计数与计划/回执列一致，唯一租约/执行ID/发布屏障；原请求/计划/执行/已知事实/终态不可改、永久记录不可删除，非空拒绝降级。

Partial/Unknown保持未完成和保护且不自动Claim；明确处置释放保护时原Root状态/计数/历史仍保留、执行任务记失败。与旧七方共享41307341事务锁、八方数据库发布屏障、层级路径及64/每Actor4预算；观测字段和历史证明区分。该表尚未迁入运行数据库，原生验证进度及失败记录见[持久化候选](qa/CONTAINER_RECURSIVE_PERMISSIONS_PERSISTENCE.md)，不能据模型算真实身份/Worker/磁盘/UI已验收。

### `DM-TERMINAL-SESSION` `terminal_sessions`

2026-10-16新增EF migration `TerminalSessionLifecycle`：UUID、Authentik主体、当前登录的不透明SHA256绑定、UUID幂等键、连接票据哈希、固定宿主目标、Issued/Connecting/Active/Closed、创建/30秒票据期限/10分钟硬期限、实际Ready时间、租约/关闭时间/关闭码/退出码与并发版本。主体+登录绑定+幂等键唯一；活动租约和创建时间索引、目标/哈希/状态/期限约束由PostgreSQL校验。票据原文、服务器Cookie引用和终端正文不入表。

票据行锁消费、实际Ready激活和终止与不可变审计同事务；全局4/每主体2个待用或活动会话的创建预算由事务锁串行化。仅同主体同登录可查/续租/关闭，终态不覆盖。当前管理员宿主会话不是P3开发空间或P5租户会话模型；长期清理和更广故障恢复仍须补齐。实现及未验收项见 [BFF会话层](qa/HOST_TERMINAL_BFF.md)。

### `DM-AUDIT` `audit_events`

当前代码已有 `AuditEvent`。后续 migration 在保留历史数据的前提下扩展：

| 字段 | 类型 | 约束/说明 |
| --- | --- | --- |
| `id` | uuid | PK |
| `actor_subject` | varchar(256) | Authentik Subject 或系统主体；由现有 `actor_id` 演进 |
| `action` | varchar(128) | 稳定动作码 |
| `target_type` | varchar(64) | 容器、数据库、任务、设置等 |
| `target_id` | varchar(512) | 资源 ID；由现有 `target` 拆分/演进 |
| `result` | varchar(32) | `Succeeded/Failed/Denied/Accepted` |
| `source_ip` | inet | 可空，系统任务无来源 IP |
| `job_id` | uuid | 可空，关联异步任务 |
| `trace_id` | varchar(64) | HTTP/RPC 关联 |
| `detail_json` | jsonb | 只存脱敏摘要 |
| `occurred_at_utc` | timestamptz | 不可变 |

索引：`occurred_at_utc desc`、`actor_subject + occurred_at_utc`、`target_type + target_id + occurred_at_utc`、`job_id`。普通业务代码不提供更新和删除审计事件的方法。

### `DM-SETTING` `platform_settings`

当前代码已有 `PlatformSetting`。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `key` | varchar(256) | PK，使用分区命名如 `metrics.retention_days` |
| `value_json` | jsonb | 非敏感动态设置 |
| `version` | bigint | 并发控制，新增字段 |
| `updated_by_subject` | varchar(256) | 最后修改人，新增字段 |
| `updated_at_utc` | timestamptz | 最后修改时间 |

设置只保存 Secret 是否已配置和 Secret 引用 ID，不保存 Secret 值。

## 3. 门户与权限映射

### `DM-PORTAL` `portal_items`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | uuid | PK |
| `owner_subject` | varchar(256) | 个人项为 Authentik Subject；公共项为空 |
| `scope` | varchar(16) | `Personal/Public` |
| `name` | varchar(80) | 必填 |
| `description` | varchar(240) | 可空 |
| `url` | varchar(2048) | 仅 http/https |
| `icon_kind` | varchar(24) | `BuiltIn/RemoteImage` |
| `icon_value` | varchar(2048) | 图标 ID 或受控 URL |
| `color` | varchar(32) | 主题安全颜色 token |
| `sort_order` | integer | 用户范围内排序 |
| `is_enabled` | boolean | 默认 true |
| `version` | bigint | 乐观并发 |
| `created_at_utc`/`updated_at_utc` | timestamptz | 时间戳 |

约束：`Personal` 必须有 `owner_subject`，`Public` 必须为空。索引：`owner_subject + is_enabled + sort_order`、`scope + is_enabled + sort_order`。

### `DM-PUBLIC-PORTAL-PREF` `public_portal_preferences`

保存用户对公共门户项的隐藏偏好：`subject`、`portal_item_id`、`is_hidden`、`updated_at_utc`，联合主键为 `subject + portal_item_id`。

### `DM-ROLE-MAP` `role_mappings`

首期仅保存管理员组映射：`id`、`authentik_group_id`、`authentik_group_name_snapshot`、`role=Administrator`、`is_enabled`、`version`、`updated_at_utc`。组名快照只用于显示，授权优先使用稳定组 ID。

不创建 `users` 表。权限快照只进入 Valkey，最长 TTL 2 小时。

## 4. 资源注册与应用

### `DM-RESOURCE` `managed_resources`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | uuid | WhaleDeck 稳定资源 ID |
| `resource_type` | varchar(48) | Container/Image/Network/Volume/Database/Application/Host 等 |
| `external_id` | varchar(512) | Docker ID、实例注册 ID 等，不存任意路径 |
| `display_name` | varchar(256) | 显示名 |
| `protection_level` | varchar(32) | `ControlPlane/IdentityCore/CriticalData/Managed` |
| `labels_json` | jsonb | 规范化标签，不含 Secret |
| `source` | varchar(32) | Agent/Template/Discovery |
| `last_seen_at_utc` | timestamptz | 最近发现 |
| `version` | bigint | 并发控制 |

唯一约束：`resource_type + external_id`。对 `labels_json` 建 GIN 索引，对 `protection_level` 建普通索引。

### `DM-APP` `application_installations`

字段：`id`、`catalog_app_id`、`template_id`、`template_version`、`display_name`、`installed_version`、`desired_version`、`state`、`auto_update_enabled`、`config_summary_json`、`installed_by_subject`、`installed_at_utc`、`updated_at_utc`、`version`。

`config_summary_json` 只存非敏感参数与 Secret 引用。关联资源使用 `application_resources(application_id, resource_id, role)`。

### `DM-APP-UPDATE` `application_update_runs`

保存旧/新镜像摘要、版本策略、计划哈希、维护窗口、任务 ID、开始/结束时间、结果、是否回滚和脱敏错误摘要。

## 5. 异步任务与计划

### `DM-JOB` `operation_jobs`

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `id` | uuid | PK |
| `job_type` | varchar(64) | 类型化任务 |
| `actor_subject` | varchar(256) | 发起人或系统主体 |
| `resource_id` | uuid | 可空，关联资源 |
| `state` | varchar(32) | Queued/Running/Waiting/Succeeded/Failed/RollingBack/RolledBack/Canceled |
| `phase` | varchar(64) | 当前稳定阶段码 |
| `progress_percent` | smallint | 可空，0–100 |
| `idempotency_key` | varchar(128) | 写入任务必填 |
| `request_json` | jsonb | 脱敏且可重试的参数 |
| `result_json` | jsonb | 脱敏结果摘要 |
| `error_code` | varchar(64) | 稳定错误码 |
| `agent_operation_id` | uuid | 可空 |
| `cancel_requested_at_utc` | timestamptz | 可空 |
| `created/started/completed_at_utc` | timestamptz | 生命周期时间 |
| `version` | bigint | 并发控制 |

唯一索引：`actor_subject + idempotency_key`（有效窗口内）；索引：`state + created_at_utc`、`resource_id + created_at_utc`。

### `DM-JOB-EVENT` `operation_job_events`

追加式记录 `job_id`、`sequence`、`state`、`phase`、`progress_percent`、`message_code`、`detail_json` 和 `occurred_at_utc`。联合主键 `job_id + sequence`，用于断线恢复任务进度。

### `DM-SCHEDULE` `scheduled_tasks`

字段：`id`、`task_type`、`name`、`schedule_kind`、`schedule_expression`、`timezone`、`parameters_json`、`concurrency_policy`、`timeout_seconds`、`is_enabled`、`next_run_at_utc`、`created/updated_by_subject`、`version` 和时间戳。

`task_type` 是预定义枚举，`parameters_json` 按类型验证，禁止 Shell 字段。运行历史存入 `scheduled_task_runs(schedule_id, job_id, scheduled_for_utc, started_at_utc, completed_at_utc, result)`。

### `DM-OUTBOX` `outbox_messages`

保存同事务产生的后台事件：`id`、`message_type`、`payload_json`、`occurred_at_utc`、`available_at_utc`、`attempts`、`processed_at_utc` 和 `last_error_code`。Worker 使用租约处理，防止数据库提交成功但任务未发布。

### `DM-RESOURCE-LEASE` `resource_operation_leases`

持久化同一逻辑资源上的写操作互斥：`lock_key`、`job_id`、`owner_id`、`acquired_at_utc`、`lease_expires_at_utc` 和 `version`。`lock_key` 为操作区域与资源稳定标识的 SHA-256 派生值，不保存敏感连接信息。Worker 每 6 秒续租，租约 30 秒失效，任务进入终态时主动释放；Worker 异常退出后其他实例可接管。PostgreSQL 是资源锁的权威存储，Valkey 清空不会解除尚未过期的写操作互斥。

## 6. 指标与资源状态

### `DM-METRIC-SERIES` `metric_series`

字段：`id`、`resource_id`、`metric_kind`、`unit`、`dimensions_json`、`created_at_utc`、`last_seen_at_utc`。唯一约束为资源、指标类型与规范化维度组合。

### `DM-METRIC-SAMPLE` `metric_samples`

字段：`series_id`、`sampled_at_utc`、`value_double`、`quality`。联合主键 `series_id + sampled_at_utc`。

- 每 6 秒采集。
- 基于 `sampled_at_utc` 使用 UTC 日范围分区；`MetricDayPartitions` 迁移保留原键、数据与读权限，事务内逐行核对后替换旧堆表。
- 对时间使用 BRIN 索引，对 `series_id + sampled_at_utc desc` 使用 B-tree。
- 原始样本保留滚动 14 天：整日过期分区直接删除，跨越截止线的唯一边界日使用时间索引做局部清理，不对整个历史父表逐行删除。此边界处理保证精确截止线，与原文“完全不逐行”的字面描述不同；仅整日删除会额外保留不足一天的数据。
- Worker 启动与每小时维护当前/未来两天分区；真实范围、名称和归属校验失败时停止维护，函数仅用调用者权限，无 PUBLIC 执行权限，无任意表名参数。
- 采集失败不写 0，使用缺口或 `quality=Unavailable`，避免图表误判。

### `DM-METRIC-ROLLUP` `metric_rollups`

保存 1 分钟/1 小时窗口的 `min/max/avg/sum/first/last/sample_count`，用于长时间图表。首期同样保留 14 天；以后延长历史时只延长 rollup，不无限保留原始样本。

既有运行时为 UTC 日分区的 1 分钟 `minimum/maximum/average/sum/sample_count`。新版 `MetricAggregation` migration/源码增加完整小时、first/last 及采样时间、加权分子/分母、观察/质量计数；统计可空，不把无有效值写成 0。新增 `metric_aggregation_pending`（分钟键/递增 revision）、`metric_aggregation_state`（固定进度键/next hour）与 `metric_aggregation_windows`（小时键/完成截止）用于样本同事务标脏、14 天逐小时回填、并发 ACK 和重启接续。每轮最多两脏小时+一回填小时；分区保留不变。时间 BRIN 由独立 migration 和 EF snapshot 管理，父索引自动继承。实际原生验证/部署完成与否见 [聚合验收](qa/METRIC_AGGREGATION.md)，不能据模型文档判为上线。

即时资源状态不必每 6 秒全部写数据库；Worker 可以批量落库，Valkey 保存最近快照。

## 7. 备份

### `DM-BACKUP-POLICY` `backup_policies`

字段：`id`、`instance_resource_id`、`is_enabled`、`schedule_expression`、`timezone`、`retention_count`、`retention_days`、`target_directory_id`、`compression`、`verify_after_backup`、`capacity_warning_percent`、`capacity_critical_percent`、`version` 和更新时间。

`target_directory_id` 引用 Agent 注册目录，不保存任意绝对路径。

### `DM-BACKUP` `backup_records`

字段：`id`、`instance_resource_id`、`policy_id`、`job_id`、`status`、`relative_path`、`size_bytes`、`checksum_algorithm`、`checksum`、`started/completed_at_utc`、`verified_at_utc`、`expires_at_utc` 和 `error_code`。

恢复已纳入 P2-08；按专用模型/migration、目标覆盖计划、权限和隔离恢复验收推进。实现完成前不注册可调用入口，但旧 `HOLD` 不再是排除理由。

## 8. 告警

### `DM-ALERT-RULE` `alert_rules`

字段：`id`、`rule_type`、`resource_selector_json`、`threshold_json`、`evaluation_window_seconds`、`severity`、`is_enabled`、`version` 和更新时间。

规则类型包含 LoadAverage（`load.normalized`，按逻辑核心归一化，阈值 0–1000%）、SystemdHealth（仅登记服务/计时器）与 ContainerHealth（进程 State + Docker 原生 Status，区分健康、失败、初始化及无健康检查）。复用现有指标/资源/告警表；独立告警循环不依赖 Agent 在线。新增四个 load 指标沿用日分区和 14 天保留，不新增 schema。见 [负载与状态验收](qa/HEALTH_ALERTS.md)。

### `DM-ALERT` `alert_events`

字段：`id`、`rule_id`、`resource_id`、`fingerprint`、`state`、`severity`、`occurrence_count`、`first/last_occurred_at_utc`、`recovered_at_utc`、`acknowledged_by_subject`、`acknowledged_at_utc`、`silenced_until_utc`、`related_job_id`、`summary_code` 和 `detail_json`。

`fingerprint WHERE state <> 'Recovered'` 的 PostgreSQL 部分唯一索引保证未恢复告警去重（包含已确认/静默）；`version` 为乐观并发令牌，防止评估覆盖管理员操作。状态为 `Active/Recovered/Acknowledged/Silenced`，历史状态变化写入 `alert_event_history`。

利用率规则的 `threshold_json` 支持 `value/criticalValue/recoveryValue/minimumCoveragePercent` 与 `operator: >=`；`resource_selector_json` 支持明确的资源 UUID/外部 ID、指标 DeviceId（磁盘使用率为挂载点）。`detail_json` 保存当前均值、覆盖比例、观测时间和已观测/未知/规则失效原因，不保存任意诊断文本或凭据；未知不能判为恢复。见 [告警状态实现与验收](qa/MONITORING_ALERTS.md)。

## 9. 配置仓库与外部同步

### `DM-CONFIG-SNAPSHOT` `config_snapshots`

字段：`id`、`repository_id`、`branch`、`base_commit_sha`、`result_commit_sha`、`file_manifest_json`、`diff_summary_json`、`sensitive_scan_result`、`job_id`、`created_by_subject` 和时间戳。只保存文件相对路径和摘要，不保存 Secret 文件内容。

### `DM-CONFIG-SYNC` `config_sync_runs`

字段：`id`、`repository_id`、`direction`、`before/after_commit_sha`、`state`、`job_id`、`error_code`、开始/结束时间。仓库 URL 与本地目录来自宿主机注册配置，不从数据库动态接受任意值。

### `DM-EXTERNAL-SYNC` `external_sync_runs`

用于 Authentik/GitLab 同步历史。GitLab 已纳入 P4-07，具体表随对应功能与 migration 实施，避免用空模型冒称功能完成。

## 10. Valkey 键空间

Valkey 使用 `noeviction`，所有临时键必须设置 TTL，并对写失败进入数据库/Agent 降级路径。

| 键模式 | TTL | 用途 |
| --- | --- | --- |
| `whaledeck:authz:{subject}` | 最长 2 小时 | 账户状态、组哈希、管理员判断和权限版本 |
| `whaledeck:overview:public` | 12 秒 | 普通用户聚合首页 |
| `whaledeck:overview:admin` | 12 秒 | 管理员完整概览 |
| `whaledeck:agent:heartbeat` | 20 秒 | Agent 心跳与能力摘要 |
| `whaledeck:resource:{id}:state` | 30 秒 | 最近容器/数据库状态 |
| `whaledeck:job:{id}:progress` | 24 小时 | 实时任务进度，最终结果仍写 PostgreSQL |
| `whaledeck:secret:{subjectHash}:{token}` | 5 分钟 | 新生成凭据的一次性交付；`GETDEL` 原子读取后删除 |
| `whaledeck:catalog:xuanyuan` | 1 小时 | 热门应用缓存 |
| `whaledeck:ratelimit:*` | 按窗口 | 登录回调和高成本查询限流 |

Valkey 清空后系统必须能从 PostgreSQL、Agent 和外部服务重建，不得影响审计和任务最终状态。

## 11. 数据保留与清理

| 数据 | 默认保留 |
| --- | --- |
| 原始指标/聚合指标 | 14 天 |
| 任务事件 | 90 天，任务主记录按策略保留 |
| 告警历史 | 180 天 |
| 审计事件 | 默认 1 年，后续由管理员配置但不能被普通界面任意清空 |
| 配置同步记录 | 1 年 |
| 备份记录 | 至少覆盖备份文件生命周期，删除文件后仍保留审计摘要 |
| 开发报告/诊断工件（待实现） | 默认 14 天，可配置且受配额；源码不按报告 TTL 删除，会话票据短时一次性 |

清理任务按小批次或分区执行，产生一条汇总日志和审计，不逐行输出日志。

## 12. 用例追踪

| 数据模型 | 主要用例 |
| --- | --- |
| `DM-AUDIT` | 所有管理写操作、`UC-AUD-*` |
| `DM-SETTING`、`DM-ROLE-MAP` | `UC-SET-*`、`UC-AUTH-004/006` |
| `DM-PORTAL`、`DM-PUBLIC-PORTAL-PREF` | `UC-PORTAL-*`、`UC-OV-001` |
| `DM-RESOURCE` | `UC-CON-*`、`UC-DB-*`、`UC-APP-*`、权限保护 |
| `DM-APP`、`DM-APP-UPDATE` | `UC-APP-*` |
| `DM-JOB`、`DM-JOB-EVENT`、`DM-OUTBOX` | 所有异步用例、`UC-JOB-*` |
| `DM-SCHEDULE` | `UC-SCH-*`、`UC-APP-008` |
| `DM-METRIC-*` | `UC-OV-002`、`UC-HOST-001` |
| `DM-BACKUP-POLICY`、`DM-BACKUP` | `UC-BKP-*` |
| `DM-ALERT-*` | `UC-ALT-*` |
| `DM-CONFIG-*` | `UC-CFG-*` |
| `DM-DEV-*`（待实现） | `UC-DEV-001..010`，具体实体/约束以开发空间专用设计为准 |

## 消息中心增量模型（站内已签收，新快照与外部投递待签收）

- `notification_events`：不可变 typed 事实，唯一 SourceKey 与 bigint 自增 Sequence；来源告警历史/任务终态，与源事务同存。管理员广播或精确 Authentik subject 受众；正文由固定代码模板生成，不复制 JSON/错误/Secret。
- `notification_read_states`：复合主键 subject + notification_id，FK 级联删除。管理员也各自已读；无平台本地用户资料表。
- 新版 `notification_read_snapshots` / `notification_read_snapshot_items`：每主体最多一份 IsCurrent 的权限范围绑定短期固定 ID 集合，部分唯一索引、bigint ItemCount、成员处理标记与完成时刻。替换仅使旧头失效并过期，不同步级联删除旧成员；后台按成员总预算清理，再删空头。只保存主体/状态/关联 ID，不复制用户资料或正文；源事实到期仍级联读状态/成员。状态见 [专项证据](qa/NOTIFICATION_READ_SNAPSHOTS.md)。
- 新的投递基础 migration：`notification_channels`/`notification_routes` 版本化策略、`notification_dispatch_receipts` 逐事件同事务回执、`notification_deliveries` 事件/渠道唯一意图及 token 租约/终态。保存私有目标/凭据引用而非实际 Secret，不包含传输正文。基础源码已原生验证，未接传输 Worker；断线前发布启动、最终结果未知，不冒称已上线。见 [投递记录](NOTIFICATION_DELIVERY.md)。
- 默认 180 天有效，独立每分钟清理最多 1000 条过期事实；读状态随事件级联清理。详见 [消息模型与验收](NOTIFICATIONS.md)。

## 2026-10-27 容器历史与内容恢复模型

迁移 `20261027074344_ContainerFileRestorationSources` 增加 `container_file_versions`：捕获UUID、container_id/original_instance_id/relative_path、source_version/sha256/capture_hash、size_bytes/captured_at_unix_ms/different_source_device、availability/last_observed_at_utc/version。当前容器与捕获索引、字段/预算约束、并发版本；分页未返回不表示缺失，历史元数据不自动删除。没有文件字节、原Owner或宿主存储路径，Present不代表全量内容校验。

`container_file_upload_operations.history_source_json` nullable text保存严格canonical已批准来源并进入不可变MAC；不使用会规范化文本的jsonb。null在普通上传绑定及旧冻结快照中省略，旧MAC格式不变；非null为恢复工作流，HTTP接收入口拒绝。现有Job/Outbox/租约/Unknown/发布屏障复用，恢复job type与审计prefix独立。真实PG迁移及精确文本/MAC/事务、租约/Ready/取消测试已通过，Root与页面证据另见恢复QA。

2026-10-27：容器恢复的HistorySourceJson.Path记录原来源；ContainerFileUploadOperation.RelativePath记录发布目标，两者允许不同，但CID一致且均入MAC。旧原路径记录和普通上传序列化不变，本批没有schema变化。

## 容器复制永久意图（2026-10-28 候选）

`20261028083811_ContainerCopyIntents` 新建 `container_copy_intents`，规划 ID 同时作为原意图主键，确认后的 execution_job_id 独立绑定第二个 OperationJob；两外键均 Restrict。
原 subject/login/trace/key/owner、精确容器实例与源目标路径、规范 command_json、created/planning expiry 和 request_hash 不可变；完整 plan_json/plan_hash/Ready 期限生成后不可变；确认时间、执行 ID/期限/execution_hash 再以独立 MAC 冻结。
lease_token/lease_expires、Root 开始标记、publish_barrier、取消、结果、私有内容可能存在、接管原件和并发 version 组成状态机。原始字节、密码和登录 token 不进入本表。

状态为 Queued/Planning/Ready/Pending/Applying/Unknown/Succeeded/Superseded/Canceled/Failed/Expired/Acknowledged。十二项约束、唯一 actor/key、执行 ID、活动租约与发布保护索引，配合共享事务 gate 和四方发布触发器。
Unknown 不释放保护；永久意图不可删除，终态不可重写，包含历史的表不得降级删除。普通数据库备份自然包含该表。
参见 [SQL/恢复测试与未部署边界](qa/CONTAINER_COPY_PERSISTENCE.md)。
