# WhaleDeck Agent RPC 契约

2026-10-27 当前安装 Agent minor42：e6b5034六组件/HTTP来源一致、实际签名 List/ReadSource 与 Root/HDD完整2097159字节、实际独占容器重启后的旧捕获、MAC/重放/核心拒绝已通过。历史来源 RPC 只有内部读取，无公网恢复/发布入口；完整恢复任务继续。[实机证据](qa/CONTAINER_HISTORY_RPC.md)。下方“未安装/两小时观察”为各历史批次的记录。

- **2026-10-27 容器HDD历史校验基础候选**：独立v2容器历史域、字段/游标预算、存在与完整内容校验区分、绑定当前实例的旧捕获只读来源已实现。20新增Linux-root及最终216容器组通过；冻结引擎在真实HDD完整2MiB+7读回/实际独占容器重启后读取旧捕获、错误UUID/旧当前范围拒绝通过，源/沙箱精确清理。尚未安装或接RPC/目录SQL/恢复意图/API/UI，不宣称已恢复文件。运行时fab6e2e+72c72cc helper不变，两小时仍观察；继续独立签名协议与完整恢复链。[证据与剩余](qa/CONTAINER_FILE_HISTORY_ENGINE.md)。

- **2026-10-27 容器历史观察/永久封存候选，未安装**：minor41新增独立签名ObserveRecovery/SealRecovery，历史观察不进原namespace；原journal原子封存并完整保留原件/HDD，冻结minor40 reader与迟到writer也拒绝。2563 Unit/174 Integration通过（7原生夹具跳过）、180容器Linux-root通过；实际固定systemd profile完整2MiB+7、真实停止/重启、历史摘要/封存与当前实例迟到发送拒绝通过，临时资源清理/11核心healthy。没有安装候选或开放恢复SQL/API/UI；59项继续active。[证据与剩余](qa/CONTAINER_UPLOAD_RECOVERY_SEAL.md)。

**2026-10-26 首字节前取消已安装签收**：136b8a0/minor40保存独立Prevented拒收标记；a58a489真实签名UDS先取消/查询/重放，再发送合法首帧，精确CONTAINER_UPLOAD_CANCELED且零暂存载荷。完整2MiB+7发布读回/HDD旧原件仍通过，2441 Unit/160 Integration（6原生夹具跳过）、166容器Linux-root通过；本批载荷精确清理、11核心healthy。永久容器任务/共享写屏障、登录撤销/权限审计及BFF/UI仍待，59项继续。[证据和后续约束](qa/CONTAINER_FILE_CANCEL_FENCE.md)。

**2026-10-26 容器写入Agent已安装签收**：ac72b41/minor39与维护入口已覆盖，真实签名UDS五方法、完整2MiB+7发布/原只读RPC逐字节读回、旧inode/HDD原件、owner/请求体/MAC/重放拒绝与显式暂存取消通过。修复collect卸载竞态掩盖权限拒绝，8故障注入通过；Release零警告错误、2411 Unit/160 Integration通过（6原生夹具跳过）、154容器Linux-root通过。成功与两次失败现场精确核验清理，11核心healthy，应用容器未更新。永久任务/当前权限审计、BFF上传编辑/历史恢复/Unknown及真实UI仍待，59项继续。[完整证据和失败](qa/CONTAINER_FILE_WRITE_RPC.md)。

**2026-10-26 容器写入协议候选（未安装）**：Agent minor39独立五RPC与请求体绑定一次性签名、SDK/Agent/root三层完整流校验、固定受限helper已实现。Release零警告错误、2410 Unit/160 Integration通过（6原生夹具跳过）、138容器Linux-root通过；真实systemd profile完成2MiB+7全内容发布/HDD旧原件、精确InvocationID活任务取消和清理，11核心healthy。没有产品安装态、永久任务/当前权限/审计或BFF写入页面；59项继续active。[证据及边界](qa/CONTAINER_FILE_WRITE_RPC.md)。

2026-10-22 minor38已安装：fd3bda2宿主及独立签名UDS四方法通过，真实完整2097159字节及保护拒绝、systemd沙箱和清理已核对；43项Linux-root通过，最终本机2275 Unit/159 Integration通过（6明确跳过）。API/Worker/Gateway仍9ec8721，业务管理员/审计/BFF/UI未接，不能算完整功能。下方“未部署38”为前序候选历史。[安装态证据](qa/CONTAINER_FILE_READS_INSTALLED.md)。

2026-10-21新增未部署minor38：独立`docker.files.read`及`ContainerFileService` InspectTarget/List/Inspect/Download，严格容器实例/相对路径、各方法签名、完整内容哈希和helper成功EOF后end；原宿主传输profile不变。2263 Unit/159 Integration通过（6原生本机跳过），没有本次安装态或业务/UI验收；实际仍9ec8721/1.37。[候选及未完成边界](qa/CONTAINER_FILE_AGENT_RPC.md)。

2026-10-21 minor37指标连续性已部署：`host.metrics.replay`在现有`HostService.StreamMetrics`中提供单次有界原始批次重放。`replay_recent=true`仅允许`interval_seconds=0`，可选`after_sampled_at_utc`为exclusive原始游标；最多120批/65536点，每批`batch.complete`且RPC正常结束后才交付业务层。Worker独立摄取并在SQL提交后确认游标，不插值、不伪造停机数据。旧单批/持续流兼容；新客户端明确检查major1/minor>=37及独立能力，不静默退回丢点模式。9ba8b9e真实UDS停读20秒补取3批及420秒14序列每条70点/零缺批通过；当前六组件9ec8721来源一致，四历史窗口/80桶SQL比对及匿名/普通权限通过。窄屏和全部下拉交互仍待补验，[证据与边界](qa/TELEMETRY_CONTINUITY.md)。

2026-10-21范围覆盖：按[单工作站59项](SCOPE_SINGLE_WORKSTATION.md)实现，仅本机UDS，不新增远程mTLS节点协议、VM/邮件/DNS托管或插件RPC。既有能力/主版本协商、peer credentials、短时签名、固定helper、资源/路径白名单与核心保护保持；用户/空间授权不能因取消企业租户而省略。下方旧76项与候选版本记录为历史。

2026-10-21新增未部署清理业务材料：独立 Application 校验/签名确认/调度和 Gateway 已接；2124 Unit/146 Integration（6 本机原生跳过）、Release及旧维护快照并发10轮通过，修复候选包含同实例维护读取/发布协调。没有清理 SQL/迁移/Worker 注册/Controller/UI，不冒称永久任务已落库；[材料及失败账本](qa/FILE_TREE_PURGING_BUSINESS_MATERIAL.md)。实际 Agent/维护56101c8/1.36、API/Worker/Gateway2c8d1bb保持，完整76项继续。

2026-10-21实际安装56101c8/minor36：独立清理能力与固定helper通过宿主验证，6c60ea3签名UDS探针/独立磁盘观察器验证两类捕获、三范围、2MiB+7/1GiB+7完整内容、6完成/6取消及精确清理；81精确路径另行确认消失，5私有证据保留。源/归档同HDD，不冒称异盘/安装态强杀或BFF/SQL/人类权限/UI。API/Worker/Gateway仍2c8d1bb；[完整证据](qa/FILE_TREE_PURGING_INSTALLED.md)。清理业务与全部76项继续；以下“未安装minor36”为候选历史。

2026-10-21新增未安装minor36候选：`FileTreePurgingService` Plan/Apply/Inspect/Discard，独立`files.tree-purge-intents`、三范围、严格6/18/12字段、uint64身份和删除事实校验。固定helper/可信loader、精确默认授权升级及测试已接；Release零警告错误、1856 Unit/146 Integration（6本机原生跳过）、冻结Linux清理30/文件184通过。生产仍2c8d1bb/1.35；尚无清理BFF/Worker/SQL/UI或安装态验收。[边界与证据](qa/FILE_TREE_PURGING_AGENT.md)。以下运行版本及候选条目保留各自历史背景。

