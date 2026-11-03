# ACL旧版本预检拒绝：306f646部署与真实页面验收

## 最新签收：306f646实际部署与旧版本预检失败真实页面

2026-11-03。**原唯一标准发布最终成功，新独占资源的真实门禁及独立只读复核通过。** 本节覆盖下面10:44“仍在发布”的快照及原候选“未部署/真实门禁未执行”待办；原时间点正文保留，不重做已完成的操作。只签本场景，不签完整ACL、P0–P5或master交付。

### 原Invocation完成，不重复发布

`whaledeck-dev-publish-306f646.service` / Invocation`d38034cfaba24730ac1fe1acae325829`，10:22:12–10:50:22 CST，约1690秒。NuGet多包EOF与Ubuntu源超时/重试确实发生并保存在原journal，仍是同一次标准update，无第二次启动、改依赖/源/TLS或自动回滚。最终active/exited、MainPID0、Resultsuccess、ExecMainStatus0；保存终态及完整journal后才停止已结束单元，inactive/dead且结果仍success。

严格六组件检查通过：API/Worker/Gateway实际运行CLI/OCI镜像revision、两宿主CLI、隔离无网络Migrator CLI及实际API/Gateway HTTP均为`306f646110229496959c0c8a2e8f5a2f1570a0e7`。Agent4135740与MaintenanceHost4135726的/proc/exe及运行中DLL设备/inode匹配当前安装文件。11核心healthy，3面板容器正常替换，8数据库平台CID/镜像/StartedAt不变；标准migrator实际“database is already up to date”，没有新schema。安装态validate的systemd/权限/UDS/受限helper/HDD策略与动态网卡入口检查通过。私密配置/开发sudo SHA/owner/mode保持，不改密码或产品Agent授权。

维护观察器1422次采样均HTTP200，状态updating→healthy、错误0；覆盖宿主安装后的本段，不覆盖发布起点到宿主重启前段，不能宣称整个发布窗口无中断。7静态进行中原件及已增长的观察日志都保留。最终部署目录root私有`deploy/artifacts/acl-stale-deploy-306f646/`：25份静态文件SHA、完整journal/终态/六组件/进程映射/11核心/配置sudo保护/安装检查/维护总结；原进行中快照没有被改成成功。最终权限门禁发现收尾观察器8份文件继承0644（目录一直root私有0700），SHA均一致；仅对这8份自有QA文件收紧0600并保存evidence-permissions-correction.json，未改内容、未重跑部署或真实操作。

P0-05缺口仍在：实际此前已部署447070e，但helper的previous-revision记录为目标306f646。只读`previous-revision-observed.json`明确recordsActualPrevious=false；没有修补该文件、没有执行回滚，也不据此签手动上一版恢复能力。

### 新资源与真实拒绝，未进入执行阶段

QA冻结`2435d44d6791c569c99bffec8991c0c1c2761f47`，proofVersion1、28源码/11安装模块逐字节核对；实际产品306f646/1.51。新随机独占容器/源/内容/临时身份，不复用前批Unknown或实例变化资源。非核心、1000:1000、无网络、只读根和唯一自有HDD bind。未重启任何服务/Worker/Agent或其他容器。

真实Authentik可见登录→Vue打开ACL对话框，持有源的原版本；父观察器复核准确自有容器/旧完整清单/文件FD后，只有该FD的mtime增加17ns并由内核增加ctime。两个文件各2097159完整字节、inode/mode/UID/GID/nlink/大小、两类ACL与固定普通xattr保持，其余树九字段/ACL不变；另存physical-mutated，不把新清单改写成原版本。

原Vue提交确实仍携初始expectedVersion。真实API/Outbox/Worker→安装Agent/固定helper规划拒绝，原意图/规划Job为Failed、CONTAINER_ACL_VERSION_CONFLICT，无计划/确认/执行ID/Root启动/回执/发布版本/历史摘要。实际1规划Job/2审计、确认0；原key再次登记仍返回同一Failed意图，无新Job或重写原SQL。执行Job0、关联Root0及attested HDD元数据0均独立核对，包括意图ID和.NET准确字节序导出的执行ID；不是用随机ID探测“没有历史”。

这证明的是**预检拒绝、没有进入执行阶段**，不是执行Unknown无回执，也不是Aborted/aclConfirmed=false。源的mtime变更由QA显式产生；不把该改动说成产品ACL写入。ACL确认/目标类型/目标模式/noChange/绑定均null，UI应显示未证明；历史回执显示未取得，历史/发布版本显示否。