最新实际2c8d1bb/minor35：新内容证明字段/专用能力已安装并通过同源码签名UDS与完整2MiB+7/1GiB+7独立磁盘、真实身份BFF/Worker/SQL和Overwrite归档实际复用。六组件/HTTP来源及健康独立核对，未增加Agent TCP或绕过原inode树证明。网页交互未签收，[完整边界](qa/FILE_TREE_OVERWRITE_CONTENT_PROOF.md)。下方“1.35未安装”为前序候选，完整76项继续。

2026-10-21未安装修复候选：minor35及`files.tree-overwrite-restore-content-proof`，覆盖计划字段16 `target_content_sha256` 独立于带inode的 `target_tree_sha256`；二者都进入严格计划指纹，Verified目标归档/目录签名改用正确内容域。Gateway新方法要求35和两项能力，不回退旧计划。旧root回执只读可查，不重新执行缺新证明计划。[验证及旧混用问题](qa/FILE_TREE_OVERWRITE_CONTENT_PROOF.md)。实际仍1ea9de6/1.34，完整76项继续。

- **2026-10-20专用覆盖恢复安装态子门禁通过**：57877a9客户端/独立观察器对已装6a6f8e2（1.34）完成签名UDS/固定helper及2MiB+7、1GiB+7完整双原件/新输出/归档manifest校验；新自有夹具与回执精确清理，旧失败现场保留。回收新私有目录按durable retainedIdentity核验，不错误要求沿用原目录inode；原文件与被覆盖目标inode要求保持。5项真实Linux root观察器回归通过。文件源/备份同HDD，不宣称异盘；不包含新BFF/Worker/人类审计/网页或中断交换及Overwrite捕获复用验收。[证据](qa/FILE_TREE_OVERWRITE_RESTORING_UI.md)。下方“未执行/正在运行”为历史，候选业务未部署，完整76项继续。

- **2026-10-20覆盖恢复WebUI候选 f35ee05**：独立3字段登记、当前目录版本冻结、计划/执行双任务、by-key与丢确认只查、取消/Unknown二次确认、覆盖前备份事实及第10类接管提示已接通；历史Trash/Overwrite严格类型均可复用。364前端单元、类型/生产构建/lint/格式通过，主代理Chrome生产预览88/88通过（含320/1045/1440明暗、普通拒绝页就绪后0文件请求）。一次DEV演示身份测试环境不匹配已保留、未削弱断言；[证据与未完成边界](qa/FILE_TREE_OVERWRITE_RESTORING_UI.md)。独立观察器3项真实Linux root范围/清理测试通过，Linux客户端已发布，安装态专用签名UDS/完整磁盘核验正在观察原句柄；未部署候选，真实身份BFF/页面与全76项仍active。实际Agent/MaintenanceHost/Gateway6a6f8e2、API/Worker18f38bb保持；以下为前序历史。

- **2026-10-20覆盖恢复管理员接管/Worker/API候选 d50b765**：第10类现任管理员Inspect/Discard对账、原身份冻结、备份证明单调、原子目录登记/审计/Job/Outbox/锁释放，以及独立HTTP/Worker/取消分流已接通；恢复kind字段扩至32并新增防截断降级保护迁移。Release零警告错误、1738 Unit/146 Integration通过（6本机原生明确跳过）；冻结d50b765工作站隔离真实PG1执行/1通过，临时DB/容器已清理并独立复核、11核心healthy。[证据及未完成边界](qa/FILE_TREE_OVERWRITE_ADMINISTRATOR_RECOVERY.md)。尚未部署：覆盖恢复UI、安装态专用签名UDS/独立磁盘证明、真实身份BFF/页面验收继续；实际Agent/MaintenanceHost/Gateway6a6f8e2、API/Worker18f38bb。完整76项仍active；以下为前序历史。

- **2026-10-20覆盖恢复持久化候选**：SQL仓储、永久双任务/Outbox/共享锁/租约、不可降级的覆盖前备份目录及Trash/Overwrite签名兼容已实现。Release零警告错误、1700 Unit/144 Integration通过（6本机原生跳过）；冻结62cd5dc在工作站隔离真实PG实际1执行/1通过、旧schema升级与审计回滚/取消/Unknown/跨Kind重用通过，临时容器清理独立确认、11核心healthy。前五次夹具/JSON查询失败均保留并修正后完整重验。[证据及未完成边界](qa/FILE_TREE_OVERWRITE_RESTORING_PERSISTENCE.md)。尚未部署：当前管理员对账、Worker/API/UI与安装态UDS/BFF/页面继续；实际Agent/MaintenanceHost/Gateway6a6f8e2，API/Worker18f38bb，完整76项仍active。以下为对应历史。

- **2026-10-20恢复在线后**：11核心healthy、Agent/维护服务重启0、WD live200。Authentik品牌最终验收通过：320/390/740短窗口/1440明暗无横向溢出，修复系统主题切换背景回退及语言控件遮Logo；独立匿名背景全SHA吻合，真实密码/OIDC/双端正常退出与临时身份清理通过。见[最终证据](qa/DISK_CAPACITY_AUTHENTIK_BRANDING.md)。新增覆盖恢复Application绑定/服务/调度与Gateway候选，1678 Unit/143 Integration通过（6本机原生跳过）、183项新纯测试；Windows原子快照并发问题也已修复并回归。尚无覆盖恢复SQL仓储/迁移/Worker/API/UI，未部署候选；见[业务材料边界](qa/FILE_TREE_OVERWRITE_RESTORING_MATERIAL.md)。实际Agent/MaintenanceHost/Gateway仍6a6f8e2，API/Worker18f38bb；完整76项继续active。以下离线/旧候选状态是对应历史。

新增**未安装RPC1.34源码候选**：独立 `FileTreeOverwriteRestoringService` 四方法/专用capability、目标版本/双树证明、覆盖前归档回执、固定helper/可信模块及精确默认根升级。1463 Unit/143 Integration（6本机原生跳过）、27覆盖恢复/驱动与23根升级实际Linux通过；[完整边界](qa/FILE_TREE_OVERWRITE_RESTORING_AGENT.md)。安装态签名UDS、业务永久任务/API/UI及覆盖发布仍缺，实际18f38bb/1.33保持，全部76项继续；以下旧候选/实际版本为历史。

新增未安装 **RPC1.33 源码候选**：独立 `FileTreeRestoringService` Plan/Apply/Inspect/Discard 和 `files.tree-restore-intents`，固定 helper/可信恢复模块与精确默认授权升级已接通。后续66451f7已接Application永久双任务/锁/Worker/事务审计/第九类处置/API，1383 Unit/143 Integration（6本机原生跳过）及冻结实际PG通过；[业务证据](qa/FILE_TREE_RESTORING_BFF.md)。新真实UDS探针已编译但未执行，恢复WebUI/安装态/身份验收与覆盖恢复/清理及全部76项继续。实际e0175a0/RPC1.32不变；[Agent边界](qa/FILE_TREE_RESTORING_AGENT.md)。下方未安装1.32/1.31和业务未接说明为历史。

新增目录回收/HDD恢复仍仅内部源码：独立tree-recycling/restore计划和回执、HDD先验收后逐文件隔离及只观察恢复已通过Linux验证；`files-tree-trash`与`files-tree-restore`只允许被注册表识别，不自动升级默认根或开放CLI/Agent/HTTP。已有目录移动新增父路径/私有目录inode复核候选尚未安装；后续应完成固定源码CI和实际移动链路回归。当前仍adc6cae/RPC1.31，全部76项继续。

2026-10-20实际adc6cae仍为RPC1.31：目录位置恢复已完成覆盖发布/真实身份签收，完整解压的已安装证据见[业务](qa/FILE_TREE_UNPACKING_BFF.md)和[页面](qa/FILE_TREE_UNPACKING_UI.md)。新增HDD目录恢复仅内部引擎源码与隔离Linux验证；`files-tree-restore`仅由内部注册表识别，默认根未升级，没有新protobuf/固定helper/Agent方法/公开HTTP入口。不能把已安装的普通解压RPC冒充目录历史恢复；后续需独立资源授权、任务/审计与新协议签收，全部76项继续。下方未安装记录为前序历史。

新增解压BFF业务源码 e365f4c 尚未安装：持久双任务/共享锁/Worker/当前管理员恢复/API及不可变输入证明已实现，并通过同源码实际隔离PG。独立解压WebUI的281前端单元/127联合浏览器及另1解压接管专项、交互/视觉已验证；真实身份BFF/安装态页面尚未签收。Agent协议仍1.31，不改变下列73513a4安装态证据。详见[业务边界](qa/FILE_TREE_UNPACKING_BFF.md)及[页面](qa/FILE_TREE_UNPACKING_UI.md)。全部76项继续。

最新实际 **73513a4 / RPC1.31** 已签收独立解压Agent子范围：四CI/同源码工作站原生PG及实际签名UDS/helper，PAX/GNU两格式四组合与1GiB+7全量新目录/独立磁盘、nonce/版本/计划/取消及精确清理通过。客户端bc8037d仅validation/docs修正，与产品差异0；不借客户端SHA冒称重建运行时。7类旧文件、OIDC/资产/宿主及11核心健康通过，见[最终账本和首次失败](qa/FILE_TREE_UNPACKING_AGENT.md)。解压业务任务/Worker/API/WebUI仍缺，绑定材料源码已新增但未部署；完整76项继续。下方候选/未安装为历史。

新增 **1.31源码候选，尚未安装**：独立 `FileTreeUnpackingService` 的 Plan/Apply/Inspect/Discard 与 `files.tree-unpack-intents`；完整输入归档/内容证明、格式/登记根/源版本/目标父inode及策略绑定，回执中输入SHA/长度在所有终态保留。固定helper动作与可信模块、只对精确旧默认升级的显式根授权已接通。1196 Unit/137 Integration、153原生树/15根升级通过；6本机PG跳过，真实安装态UDS/BFF/Worker/UI仍缺，不能当作用户已可解压；见[候选边界](qa/FILE_TREE_UNPACKING_AGENT.md)。线上仍abef670/1.30，全部76项继续。下方为对应历史。

最新实际运行时 **abef670 / RPC1.30**：目录压缩WebUI已覆盖并通过真实身份、两格式2MiB+7源/完整原生下载及独立Root/磁盘/成员校验；四CI、工作站冻结PG、六类旧文件与宿主回归通过。协议不变；新的解压RPC/任务/页面仍缺，不把内部codec当作产品入口，见[网页签收及边界](qa/FILE_TREE_PACKING_UI.md)。完整76项继续，下方旧安装态均为对应阶段历史。

当前安装态 **0661051 / RPC1.30**：压缩业务层持久双 Job、共享锁、Worker、人类审计与回执专用 HTTP 已接通，并通过真实身份/已安装UDS/helper、两格式和超过1GiB完整流/独立全SHA验收；四CI与冻结实际PG、六类旧文件/OIDC回归通过。协议不变，普通文件下载仍限1GiB。压缩操作WebUI、解压和目录回收恢复仍缺，完整76项保持active；见[业务后端最终证据](qa/FILE_TREE_PACKING_BFF.md)。下方“BFF/HTTP仍缺/未安装”是历史，不代表当前状态。

最新安装态 **8aa73c0 / RPC1.30**：独立目录压缩与回执绑定大归档下载已通过真实签名UDS/固定helper、两格式2MiB+7及原生TAR超过1GiB完整流/独立磁盘验证，四CI与冻结实际PG、原六类文件BFF/OIDC回归及11核心健康通过。BFF/持久任务/Worker/人类审计/HTTP/压缩WebUI仍缺，不据此签完整目录压缩或76项；见[最终验收](qa/FILE_TREE_PACKING_AGENT.md)。下方“候选/尚未安装”为历史。

新增 **1.30候选，尚未安装**：独立`FileTreePackingService`的Plan/Apply/Inspect/Discard及回执绑定Download，能力`files.tree-pack-intents`/`files.tree-pack-download`。TAR/TAR.GZ格式、源版本/全manifest摘要、双方策略、目标父inode和计划指纹绑定；完整manifest不跨64KiB私有帧。来源要求list/download/archive/tree-pack，目标mutate/upload/archive/tree-pack；只新建目标，不覆盖、不删源。下载仅已完成操作/原owner，重核当前目标策略、原inode/版本/整包摘要，32KiB块、17GiB总上限、600秒/取消和最终完整性核对；不扩大旧1GiB普通下载。下载不占全局写门，只持本操作锁，目标并发变化失败关闭。独立方法capability保持60秒、一次nonce及UDS peer guard，无TCP Agent端口；BFF/Worker的人类权限、持久任务、共享层级锁/审计/API/UI仍待接通。默认根新增files-tree-pack，只精确升级未修改1.29默认并保留`.pre-tree-pack.json`，自定义不扩权。见[候选证据](qa/FILE_TREE_PACKING_AGENT.md)。当前实机仍2a60668/RPC1.29，下方为历史。

最新已安装协议 **1.29 / 5e131e9**：正文搜索已通过同源码CI/实际PG、真实身份API→安装态UDS/helper与管理员网页签收，六组件实际来源和HTTP对应。独立普通用户403、匿名401、CSRF/Origin、完整2MiB+7/原inode及元数据证明、精确清理见[最终证据](qa/FILE_CONTENT_SEARCH.md)。本批仅增加只读正文搜索，不开放任意宿主路径/命令，不签完整文件管理或76项；下方候选/未安装文字为历史。

新增1.29候选（尚未部署）：`FileTransferService.SearchContent`、能力`files.utf8-content-search`，与文件名Search分离。独立请求包含登记根/目录、字面UTF-8关键词、大小写、游标、take及1–16MiB单文件上限；结果为每文件首个匹配行/有界片段、原文件版本、实际读取字节/文件及跳过计数。最多10000项/64MiB/5秒一页、32层、100条、64KiB私有帧；helper另有20秒systemd上限/384MiB内存/单核CPU，不提供任意路径或命令。游标MAC绑定ContentUtf8V1域、根/目录/关键词/大小写/文件上限/祖先版本与10分钟期限。必须同时具备同一登记快照的files-list和files-download；排除保护配置、链接、多硬链接、特殊文件及嵌套挂载，严格UTF-8和全文件版本验证，不把前缀或跳过项当作已查全文。API读前读后复核当前管理员；片段/关键词不写Job、审计正文、日志或URL。完整证据及未验收边界见[正文搜索](qa/FILE_CONTENT_SEARCH.md)。

范围修订：2026-10-13 [P0–P5 基线](GOAL_P0_P5.md) 增加受控终端会话/文件/恢复/网络/网站/邮件/DNS/多节点适配。源码1.17通知私有配置、1.18宿主PTY基础，保留本地UDS限制，不得直接放宽成任意路径/可执行程序RPC；新增能力须独立设计、能力协商和安全测试。P5远程Agent采用专用mTLS通道，本地UDS不暴露TCP；远程写入须同时验证节点/资源/主体/短时capability。

本文定义 `WhaleDeck.Api/Worker` 与宿主机 `WhaleDeck.Agent` 的内部契约。Agent 是唯一可以访问 Docker Engine、systemd、journald 和批准宿主机资源的组件。