### 两种实际任务详情、权限与原上下文退出

原planning Job的15字段SQL/API受限摘要同值同类型；同一正常登录上下文在320×600暗色和1440×600亮色打开真实任务中心同ID详情，标题“预检失败，未进入 ACL 修改执行”，不是执行失败。未证明/未取得等事实、完整字段视口及横向边界通过，身份/路径/版本/凭据不进入安全摘要，无通用取消/重试按钮。5实际PNG含预检失败对话框和两种标题/事实；已目视核对320标题/事实与1440标题，不以预览截图替代。

唯一原UI注册1、确认0、内容写0、Job管理写0；额外原key重试1。原管理员在同上下文正常面板/Authentik双端退出，BFF401/身份401或403与provider导航证明齐全；另真实普通身份2读/1登记均403，匿名2读均401，普通HTTP登录也正常双端退出，原SQL快照完全保持。没有注入Cookie、拦截响应、写SQL/缓存/时钟或删除身份冒充退出。

### 独立原件、准确清理与边界

父进程在清理前复核完整内容/九字段/ACL/xattr等于写后mtime清单，仅在原Failed且无执行历史时清理本案源；原fixture严格核对所有权后移除准确容器。独立新进程重新只读核冻结来源/安装模块、原SQL/摘要、关联Root/HDD缺席、两种实际详情/正常退出、临时身份与源/容器不存在、宿主来源/PID及11核心healthy，执行/Root/HDD仍0。它不声称清理后重读已删除源字节。

成功原件root:root/0700目录`deploy/artifacts/container-acl-stale-f5d6597939be40b8a593d0428011d331/`，21份文件SHA（索引自身另计）、5 PNG/JSON/日志0600。parent-final.log、independent-final.json、counts.json保留；主/独立原日志`/tmp/wd-acl-stale-real.log`与`/tmp/wd-acl-stale-independent.log`保留。用户可视副本是原PNG逐字节复制，无编辑/拼图，放忽略目录`deploy/artifacts/ui-candidates/acl-job-summary/stale-real-306f646/`。

本轮89相关离线门禁49＋40及文档范围复核通过；993前端单元/8生产预览是候选批次的既有结果，本轮没有再声称执行它们或后端全量测试。306f646 CI41565338163最后读取仍4路成功、前端在综合授权/传输/终端浏览器步骤运行，未取得整条成功终态。下方元数据措辞导致的1项范围失败与修复原始记录保留，不改测试或范围。

仍需真实执行无回执Unknown/不可信绑定/Superseded等摘要、其他共享写入/磁盘/路径组合、ACL递归与历史恢复、可恢复删除/长期清理、P0-05与其余59项/UI/C#酒馆/供应商/AI Live2D。当前40部分实现/17待实现/2最终验收不变，25%–35%仅既有低置信度工作量估算，不因分项通过抬升整项完成率。本批7份变动MD的790条相对链接无缺失，目标第1节与306f646逐字一致，以下2435d44快照及原候选正文逐字保留。最终项目图谱首次upsert连接失败，使用同一项目/事件重试成功，已核对projectId/key/eventId回执，事件`whale-deck.container-acl.306f646.stale-preflight-real`移入synced，阶段仍active。以下进行中与候选正文逐字保留。

## 后续执行快照：唯一标准发布仍在运行，未取得成功终态

2026-11-03 10:44 CST。运行checkout由所有者user从干净447070e快进至固定306f646（包含caa9c64产品改动），不由root写开发者Git索引、不读取或复制私密配置正文。唯一标准update单元`whaledeck-dev-publish-306f646.service`，Invocation`d38034cfaba24730ac1fe1acae325829`，10:22:12开始、TimeoutStartSec1800；本次快照仍activating/start、MainPID4127401。**运行中的Result=success/ExecMainStatus=0只是尚未退出，不能据此宣称发布成功。** 未重启或重复创建本次发布单元，不自动回滚dev。

宿主安装阶段已经产生新Agent4135740与MaintenanceHost4135726，跟随实际进程exe的build-info均306f646/协议1.51；API/Gateway实际HTTP仍447070e。11核心原CID/镜像/StartedAt/healthy均保持，8数据库平台未重建。私密.env/.images.env与开发user NOPASSWD文件的SHA/owner/mode同原基线，未改密码、未扩大产品Agent权限。当前是维护中的混合版本，不能将新宿主版本当作六组件一致的新部署。

镜像构建的Ubuntu源曾超时后继续安装，worker运行层安装步骤已实际DONE；NuGet多个固定包（Valkey.Glide1.1.0、Microsoft.CodeAnalysis5.0.0、Grpc.Tools2.84.0等）仍出现unexpected EOF重试。尚未取得整个镜像构建/迁移/容器替换终态，没有改包版本、源或TLS策略，也没有另起重复update。306f646的CI41565338163本轮查询为backend/installer/native-postgres/native-valkey成功、frontend运行中，未取得整条成功终态。

正确8080维护/status入口从宿主安装后开始的1052次本段采样均HTTP200/updating，错误0；采样未覆盖发布开始至宿主重启的前段，不冒称全窗口无中断。一次额外探针误用18080的维护路径，响应无法按JSON解码，未将它当作既定维护入口；后续只使用8080路径。未启动新的真实旧版本预检门禁，未创建新业务验收源/身份或进行mtime改动；预期的无执行/Root/HDD仍不是本批真实结果。

原件root私有0700目录`deploy/artifacts/acl-stale-deploy-306f646/`：baseline/launch/初始unit、progress-inflight/protection、部分journal和已准备但未执行的collect-final.py，7份静态文件SHA已保存；JSON/日志/脚本0600。`maintenance-observations.jsonl`由有界只读观察器继续追加，未将仍增长文件纳入静态SHA。旧候选和前批实机原件不改。89相关离线门禁本轮实际49＋40通过；不是新增真实BFF/Agent/磁盘或后端全量验收。

接续时先检查**同一Invocation/单元**及该目录，不重发发布。只有原unit得到真正active/exited/MainPID0/Resultsuccess/ExecMainStatus0，才执行已准备的严格收尾：保存终态与完整journal、停止已结束单元、六组件CLI/镜像/HTTP及运行exe/DLL inode核对、数据库/配置/sudo保护及空闲检查；通过后才新独占资源运行`validate_container_acl_stale.py <实际运行完整revision>`，随后独立只读核验。若原发布失败，先保留准确终态和日志，再修复并覆盖，不把失败记成功或自动回滚。进度元数据首次范围检查因“不计完成”未匹配既定“不算完成”字面约束而1项失败，已恢复既定措辞后重验；未修改59项或范围测试。7份变动MD的781条相对链接无缺失，目标第1节及以下原候选正文逐字保留。发布中事件`whale-deck.container-acl.306f646.publishing-d38034cf`已核对projectId/key/eventId回执并移入synced，阶段仍active，不记通过。以下原候选正文逐字保留为历史。

2026-11-03。固定源码候选`caa9c64`。**本批完成实现和本地候选验证，没有部署，也没有执行新的真实旧版本故障场景。** 运行仍447070e/协议1.51；Agent3947643、MaintenanceHost3947551，11核心healthy/所有活动计数0的只读复核通过。本批没有安装、EF/schema/依赖变更、服务重启、原生源文件变更、Root/HDD写入、回滚或sudo策略/密码修改。59主任务状态和现有25%–35%工作量粗估不因这些测试提高。

## 已复现并修正的问题

新增组件回归先在原代码明确失败：原planning/Failed、CONTAINER_ACL_VERSION_CONFLICT且未取得计划/回执时，摘要标题仍为“执行失败”。原实现11项通过/1项失败及完整断言保留。现在按已验证摘要的阶段区分：planning/Failed显示“预检失败，未进入 ACL 修改执行”，execution/Failed仍为“执行失败”；null/false/真实Completed/Aborted/Unknown等历史字段不被文案改写，未知或坏摘要继续失败关闭。版本冲突提示改为“对象版本已变化，请重新读取后预检”，不假定变化发生在预检之后。

生产预览增加真实形状的planning/Failed无计划无回执、execution/Failed/Aborted与Superseded/Completed绑定无效三分支，保留既有成功/Ready/Unknown/确认处置/旧任务空态。320/1440、明暗、600px高度四组合逐项核标题、三态、全部字段边界及无管理写请求，预检未证明字段完整入视口；不把Superseded说成未发生ACL修改。

## 新真实门禁已经实现，但本批没有执行

独立`validate_container_acl_stale.py/.mjs`及`verify_container_acl_stale.py`不改既有九场景或旧proofVersion。冻结干净Git来源与安装模块字节、实际产品来源/宿主及空闲后，使用新随机独占容器/源/内容/身份；非核心、非root1000:1000、无网络、只读根、准确HDD bind。正常可见Authentik→Vue先打开ACL对话框持有原版本，私有信号后主进程重新验证准确容器/旧完整清单/文件FD，**只将该自有文件mtime增加17ns**，不改全局时钟、SQL、缓存、Root、HDD或产品helper。