开发空间拟新增专用 `RPC-DEV-001..007`，只处理登记空间/环境/run/session ID，按能力协商启用。代码执行和调试发生在隔离空间内，不新增任意宿主命令/路径接口；调试适配器与 IDE 不持有 Docker/Agent Socket。契约与数据流见 [开发空间 Agent 契约](DEVELOPMENT_WORKSPACES.md#6-agentworker-契约)。此处是待实现设计，**源码协议 1.25 尚未包含开发空间能力**，登记开发文件根不代表空间运行时已实现。

## 1. 传输与版本

最新运行时 **1399563/Agent1.28** 移动WebUI及真实身份/安装态链路已经签收，见[最终证据](qa/FILE_TREE_MOVE_UI.md)。协议仍1.28。下方“WebUI仅本地/未安装”为对应历史，不覆盖本记录；完整文件管理与原登录丢失后的当前管理员处置仍待继续。

最新上层运行时 **39cb274/Agent1.28**：移动异步BFF/永久双任务/共享锁/Worker与人类审计已通过实际身份/SQL/安装态UDS验收，见[后端证据](qa/FILE_TREE_MOVE_BFF.md)。移动WebUI仅本地候选，未生产签收；协议仍1.28，不把下方历史“尚未接通/未安装”当作当前状态。

**最新已安装1.28 / 4fd9ba7**：独立移动RPC/固定helper/显式根策略/旧默认备份，以及真实UDS的2MiB+7、1GiB+7、完整字节/原inode保留/取消栅栏/清理已通过。源码四CI、冻结实际PG、真实更新unit终态/安装SHA/权限见[安装态签收](qa/FILE_TREE_MOVE_AGENT.md)。移动BFF/Worker/UI仍未接通；下方“候选/1.27”为历史，不代表当前Agent。

2026-10-18候选协议**1.28，尚未安装**：新增独立`FileTreeMoveService.Plan/Apply/Inspect/Discard`与`files.tree-move-intents`。固定helper枚举`tree-move-*`及源/目标显式`files-tree-move`根授权；复制域不授予移动权限。只传既有copy摘要+源父UInt64身份+外层fingerprint，不传完整清单/任意路径/UID；回执新增`destinationPublished/sourceRemoved/sourceRetained/partialRetained`，未知结果不重新Apply。旧默认根策略精确升级，自定义保持；验证和边界见[Agent候选证据](qa/FILE_TREE_MOVE_AGENT.md)。BFF/Worker/UI尚未实现，以下1.27仍是当前线上。

最新上层运行时 **6c5b1d6/Agent1.27**：独立目录复制异步BFF/双任务/共享锁owner/Worker与WebUI已实机签收；原登录计划、刷新只读恢复、暂态Unknown保锁/不重新Apply及真实管理员/普通用户链路见[最终证据](qa/FILE_TREE_COPY_UI.md)。协议版本不变；下方“候选/未接通”为当时历史状态，不代表当前运行时。

新增上层候选（尚未发布，不变更Agent1.27协议）：目录复制采用异步BFF计划/永久双任务/确认/Worker，不在HTTP请求中等待扫描；原登录绑定登记键只通过GET恢复。Root开始后若传输失败，BFF持久Unknown并保留共享路径锁，后续只Inspect/Discard原执行ID，禁止重新Apply。源码2ece1c1及后续WebUI证据见[目录操作](DIRECTORY_TREE_OPERATIONS.md)和[页面验收边界](qa/FILE_TREE_COPY_UI.md)；运行时仍以下方e0efb8e为准。

**已安装1.27，运行时e0efb8e**：独立`FileTreeCopyService.Plan/Apply/Inspect/Discard`和`files.tree-copy-intents`能力。只传登记源/目标、原版本、有界计划摘要/父目录UInt64身份与回执，不传16MiB完整清单或任意宿主路径/UID/命令；旧普通文件计划与签名JSON不变。新增固定tree-copy枚举与独立`files-tree-copy`根授权，仅精确旧默认配置自动升级，自定义/收紧策略保持；新树操作独立16GiB/384MiB/128FD/1CPU预算，不放宽旧1GiB上传。2MiB+7与1GiB+7的安装态SDK/UDS/helper复制、完整字节/元数据/回执、方法与nonce、owner/版本/取消栅栏及清理实际通过。BFF异步计划、持久目录任务与人类审计/UI尚未接通，不能在HTTP请求内直接等待整树散列。见[安装态验收和失败记录](qa/FILE_TREE_COPY_AGENT.md)，全部76项继续。

2026-10-17最新运行时6dd60dd/Agent1.26：FileArchiveService已安装，普通文件历史/回收恢复及共享层级锁已通过实机链路；下方“1.26尚未安装”为历史。目录完整清单/复制内部引擎尚未安装或注册独立RPC，开发空间仍未实现，不可从现有FileRoot推断新能力。见[最新签收](qa/FILE_HIERARCHY_LOCKS.md)。

源码扩展1.26（尚未安装）：专用`FileArchiveService.List/Plan/Apply/Inspect/Discard`，能力`files.archive-intents`；与旧上传/变更wire和永久签名JSON分离。固定helper仅增加archive-list/plan/apply/status/discard枚举，模块root所有/单链接/不可组或其他用户写；历史页50返回/500扫描/有界JSON，Exact UInt64身份/纳秒/完整SHA和HDD证明逐层验证。只接受登记根、相对路径和版本/操作UUID，不接受HDD绝对路径、任意UID或命令。新目录权仅对精确原始/1.25默认注册表升级，任何自定义/收紧策略保持。源码及实际证据见[文件历史与回收站](FILE_HISTORY_RECYCLE.md)，不能将协议/SQL测试当作安装态或UI通过。

2026-10-17实际运行时931a24c/Agent1.25已经签收UTF-8预览编辑、版本冻结确认保存及覆盖统一完整HDD前置备份，见[最终证据](qa/FILE_TEXT_HDD.md#最终运行时签收931a24c--agent125)。原文件变更及1.24有界目录树/名称搜索保持，下面旧未部署说明为历史；目录树/搜索仍依赖files-list授权，不扩大到任意路径或正文搜索。历史管理/恢复、开发空间、目录复制移动及完整文件管理仍待继续。

- 协议：gRPC，包名 `whaledeck.agent.v1`。
- 传输：Unix Domain Socket `/run/whaledeck/agent.sock`，不监听 TCP。
- Socket：`whaledeck-agent:whaledeck`、`0660`，由非 root 的 Agent 服务创建；仅 Agent 与 API/Worker 的专用宿主机组可以连接。能力密钥与注册表仍由 root 所有且 Agent 只读。
- Agent 使用 Unix peer credentials 校验调用进程，并验证短时签名能力声明。
- `GetCapabilities` 返回 Agent 版本、协议版本和方法能力；未知主版本直接拒绝，次版本按能力协商。
- 浏览器不得连接 Agent。宿主机稳定入口只读取 Agent 原子写入的脱敏维护快照。
- 当前源码协议版本为 `1.25`，新增 `files.hdd-versions`（源码实现，部署证据见下方专项账本）；`1.24` 为 `files.bounded-browser` 与目录树/名称搜索；`1.23` 增加 `files.mutation-tombstones` 与独立 Discard 方法；`1.22` 新增 `files.registered-mutations`；`1.21` 为 `files.staged-publication`，`1.20` 为 `files.registered-streams`，`1.19` 为 `docker.terminal.exec`，`1.18` 为 `host.terminal.pty`，`1.17` 为 `notifications.private-configuration`。历史 `1.4` 增加普通 Docker 网络与孤立卷管理，`1.5` 增加有界 Docker 事件缓冲与时间线能力 `docker.events`，`1.6` 将数据库、备份、Compose、主机与平台维护预检分流到 `ManagedResourceService.PlanAction`，`1.7` 增加只读进程能力 `host.processes`，`1.8` 扩展容器摘要（`docker.container-summary`），`1.9` 增加 `host.disk-io`，`1.10` 增加 `docker.usage`，`1.11` 增加兼容只读字段 `docker.relationships`，`1.12` 增加 `applications.configuration`，`1.13` 增加 `applications.backups`，`1.14` 增加 `applications.backup-retention`，`1.15` 增加 `backups.capacity`，`1.16` 增加 `host.load-average`；旧次版本按能力协商并安全降级。

### 覆盖前完整 HDD 旧版本（1.25）

`FileUploadReceipt.hdd_backup` 为可选强类型 `FileHddBackupProof`：操作 UUID、批准存储 ID、旧源版本、大小、完整 SHA256、UTC 毫秒和是否不同设备。新建文件没有前序证明；覆盖必须有原操作及原源版本对应证明。`result_code` 只允许有界 `FILES_BACKUP_*` 安全错误码，不透传路径/正文或原始异常。

API/Worker 的暂存与发布写入口要求 `files.hdd-versions`，旧 Agent 不静默降级。直接上传与暂存发布在同一 namespace 锁内、原子交换前，共用已钉住物理 HDD 身份/空间/权限的 root 模块。完整复制、fsync、SHA256复读与私有回执均先于发布；失败保持源内容，不确定结果只观察恢复。Worker 每6秒续原90秒租约及检查取消/身份/期限，发布最多10分钟；丢租约/撤权/取消/故障停止同一 invocation，已提交不撤销。见[实现与验收状态](qa/FILE_TEXT_HDD.md)；此能力不包含历史列表/恢复/自动清理或容器文件。

### 登记文件变更（1.22）

独立`FileOperationService.Plan/Apply/Inspect`，每方法capability、UDS peer与nonce继续生效。强类型枚举仅CreateDirectory/CopyFile/MoveFile/SetPermissions；不能传命令、UID、宿主绝对路径或任意JSON脚本。计划以uint64设备/inode和有符号纳秒精确表达原对象/父目录身份、原版本、大小/完整SHA、普通权限及opaque fingerprint；Apply绑定永久UUID、不可逆owner与同一计划，回执再核对UUID/操作类型/指纹/大小/SHA。

1.23增加`Discard(FileOperationApplyRequest)`和`files.mutation-tombstones`能力；仅此方法的签名可调用固定`operation-discard`动作。新记录只写原owner/UUID/计划绑定的Aborted回执，不需要源路径仍可读；迟到Apply只重放Aborted，不执行。已有Completed不回滚，已有非终态只观察恢复，Unknown不重做。新BFF要求此能力，不能对1.22旧Agent静默绕过取消保护；源码实现不等于安装态已签收，见[工作流账本](qa/FILE_MUTATION_WORKFLOW.md)。

固定helper现有operation-plan/operation-apply/operation-status/operation-discard四枚举；相邻执行模块须root所有、单链接、不可组/他人写。上传和变更共用非阻塞namespace提交锁，忙时不误标Unknown；新注册根须明确files-mutate。安装只升级与旧默认完全相等的注册表并原子备份，任何自定义/收紧规则原样保留。新BFF/持久任务/Worker/人类审计/UI在3e2da89源码中已通过门禁，尚未覆盖该运行时；最后安装态仍3f47300/1.22。见[文件变更](FILE_MUTATIONS.md)及[源码验收](qa/FILE_MUTATION_WORKFLOW.md)，不代表完整文件管理。

### 登记文件传输（1.20；1.21增加暂存发布）

专用 `FileTransferService.ListRoots/List/Inspect/Download/Upload/GetUploadStatus`；根ID与相对路径、版本化顺序流、32KiB帧、1GiB与时间/并发/容量预算、root-only回执和Unknown禁止自动重写。独立root配置升级保留，`openat2`目录FD禁止链接和根以下跨挂载；原子EXCHANGE保留旧对象。1.21增加方法级capability的`StageUpload/PublishUpload/DiscardUpload`：先完整收取校验并持久Staged，再由持久Worker复核权限/期限/取消/租约后发布；取消先创建root tombstone阻止迟到发送，Completed不可撤销，Unknown不自动重写。浏览器只经15378ec已验收的BFF/确认/持久任务/人类审计与WebUI，不直接调用RPC；基础见 [文件通道](FILE_TRANSPORT.md)，当前闭环和剩余项见 [实机证据](qa/FILE_MANAGEMENT.md)。

### 宿主PTY（1.18）

源码为**1.25**；1.19已增加专用 `ContainerTerminalService.Inspect/Connect` 和 `docker.terminal.exec`。新文件能力部署状态见文件通道与BFF记录，下方1.18为宿主能力加入时的历史版本，仍保持兼容。

独立 `HostTerminalService/Connect` 双向流同时验证UDS peer与方法绑定、单次nonce的短时capability；首帧只接受canonical UUID、主体、open与2–400列/行。后续仅4KiB内的二进制input、resize、close，不可更换目标、主体、UID、路径或启动程序。root与Agent注册表共同批准固定宿主资源，helper在独立systemd cgroup里以固定非特权身份启动，管道正文不进入操作存储/日志。退出/断线/超时清理，防重放元数据由root原子维护，停止必须匹配systemd InvocationID。原生证据见 [PTY基础](qa/HOST_TERMINAL_PTY.md)。

浏览器只连接BFF的一次性、登录绑定WebSocket，不直接连接本RPC；BFF持久元数据与审计见 [会话层与实际状态](qa/HOST_TERMINAL_BFF.md)。当前宿主会话仅管理员共享的受限身份，不是root控制台、租户级文件系统隔离或开发空间终端；085acef容器BFF/UI已覆盖部署并通过真实链路/浏览器验证，见 [当前证据](qa/CONTAINER_TERMINAL_BFF_UI.md)。文件传输等仍缺，不能签完整P1-11。

### 容器Exec（1.19）

Inspect只接受`ResourceReference(type=Container, id=完整小写64位ID)`，返回名称、镜像、本次启动实例与配置用户，不含Env。Connect首帧`ContainerTerminalFrame`将既有有界终端payload和容器ID/启动实例/sh|bash|ash绑定；后续帧不得更换目标、实例或shell。独立方法capability、UDS peer与单次nonce继续生效。

Agent与root驱动分别检查核心保护及隔离；root驱动通过固定Docker Unix API建立真实Exec，等待实际PID再Ready，保留容器配置的用户/隔离。pidfd只关闭本次交互Exec，不停止容器；主动脱离终端的后台服务归容器管理，不承诺宿主PTY的全cgroup清理语义。标准输入与输出都受预算限制，root回执不存正文或凭据，停止必须匹配InvocationID。实现、失败与实际验证见 [容器Agent证据](qa/CONTAINER_TERMINAL_AGENT.md)。浏览器仅通过085acef已签收的专用BFF许可/持久模型/审计和WebUI访问，不直接连接本协议；文件传输/隔离/清理仍待继续。

### 通知私有配置（1.17）

`notifications.private-configuration` 增加独立 `NotificationConfigurationService/Inspect/Apply`。Inspect 接受渠道 UUID 和可选操作 UUID，只返回别名/布尔/不透明版本及原操作回执；`binding_current=false` 表示已被后续变化取代。Apply 的私有字节只经 UDS 和 helper stdin，在有界校验后送固定 root writer，不能进入 OperationStore/任务/审计或命令参数。根回执和绑定同一个原子文件提交，可在输入暂存过期后查询确认；最终 Application 永久幂等和 PG 一致性仍须实现。

固定 helper 用短时 systemd unit 逃离 Agent 只读挂载命名空间，writer 自身保持 ProtectSystem=strict，只有通知私有目录可写、无网络 socket；不扩大 Agent 的 ReadWritePaths。详见 [实现与验收状态](qa/NOTIFICATION_PRIVATE_CONFIGURATION.md)。源码与已部署运行时须按该证据区分；其他原有 1.16 能力保持兼容。

### 主机负载与状态（1.16）

现有 `HostService.StreamMetrics` 有界批次增加 `load.average.1/5/15`（tasks）和 `load.normalized`（percent）；后者是一分钟负载除以逻辑核心数，不是 CPU 使用率，允许超过 100%。缺失/非法 `/proc/loadavg` 返回 Unavailable 质量。沿用 6 秒采样、UDS/短时 capability、14 天指标保留；不新增宿主路径、命令或 TCP 方法。

`ListSystemdUnits` 仍仅注册单元；状态按 `ActiveState/SubState/LoadState/UnitFileState` 命名属性读取，不依赖输出次序。现有 Docker 原生 summary `Status` 与进程 State 共同用于健康判定；starting/未知/失联不能解除风险，未配置健康检查不等于健康检查通过。详见 [状态与负载验收](qa/HEALTH_ALERTS.md)。

### 备份目标容量（1.15）

`ManagedResourceService.GetBackupTargetCapacity(Empty)` 仅返回批准 HDD 目标的 logical ID、可用标志、总/已用/可用字节、采样 UTC 时间及稳定错误码，不接受任意路径。Unix peer credentials 与绑定方法的签名 capability 仍生效。固定 root helper 无参数只读查询，验证目录无 symlink、与 HDD 基目录同文件系统；Agent 缓存 30 秒以限制 sudo 日志，客户端拒绝超过 90 秒或非法容量样本。

Worker 按策略创建只读 `BackupStorageCapacity` 规则，告警去重、升级、静默到期及恢复写现有表。未知容量不能恢复或降低已有临界风险，低于 256 MiB 空闲触发临界；禁用策略可显式收尾告警。该规则不能由通用告警编辑入口覆盖。存储模型不变，本阶段无新 migration；真实隔离库仍通过完整 migration 验证。

### 应用归档保留（1.14）

`applications/cleanup-backups` 使用既有计划/执行 RPC，参数仅允许 slug、retentionDays（1–3650）和 retentionCount（1–365），计划绑定 root 所有的已校验归档集合。清理不会改变应用运行数据，始终保留最新一份；普通停止/删除保护不能通过清理入口绕过。清理也使用 API job UUID 对应的独立 systemd 单元，删除每对归档/元数据之前先持久化 intent；恢复仅收尾已经开始的一对，不擅自继续其余候选。终态结果携带真正删除的 backup ID，Worker 在成功、失败或取消时按原资源更新 Expired 历史，不删除审计记录。

应用策略强制 gzip/SHA256，启用需管理员确认，并持久化 live/quiesced 一致性。Worker 调度及备份完成后分别安排有幂等键的清理；签发前检查策略仍启用且版本一致，并生成新计划，不复用排队前的过期计划。

### 应用归档（1.13）

`applications/backup` 与 `applications/verify-backup` 使用既有 `ManagedResourceService.PlanAction/RunCompose`、短时 capability、确认、资源锁和单次计划。范围固定为批准的普通应用配置、data bind 与独占本地卷，禁止核心与共享资源；预检使用私有 HMAC，不返回 Secret 值或其公开散列。默认 quiesced 仅提供文件系统暂停，不保证数据库事务一致性；不提供恢复或下载。

备份由 UUID 绑定的独立、限时 systemd 服务执行。应用备份/校验的 Agent operation ID 固定等于 API job ID，Worker 在发送 RPC 前持久化它，防止响应丢失或并发取消保存冲突造成“尚未执行”的误判。Agent 保存恢复标记，重启后只观察原服务，不重启或复制任务。取消须等待原服务停止且暂停成员恢复；已完成归档可优先于迟到的取消。ExecStopPost 与定时恢复使用项目 flock，不能干扰存活备份。Worker 将归档结果写入 BackupRecord，校验任务只更新原记录的校验状态，不写应用更新历史。

### 应用参数编辑（1.12）

当前实现增加 `applications.configuration` 能力与 `ManagedResourceService.GetApplicationConfiguration`。快照只返回结构化资源、现有环境变量名称及命令覆盖存在标记，不返回环境值、Cmd 或 Entrypoint 原文。

`applications/configure` 预检由 API 所有者绑定短时票据提供候选；计划绑定源文件、服务成员、候选内容、已缓存镜像与批准网络。执行消费一次票据并再次核对指纹。仅保存不重启；保存并重部署禁止隐式 build/pull，数据卷不自动清理。核心资源、任意宿主路径与受保护网络不能通过此入口修改。配置预检使用清空继承环境后的固定 CLI 环境，文件权限 `0600`。

### 容器资源关联（1.11）

既有 `InspectContainer` 增加可选 `relationships`，包含实际 image ID、Compose project/service、network mode、结构化网络端点、挂载与端口映射。网络最多 128，挂载/端口各最多 256，单字符串最多 4096 字符，所有文本合计最多 262144 字符；超限标记 `is_truncated`。只投影固定字段，不返回 Env 值、driver options 或原始 Config。宿主机映射配置不代表正在监听；运行时已解析端口优先，停止容器可退回配置值，未指定 host port 展示自动分配。

容器摘要同时增加实际 image ID、named volume names、network IDs，供关联资源页精确反向筛选，无逐容器 inspect N+1 请求。旧 Agent 的可选消息为空，UI 明确提示不能伪造“没有关联”。Compose labels 仅供导航，不能赋予管理权；只有注册表认可的 ComposeApplication 才显示应用详情，保护规则仍由注册表与标签双重执行。

### 容器资源采样（1.10）

`DockerService.GetContainerUsage(Empty)` 返回 `ContainerUsageBatch`，只读缓存，不触发 Engine 采样。Agent 每 6 秒通过 Docker Engine `stream=false&one-shot=true` 采样；每轮总预算 5 秒、最多 8 个并发请求、最多 256 个容器，并返回总数、截断及跳过计数。超过 18 秒未更新标记 stale，前端同时检查每个容器自身时间戳。

CPU 用相邻原生计数器差值计算，100% 代表一个逻辑核心（可超过 100%）。首次、重启、计数器重置、无效或超长窗口返回 Warmup/null。内存扣除 cgroup 可回收文件缓存；缺失字段保持 null。停止容器返回 Stopped/null，读取失败返回 Unavailable/null。网络与块 I/O 是累计字节而非速率。RPC 不推进计数器基线，不启动逐用户/逐页面 Docker 统计流。

Worker 将同一缓存批次的 CPU、内存、累计网络/块 I/O 和 PID 指标批量写入现有指标存储；按资源、设备、类型和 PostgreSQL 微秒时间戳去重，权限仍限管理员。没有成功逐样本 Info 日志。

## 2. 通用消息

### 2.1 请求上下文

所有非健康检查 RPC 必须包含：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `request_id` | UUID | 每次调用唯一，用于日志关联 |
| `trace_id` | string | 贯穿 HTTP、任务、RPC 和审计 |
| `actor_subject` | string | Authentik Subject；系统任务使用固定系统主体 |
| `job_id` | UUID? | 异步任务 ID |
| `idempotency_key` | string? | 写操作必填，同动作/资源重复提交返回原结果 |
| `issued_at_utc` | timestamp | 签发时间 |
| `expires_at_utc` | timestamp | 最长 60 秒，过期拒绝 |
| `capability` | bytes | 签名后的动作、资源和权限声明 |

能力声明至少约束 `action`、`resource_id`、`job_id` 和有效期。Agent 不根据前端传来的角色字符串自行推断权限。

### 2.2 资源引用

```text
ResourceRef {
  resource_type
  resource_id
  expected_version?
}
```

`resource_id` 必须来自 Agent 的资源注册表。涉及宿主机路径时，API 只发送资源 ID 或挂载 ID，Agent 从本地只读注册表解析实际路径；不得接收任意绝对路径。

### 2.3 异步操作

写 RPC 返回：

```text
OperationRef {
  operation_id
  job_id
  state
  phase
  progress_percent?
  accepted_at_utc
}
```

状态为 `Queued`、`Running`、`WaitingForHealth`、`Succeeded`、`Failed`、`RollingBack`、`RolledBack` 或 `Canceled`。Agent 在 `/var/lib/whaledeck-agent/operations` 原子保存操作状态和非敏感恢复数据；Docker、API、PostgreSQL 或 Valkey 重启不能丢失操作。

### 2.4 错误码

| 代码 | 含义 |
| --- | --- |
| `INVALID_ARGUMENT` | 字段、枚举、资源组合或计划哈希无效 |
| `PERMISSION_DENIED` | 能力声明无权执行该动作 |
| `RESOURCE_PROTECTED` | 目标为受保护资源，普通流程禁止操作 |
| `RESOURCE_CONFLICT` | 资源锁、端口、名称、挂载或并发操作冲突 |
| `PRECONDITION_FAILED` | 健康、磁盘空间、版本或计划哈希不满足 |
| `NOT_FOUND` | 已批准资源不存在；不返回额外路径信息 |
| `UNAVAILABLE` | Docker/systemd/依赖服务暂不可用 |
| `OPERATION_NOT_CANCELABLE` | 当前阶段不能安全取消 |
| `ROLLBACK_FAILED` | 原操作和回滚均失败，需要人工介入 |

Agent 错误只返回安全摘要和诊断 ID，不返回完整环境变量、命令行、Secret 或配置文件内容。

## 3. Agent 与操作状态

| RPC ID | Service/Method | 类型 | 调用方 | 说明 |
| --- | --- | --- | --- | --- |
| `RPC-AGT-001` | `AgentService.GetCapabilities` | Unary | API/Worker | 版本、协议、平台和支持能力 |
| `RPC-AGT-002` | `AgentService.GetHealth` | Unary | API/Worker | Agent、Docker、systemd 和本地存储状态 |
| `RPC-AGT-003` | `AgentService.WatchHealth` | Server stream | Worker | 心跳和能力变化，低频发送 |
| `RPC-OPS-001` | `OperationService.GetOperation` | Unary | API/Worker | 查询持久操作状态 |
| `RPC-OPS-002` | `OperationService.GetMaintenanceStatus` | Unary | API | Docker/主机/自身维护阶段和结果 |
| `RPC-OPS-003` | `OperationService.CancelOperation` | Unary | API | 在声明为可取消的安全阶段取消 |
| `RPC-OPS-004` | `OperationService.WatchOperation` | Server stream | API/Worker | 任务阶段变化；断线后可按游标恢复 |

Agent 每次维护状态变化都原子写入 `/run/whaledeck/maintenance/status.json`。该文件只包含状态、阶段、时间和安全错误码，由宿主机稳定入口提供给维护加载页。

## 4. 主机、设备与指标

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-HOST-001` | `HostService.GetHostInfo` | Unary | 主机名、发行版、内核、架构、启动时间、CPU/内存摘要 |
| `RPC-HOST-002` | `HostService.StreamMetrics` | Server stream | 每 6 秒发送 CPU、内存、负载、网络和磁盘 I/O 采样 |
| `RPC-HOST-003` | `HostService.ListNetworkInterfaces` | Unary | 网卡、地址、链路、速率、路由摘要和累计流量 |
| `RPC-HOST-004` | `HostService.ListStorageDevices` | Unary | 磁盘、分区、文件系统、挂载、inode 和 SMART 摘要 |
| `RPC-HOST-005` | `HostService.RebootHost` | Operation | 管理员确认后重启；拒绝关机请求 |
| `RPC-HOST-006` | `HostService.ListProcesses` | Unary | 有界进程快照、CPU/内存/PID/名称排序、过滤及分页 |
| `RPC-HOST-007` | `HostService.GetProcess` | Unary | 通过 PID 与启动身份读取当前只读详情；PID 复用返回不存在 |

进程采样每 6 秒一次，不记录成功采样日志。CPU 根据单调时间和累计 CPU 时间差分，100% 为一个逻辑核心；首次采样或新进程返回 `null`，不伪造 0%。返回采样时间、窗口、18 秒过期标记、截断标记与跳过数量；采样上限 32768，每页 1–500，过滤最长 128。只返回 PID、父 PID、UID、名称、状态、线程、启动时间和 CPU/内存统计，不读取或返回 argv、环境变量及可执行路径。详情的 `instance_id` 为 `pid:启动时间UTC ticks`，不能用新进程替代已退出实例。API 在未知主版本或未声明能力时返回 503 `AGENT_CAPABILITY_UNSUPPORTED`，不降级为伪空列表。

名称与内存直接来自固定 status 字段，不使用会间接读取 cmdline 的框架名称/进程信息补全路径。名称为内核 `Name`，可能按内核规则截短；内存按 kB 转字节，内核线程缺失地址空间字段时为 0，格式错误或整数溢出时跳过而非伪造值。

指标使用单调计数器计算速率，包含采样起止时间和设备稳定 ID。Worker 负责聚合、降采样和 14 天清理，Agent 不为每次采样写 Info 日志。

`1.9` 的主机指标后台采样器独立于 RPC 查询：同一批次共享采样时间，多个页面/API/Worker 读取不会推进计数器基线。首次采样、计数器重置返回 `Warmup`；磁盘无完成 I/O 时延迟返回 `Idle`，API 将非 `Good` 值投影为 `null`，不把缺失或空闲画成零延迟。仅汇总实际块设备，不重复计算分区、device-mapper 和虚拟磁盘。

新增 `disk.read.iops`、`disk.write.iops`（operationsPerSecond）及 `disk.read.latency`、`disk.write.latency`（milliseconds）。磁盘速率按 sector 差值 × 512 / 单调时间差；延迟按读/写累计毫秒差 / 完成 I/O 数差，表示内核请求完成耗时而非磁盘设备自身纯服务耗时。历史延迟按匹配 IOPS 加权。字段依据 [Linux I/O statistics](https://docs.kernel.org/admin-guide/iostats.html)。

`GET /api/v1/metrics` 的 `window=live|1h|24h|7d` 为管理员只读接口；历史分别按 30/300/1800 秒聚合，返回 121/289/337 个对齐时间桶和 `windowStartUtc/windowEndUtc/resolutionSeconds`，当前桶可能未结束。无采样桶为 `Missing/null`。实时保持最多 600 个原始点的旧 `take` 兼容范围；Vue 使用固定 6 分钟 61 槽。数据库端有界聚合，不把 7 天原始样本全部加载到 API 内存。UTC 日分区及按分区 14 天保留已有专用 migration/SQL 与历史验收；分钟/小时与迟到回填见 [聚合证据](qa/METRIC_AGGREGATION.md)，不能把此子项已验证当作整个监控或 P0–P5 已完成。

## 5. systemd、日志与系统更新

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-SVC-001` | `SystemdService.ListUnits` | Unary | 白名单范围内 unit 状态和启动方式 |
| `RPC-SVC-002` | `SystemdService.ChangeUnitState` | Operation | 启动、停止、重启白名单 unit；核心 unit 使用专用流程 |
| `RPC-SVC-003` | `SystemdService.ListTimers` | Unary | WhaleDeck 注册 timer、上次/下次运行和结果 |
| `RPC-LOG-001` | `JournalService.Query` | Unary | 按 unit、级别、时间和游标查询，限制行数/响应大小 |
| `RPC-LOG-002` | `JournalService.Follow` | Server stream | 受限实时日志；断线和慢消费者主动结束 |
| `RPC-UPD-001` | `UpdateService.Check` | Operation | 检查可用普通系统更新和待重启状态 |
| `RPC-UPD-002` | `UpdateService.Install` | Operation | 执行普通更新，不自动重启主机 |
| `RPC-UPD-003` | `UpdateService.GetTimerStatus` | Unary | 周日 20:00 自动更新 timer 状态 |

Agent 不提供“执行命令”“执行脚本”或任意 unit 名称接口。

## 6. Docker 查询与容器操作

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-DKR-001` | `DockerService.ListContainers` | Unary | 状态、健康、镜像、项目、网络和保护标记 |
| `RPC-DKR-002` | `DockerService.InspectContainer` | Unary | 规范化详情，敏感环境变量值脱敏 |
| `RPC-DKR-003` | `DockerService.GetContainerLogs` | Unary | 时间、尾部行数和响应大小有上限 |
| `RPC-DKR-004` | `DockerService.GetContainerStats` | Unary | 单次读取 CPU、内存、网络、块 I/O 和进程数 |
| `RPC-DKR-005` | `DockerService.GetEngineInfo` | Unary | Engine/API 版本、存储和日志驱动 |
| `RPC-DKR-006` | `DockerService.PlanCreateContainer` | Unary | 只读校验，返回计划哈希、冲突和资源摘要 |
| `RPC-DKR-007` | `DockerService.CreateContainer` | Operation | 必须携带未过期计划哈希 |
| `RPC-DKR-008` | `DockerService.StartContainer` | Operation | 受保护策略校验 |
| `RPC-DKR-009` | `DockerService.StopContainer` | Operation | 二次确认后执行；核心容器拒绝 |
| `RPC-DKR-010` | `DockerService.RestartContainer` | Operation | 二次确认后执行；核心容器拒绝 |
| `RPC-DKR-011` | `DockerService.DeleteContainer` | Operation | 默认保留卷和镜像；核心容器拒绝 |
| `RPC-DKR-012` | `DockerService.PullImage` | Operation | 只允许批准 Registry/镜像加速配置 |
| `RPC-DKR-013` | `DockerService.PlanPrune` | Unary | 返回引用关系和预计回收空间 |
| `RPC-DKR-014` | `DockerService.Prune` | Operation | 必须携带计划哈希，跳过受保护资源 |
| `RPC-DKR-015` | `DockerService.ListImages` | Unary | 摘要、大小、标签和引用容器 |
| `RPC-DKR-016` | `DockerService.ListNetworks` | Unary | 网络、范围、子网和关联容器 |
| `RPC-DKR-017` | `DockerService.ListVolumes` | Unary | 卷、挂载、关联容器和孤立状态 |
| `RPC-DKR-018` | `DockerService.InspectImage` | Unary | 读取或按请求拉取 OCI 镜像声明，返回 Env、Ports、Volumes、Entrypoint、Cmd 与 Labels |
| `RPC-DKR-019` | `DockerService.ListEvents` | Unary | 返回 Agent 订阅并脱敏、去重、限长后的近期 Docker 事件 |

创建 DTO 使用类型化字段，不接受 Docker CLI 参数字符串。首期禁止通过创建页面启用 `privileged`、宿主机 PID/IPC namespace、任意设备或未批准的宿主机路径。

## 7. Docker 配置与 Compose

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-DCFG-001` | `DockerConfigService.Get` | Unary | 返回支持字段、脱敏配置摘要和配置版本 |
| `RPC-DCFG-002` | `DockerConfigService.Validate` | Unary | 合并未知字段、原生校验、返回差异/影响/计划哈希 |
| `RPC-DCFG-003` | `DockerConfigService.ApplyAndRestart` | Persistent operation | 备份、原子写入、重启、健康等待和自动回滚 |
| `RPC-CMP-001` | `ComposeService.ListProjects` | Unary | 识别已有项目、服务、配置来源和目录 ID |
| `RPC-CMP-002` | `ComposeService.PlanDeployment` | Unary | 模板解析、差异、依赖、卷和计划哈希 |
| `RPC-CMP-003` | `ComposeService.Deploy` | Operation | 使用现有模板部署或更新 |
| `RPC-CMP-004` | `ComposeService.ChangeProjectState` | Operation | 按依赖顺序启动、停止、重启 |
| `RPC-CMP-005` | `ComposeService.RemoveProject` | Operation | 明确保留/删除数据卷 |

`ApplyAndRestart` 必须在 API 消失后继续运行。核心容器更新不走普通 Compose RPC，而走平台维护服务。

## 8. 数据库与备份

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-MGD-001` | `ManagedResourceService.PlanAction` | Unary | 对注册资源执行只读预检并签发短时、单次计划哈希；不会解释为 Docker 容器创建 |
| `RPC-DB-001` | `DatabaseService.ListInstances` | Unary | 注册实例、引擎、版本、健康、容量和存储摘要 |
| `RPC-DB-002` | `DatabaseService.ListResources` | Unary | 数据库/Schema、账号/角色、权限和连接摘要 |
| `RPC-DB-003` | `DatabaseService.CreateDatabase` | Operation | 引擎适配器参数化创建 |
| `RPC-DB-004` | `DatabaseService.DeleteDatabase` | Operation | 二次确认，禁止系统数据库 |
| `RPC-DB-005` | `DatabaseService.CreatePrincipal` | Operation | 生成密码、最小权限；结果 Secret 只交付一次 |
| `RPC-DB-006` | `DatabaseService.ChangePrincipalState` | Operation | 禁用/启用/删除账号或角色 |
| `RPC-DB-007` | `DatabaseService.GrantPermissions` | Operation | 只接受引擎定义的权限枚举 |
| `RPC-DB-008` | `DatabaseService.RotateCredential` | Operation | 轮换并一次性交付新 Secret |
| `RPC-DB-009` | `DatabaseService.ListConnections` | Unary | 返回脱敏连接元数据 |
| `RPC-DB-010` | `DatabaseService.TerminateConnection` | Operation | 禁止终止系统/维护连接 |
| `RPC-BKP-001` | `BackupService.Run` | Operation | 调用注册备份实现，写入 HDD 临时文件后原子完成 |
| `RPC-BKP-002` | `BackupService.Verify` | Operation | 校验文件完整性和引擎可读性 |
| `RPC-BKP-003` | `BackupService.Cancel` | Unary | 仅在安全阶段取消并清理临时文件 |

数据库适配器映射到预注册实例与实现，不接受任意 SQL、JavaScript、Shell 或容器名。数据库恢复 RPC 保留编号空间但不注册服务。

## 9. 配置仓库与平台维护

| RPC ID | Service/Method | 类型 | 说明 |
| --- | --- | --- | --- |
| `RPC-CFG-001` | `ConfigRepositoryService.GetStatus` | Unary | 批准仓库、分支、提交、工作区和同步状态 |
| `RPC-CFG-002` | `ConfigRepositoryService.PlanSnapshot` | Unary | 文件清单、差异、敏感检查和计划哈希 |
| `RPC-CFG-003` | `ConfigRepositoryService.CommitAndPush` | Operation | 只提交批准文件，固定 Git 参数，记录提交 SHA |
| `RPC-MNT-001` | `PlatformMaintenanceService.RunDiagnostics` | Operation | 检查 Agent、Docker、核心服务、网络、磁盘和时间 |
| `RPC-MNT-002` | `PlatformMaintenanceService.PlanUpdate` | Unary | 镜像摘要、配置、迁移兼容、空间和回退能力 |
| `RPC-MNT-003` | `PlatformMaintenanceService.ApplyUpdate` | Persistent operation | 编排核心更新并始终保留稳定入口 |
| `RPC-MNT-004` | `PlatformMaintenanceService.Rollback` | Persistent operation | 回退镜像/配置；不执行不安全的数据库降级 |
| `RPC-MNT-005` | `PlatformMaintenanceService.BuildDiagnosticBundle` | Operation | 生成有大小/时效限制的脱敏诊断包 |

配置仓库 RPC 不接受任意仓库 URL、本地路径、提交参数或 Git 命令。仓库和允许目录由宿主机配置注册。

## 10. 超时、流控与日志

- 普通 Unary 查询默认 10 秒；慢查询必须分页或转任务。
- 流式日志和统计必须支持取消、最大持续时间、最大消息大小和慢消费者保护。
- Agent 只在状态变化、失败和管理操作时记录结构化日志；6 秒指标不写逐样本 Info 日志。
- 所有 Secret 字段在 protobuf 中使用明确类型，日志拦截器默认丢弃这些字段。
- Agent 重启后扫描未完成操作：可恢复则继续，不可恢复则标记失败并给出安全诊断码。

## 11. Compose 项目能力补充

现有 ManagedResourceService 宣告 compose.projects 能力，沿用协议 1.7 和现有 wire 消息：

- ListComposeProjects 同时发现 Docker Compose 项目与批准目录中的安装；未注册项目只读，核心资源按注册表和标签双重保护。
- PlanAction 使用 resource_type=compose-projects、resource_id=application:<slug>，动作允许 start/stop/restart/pull/redeploy。不接受客户端路径、命令、配置或额外参数。
- 预检比较 Compose 原生服务 hash 与现有容器标签，只输出服务级差异与受影响容器数量，不输出规范化配置、环境变量或 Secret。计划绑定配置输入及容器成员指纹，五分钟内一次性消费。
- RunComposeAction 执行前再次验证指纹、项目名称和受保护容器名称；启停/重启、拉取与重部署全部要求计划和管理员确认。与应用生命周期共享资源锁。
- pull 不重启服务；redeploy 不隐式拉取或构建镜像。孤立服务、配置和数据卷不自动删除；启动/重启/重部署等待所有目标服务健康。
- 当前提供服务级配置变化摘要，不提供原文编辑或逐字段 diff；项目移除和数据卷选择继续使用现有应用卸载用例。Git 同步属于独立后续能力。