父进程核对完整2097159字节、原inode/mode/UID/GID/nlink/大小、两类ACL及固定普通xattr不变，除目标mtime和ctime外其余树保持；再允许原页面提交，强断言POST仍使用初始版本。预期实际Worker规划经已安装签名Agent/固定helper返回CONTAINER_ACL_VERSION_CONFLICT，原意图与规划Job Failed，无计划/确认/执行ID/Root开始时间/回执/发布版本/历史事实；不能将null写成aclConfirmed=false。SQL/API的15字段摘要严格同值同类型，原key重试只能返回原Failed意图。

正常任务中心320暗色与1440亮色的同一原Job详情必须显示新标题及未证明/未取得、无通用写按钮，并正常原上下文双端退出；5实际PNG才可签新门禁。另实际普通用户读/登记403、匿名读401及正常退出，原SQL不得变化。Root/HDD按意图ID及.NET准确字节顺序导出的执行ID检查不存在，不能以随机无关ID冒充。独立进程重新只读核对冻结来源、原SQL/摘要、关联历史缺席、真实页面/退出、准确清理、宿主与11核心；清理后不声称重读已删除源内容。

只在真实终态和完整已证明清单匹配时清理本案源；不明/未证明时保留数据。停止范围仅QA浏览器子进程，不杀产品helper、不重启宿主或产品服务，容器变化仅限准确自有fixture的创建/清理。原通用自有fixture仅在严格所有权核对后移除本案容器，证据永留。新脚本已加入CI离线语法及证明边界检查；它们的存在不代表实际门禁通过。

## 本批实际验证与已知失败

- 全部前端Unit：56文件、993项通过；新增组件负回归证明原文案错误，修复后通过。类型检查/生产构建、oxlint/eslint与格式通过。
- 冻结候选生产预览：4组ACL摘要＋4组本地HTTP EventSource续传共8项通过；使用测试身份/接口，不能证明Authentik、真实数据库、Agent、实际ACL或HDD。28预览PNG中新增失败/Superseded为12张，其他16张是旧摘要回归。
- 8新增证明边界＋11既有浏览器证明＋22 Unknown证明＋8范围＋40 Linux-root故障范围，共89项通过。临时文件FD测试证明只改准确mtime；错误容器、漂移ACL/非目标、虚假执行/计划/回执、错误主体/版本/摘要及模拟Root存在/临时目录中的关联HDD元数据均拒绝。固定执行ID向量对应前批真实.NET操作，不访问前批凭据。
- 首次`corepack pnpm`因本机缓存入口缺少bin/pnpm.cjs而MODULE_NOT_FOUND；使用现有用户固定pnpm12.10.1 native入口继续，没有安装/切换版本或修改全局配置。直接已安装Vitest复现原文案失败后，正常固定pnpm各门禁通过。首次e2e格式检查失败，经既定Prettier修正后通过。
- 新证明测试首次因直接导入unittest类额外发现11旧测试，共19执行；已改模块引用，最终新增只记8，89总数不重复计数。没有重新跑后端全量/.NET原生PG，不宣称新整条CI全绿。

本批预览/离线证据保存在忽略目录`deploy/artifacts/ui-candidates/acl-job-summary/acl-preflight-failed-caa9c64/`，包含候选/运行界限、原失败与修复日志、源码/构建和截图SHA索引。它不是部署或真实验收原件目录。

## 下一步与剩余范围

先正常冻结覆盖候选，核对六组件实际来源/安装helper/进程与数据库保护，再使用上述**全新**资源执行真实旧版本预检拒绝门禁和独立复核；不重放已签收的[同CID实例变化](CONTAINER_ACL_INSTANCE_CHANGE.md)或[目标消失](CONTAINER_ACL_POST_UNKNOWN.md)。新门禁目前未执行，Root/HDD为0只是预期断言，不是已取得实机结果。

无回执Unknown/不可信绑定/Superseded等真实摘要、其他共享写入/磁盘/路径组合、ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D继续。生产预览中出现Superseded不表示真实该分支已验收。

文档复核：8份变动MD的778条相对链接无缺失，目标第1节与caa9c64逐字一致，前批实例变化QA原正文逐字保留。候选证据40文件SHA及28预览PNG保存，元数据明确candidateDeployed=false/newNativeStaleGateExecuted=false。项目图谱事件`whale-deck.container-acl.caa9c64.stale-preflight-candidate`已核对projectId/key/eventId回执并移入synced，阶段仍active。
