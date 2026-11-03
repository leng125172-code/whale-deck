# 实施状态与恢复入口

- **2026-11-03 历史读取维护代理超时已定位，b69302b修复候选未部署**：写后移组分项dc243实机Succeeded/Completed、原403/引用清空/当前管理员Recovery200而原Owner接口404通过；同批停用及新批ea30重读均在历史列表503前置失败，未登记恢复/撤权，两失败树保留，不签整批。实际同请求内部Gateway4.972秒200/124份、维护入口5.007秒503维护HTML，定位普通5秒代理预算。候选新增仅规范历史GET的35秒路由及保留5秒硬截止的3秒软分页，Release零警告错误/4363Unit/488Integration（7原生跳过）/32Root通过，真实HDD候选82/42两页3.085/1.604秒读完124份不改原件；不是安装态RPC或UI签收。CI四路成功/frontend待，生产仍cad6182/1.52、1399935/1214273、11核心healthy/8数据库/7配置及账本保持；须标准覆盖与8080整链复验，59项不升完成率。[失败、根因、候选与未验](qa/CONTAINER_ACL_HISTORY_TIMEOUT.md)。

- **2026-11-03 ACL恢复写后取消/Worker回执中断实机通过**：产品仍cad6182/1.52不重发，工具3957cd7/schema10/version4两新资源在目标基本ACL已写/Root Applying时真实页面取消或准确原Worker SIGKILL。产品5.278/0.114秒结束helper，强杀原租约87.034秒/41采样后以原执行ID对账；两原任务Succeeded/Completed、aclConfirmed=true及新版本保持，取消请求未冒充已撤回。2旧来源+2当前HDD/9恢复审计/8Job、完整已写清单及迟到原Apply不变，正确wrapper/原上下文退出/独立核验/准确清理/4真PNG及80检查通过；旧schema9只读92原件保持。11核心healthy/8数据库/两宿主/7配置保持，395CI四成功/frontend待；写后撤权/Agent、Unknown/来源、递归ACL/删除及59项继续。[证据与边界](qa/CONTAINER_ACL_RESTORE_POST_CHANGE.md)。

- **2026-11-03 ACL恢复写入前Worker强杀/Agent重启实机通过**：产品仍cad6182/1.52不重发，工具12c789c/schema9/version3两新资源真实捕获后恢复至当前ACL原件HDD已验证/目标未写切点。Worker实际SIGKILL后产品0.109秒停helper，原租约86.846秒/41采样保留token/expiry/来源及权威hash；Agent重启0.012秒停helper，PID1214295→1399935、维护1214273保持，重启窗口153次200/healthy-Ready。两原任务Failed/Interrupted、Root Aborted，全源/两类ACL/2旧来源+2当前HDD/8恢复审计/8Job保持，正确恢复wrapper、原上下文退出/独立核验/准确清理/4真PNG及77检查通过；旧schema8只读兼容87原件保持。11核心healthy/8数据库/7配置保持，12cCI四成功/frontend待；写后/Unknown/来源等故障、递归ACL/删除与59项继续。[实机证据与边界](qa/CONTAINER_ACL_RESTORE_RESTARTS.md)。

- **2026-11-03 ACL恢复实时移组/账号停用实机通过**：产品仍cad6182/1.52不重发，工具e77c1a6/schema8/version2在两套新资源真实页面先留自有捕获再恢复，当前ACL原件HDD已验证且未写目标时仅撤销准确临时身份。旧正权限仍余7138.461/7137.025秒，产品4.938/4.858秒结束helper，Canceled/Aborted、完整源/两类ACL保持；原恢复GET均403，移组me200非管理员→forbidden、停用me403→login，引用null/取消POST0，不冒称正常退出。2旧来源+2当前HDD/8恢复审计/8Job、独立Passed/准确清理/4真PNG及73检查/语法通过；旧schema7只读兼容与81份原件保持。11核心healthy/8数据库/两宿主/7配置保持，e77CI四路成功/frontend待；其余恢复故障/递归ACL/删除与59项继续。[实际证据](qa/CONTAINER_ACL_RESTORE_REVOCATION.md)。

- **2026-11-03 ACL恢复写入前取消/正常退出实机严格复验通过**：产品仍cad6182/1.52，无新发布；工具7990b84/40份来源在两套新资源先真实页面产生自有旧ACL捕获，再经历史恢复确认到Applying/Root Preparing且当前ACL原件HDD已验证。取消/原上下文正常双端退出由产品5.434/5.575秒结束准确helper，原任务Canceled/Root Aborted、完整源/两类ACL保持；严格轮2旧来源+2新当前HDD、9恢复审计/8Job、独立Passed/准确清理/3真实PNG通过。初轮物理与独立已过但状态出截图范围的边界保留，新增滚动/视口断言后用新资源复验，未重放旧操作。68相关检查与语法通过，11核心healthy/8数据库/两宿主/7配置保持；799 CI四路成功/frontend待，剩余恢复故障/递归ACL/删除和59项继续。[实盘事实与边界](qa/CONTAINER_ACL_RESTORE_FAULTS.md)。

- **2026-11-03 cad6182 ACL历史恢复已部署且真实常规整链通过**：准确候选CI五路成功，唯一标准update Invocation0efd3714f17b44209562fc73c12e8ffd成功；六组件同源cad6182/1.52，Agent1214295/维护1214273，生产来源迁移入库/18CHECK，currentcad/previous08/pending空。工具冻结2880f7b的新独占资源完成9基础场景及6恢复（5执行/1预检取消），新增5当前ACL原件HDD、23恢复审计，所选旧Root/HDD保持；真实Authentik→Vue→永久PG/Worker→Agent→实际文件、普通/匿名拒绝、两原上下文正常双端退出和准确清理通过。独立只读Passed、22真实PNG、11核心healthy/8数据库与7受保护配置保持；不重跑旧任务抽屉、不算异盘，恢复故障/递归ACL/删除与其余59项继续，整项完成率不升。[实际发布、证据与未验边界](qa/CONTAINER_ACL_RESTORE_REAL_UI.md)。

以下旧日期条目保留各自当时的版本与“未部署/未验”边界，按时间顺序阅读，由上条本批记录接续，不作为当前运行状态。

- **2026-11-03 ACL恢复四镜像预构建已完成，未部署**：运行源码以user从08b2bb8快进冻结cad6182，标准Git archive构建四镜像成功；各实际ID的CLI/build-info及label同源cad6182，Ubuntu慢源依赖正常安装，未删依赖。11运行核心ID/镜像/StartedAt/healthy、Agent684268/维护684263与实际08b2bb8/1.52、账本及7份私密/权限文件摘要保持，活动0、源列不存在/17CHECK。CI四路成功，前端通过新ACL与容器布局组、仍在最后授权/文件/终端组；未启动发布unit/宿主安装/生产迁移或真实恢复。待五路终态再标准覆盖及新独占真实恢复整链，59项整项完成率不升。[基线、构建、失败修正与未验边界](qa/CONTAINER_ACL_RESTORE_PREBUILT.md)。

- **2026-11-03 ACL恢复发布准备：CI身份检查缺口与门禁候选已修，未部署**：d183 CI实际暴露历史helper关闭原FD后仅比dev/inode可被inode复用绕过；5423433改为读取期间pin原FD并对原/新路径完整九字段复核，30历史专项/469旧文件回归通过。542 CI后端/真实PG/Valkey成功，但安装Node测试缺前端依赖、页面80/81误等来源warning后过早刷新；cad6182移至依赖安装后并等待明确错误/原key，2请求测试及5次丢响应复验通过。2fb2a40新增六场景真实恢复主验、独立SQL/Root/HDD和精确清理工具，29证明/8范围/语法通过，另原样本机CI安装门禁54组1533项及5CLI检查通过，**尚未运行真实恢复**。生产仍08b2bb8/1.52及原PID/11核心healthy、来源列不存在/17CHECK；待CI五路成功再覆盖及真实恢复验收，完整故障/递归ACL/删除等59项继续，不升整项完成率。[CI失败、修复及真实门禁边界](qa/CONTAINER_ACL_RESTORE_RELEASE_PREPARATION.md)。

- **2026-11-03 ACL历史恢复管理员API/Vue候选3fd7f5f已接入，未部署**：新增严格五字段来源选择登记及原ID/key查询，复用原ACL确认/取消与永久Worker；Vue从历史快照进入新预检，完整来源与当前/目标ACL、空default、显式确认、原key刷新、迟到/退出/源变化拒绝及共享写入锁已接。冻结Release零警告错误/来源3fd7f5f、4361 Unit/488 Integration（7默认原生跳过）、1059前端Unit及类型/构建/lint/格式通过；HTTP身份/Agent/仓储和页面API为夹具，不记真实恢复。16项恢复与65项关联页面分批通过，联合轮79/81及两脚本修复原件保留，不签单次81全绿；生产仍08b2bb8/1.52与Agent684268/维护684263、11核心healthy不变，新来源列不存在/17 CHECK。尚需正常覆盖与真实身份/文件ACL/当前原件HDD恢复整链、完整故障/递归ACL/删除及其余59项，整项完成率不升。[候选、失败与未验边界](qa/CONTAINER_ACL_RESTORE_WEB.md)。

- **2026-11-03 ACL历史恢复内部业务/永久化候选ffd08a8、冻结47e3ba1通过，未部署**：仅用所选capture/digest派生新的单类型access/default ACL计划与独立执行，完整来源快照纳入请求MAC、18个CHECK及来源不可变触发器，保留原普通MAC/共享锁和写前当前ACL备份语义；规划/确认/首次Apply前复核来源，原Root回执优先对账，来源存在即拒绝破坏性降级。冻结Release零警告错误/来源47e3ba1、4361 Unit/463 Integration（7默认原生跳过）、双隔离真实PG完整1执行/1通过/0跳过及准确清理通过；快照/Root/身份为合成，不算实际恢复HDD。首轮PG已过但记录器拒绝新字段的整体Failed保留，修复3项记录器测试/8范围通过后重跑取得Passed。运行六组件仍08b2bb8/1.52、Agent684268/维护684263与11核心healthy不变，生产新列不存在、仍17 CHECK。管理员恢复API/Vue、部署及真实身份/当前原件HDD恢复整链、故障矩阵/递归ACL/删除和其余59项仍待，整项完成率不升。[实现、失败、证据与未验边界](qa/CONTAINER_ACL_RESTORE_PERSISTENCE.md)。

- **2026-11-03 ACL历史只读常规整链已真实部署验收**：唯一正常覆盖Invocation91e75020130045d58a072f1c360d3c50成功，六组件08b2bb8/1.52同源，Agent684268/维护684263；11核心healthy/8数据库平台及私密配置不变，账本current08b2bb8/previous0478262/pending空。工具17590e8新独占严格轮真实Authentik→Vue→API/10持久PG审计→Agent/HDD完成2页/8快照，源/Root/HDD保持、普通/匿名两历史GET各403/401、原两上下文正常双端退出及独立Passed；36真实PNG和准确清理通过。前两轮工具JSONB类型/50与100响应竞争失败及已完成分项保留，不记整体首轮通过；19证明/2竞争回归/8范围/语法通过。历史恢复、查询中故障、递归ACL、删除与其余59项继续，整项完成率不提升。[部署、失败、严格原件与边界](qa/CONTAINER_ACL_HISTORY_REAL_UI.md)。

- **2026-11-03 ACL历史管理员API/Vue只读候选e83bad0已验证并推送，未部署**：新增准确容器/实例/路径的历史List/Read两GET，实时管理员与原登录在RPC前/输出后/持久审计后复核，严格query/NoStore/数组冻结；复用现有审计表，仅记录路径与实例hash/元数据摘要，无新schema或恢复入口。Vue完整原两类ACL/显式0/mask有效权限、版本分页/空部分页、错误/迟到/退出清理及320小屏明暗滚动齐接。冻结Release零警告错误/API来源e83、4343 Unit/463 Integration（7默认原生跳过）、1042前端Unit、89生产预览（22新+67关联）及类型/构建/lint/格式通过；测试目录/Agent/审计和页面API均为夹具，不算实际Authentik/PG/HDD页面。应用仍0478262、宿主2ef06f0/1.52及561098/561094，11核心healthy不变；下一步正常覆盖与真实业务整链，再历史恢复/递归ACL/删除及其余59项，不提升整项完成率。[候选、失败与验收边界](qa/CONTAINER_ACL_HISTORY_WEB.md)。

- **2026-11-03 ACL历史1.52安装态已通过真实签名RPC/HDD与独立核验**：宿主及原生探针冻结2ef06f0，唯一正常安装Invocation0552122d971a42f8a925b5455217fa89成功，Agent561098/维护561094及映射来源同源；应用仍0478262、11核心ID/镜像/StartedAt不变且healthy，无迁移/全栈发布或回滚。新独占9旧ACL操作留8份HDD，真实List10页/Read8份、29边界拒绝、源/Root/HDD全字节与身份保持、精确清理及独立Passed；实际只读unit仅DAC_READ_SEARCH/SYS_PTRACE与读租约目录可写。错Invocation拒绝/准确停止及stdin EOF两控制分项、旧所有者/两递归签名能力主与独立兼容通过；58 C#专项/8探针proof/构建语法通过，无新前端/Authentik页面。HDD同device非异盘，控制前置不冒充进行中RPC取消；历史业务/API/Vue/恢复及其余59项继续，整项完成率不提升。[真实来源、原件与未验边界](qa/CONTAINER_ACL_HISTORY_INSTALLED.md)。

- **2026-11-03 ACL历史1.52签名RPC/SDK与受限只读传输候选6013aa1通过，未安装**：新增独立List/Read、全请求/方法/Subject/Trace与分页/捕获绑定、一次性短时能力；显式0/false存在位及严格输出校验，SDK必须minor≥52/专属能力，无写入降级。固定helper仅两读取动作，只有读租约目录可写、cap仅DAC_READ_SEARCH/SYS_PTRACE，安装/校验源码接入但生产未运行。冻结Release零警告错误/来源6013、4343 Unit/432 Integration（7默认原生跳过）、58新C#专项/28历史传输root/431旧文件/57维护/5安装预览通过，初编译/旧51断言/无匹配及收集器PATH失败保留。运行仍0478262/1.51、315250/315245及11核心healthy；无新实际RPC/HDD/业务/UI，不复用前批实盘补签。[候选、安全与后续](qa/CONTAINER_ACL_HISTORY_RPC.md)。

- **2026-11-03 ACL历史只读内核候选818396e/891acf4通过，未安装/未恢复**：复用独立container-acl-v1真实HDD原计划，新增完整校验/预期摘要/当前容器路径绑定及有界版本分页，隐藏owner/raw plan/原inode与属主字段；无CLI/写入/恢复或权限扩展。15 Linux-root新专项及431旧容器文件回归通过；冻结候选实际新独占容器5捕获/5查询、7页/独立5HDD-Root及准确清理通过，11核心/宿主0478262/1.51与315250/315245不变，源与HDD同device不算跨盘。库存计数/版本全局，只有条目正文按CID路径过滤，不签多租户统计隔离；签名RPC/C#目录/管理员API/Vue及历史恢复仍待，不把root候选当人类页面验收。[来源、安全、实机分项与剩余](qa/CONTAINER_ACL_HISTORY_READER.md)。

- **2026-11-03 0478262维护确认框修复已部署，真实Auth页面严格复验通过**：真实134fd25确认框实际是英文Cancel/OK（按钮在视口内），修复为中文取消/确认更新/确认回退、取消关闭正常返回及长计划滚动，14生产构建预览/993前端单元/类型构建lint通过；唯一标准覆盖Invocation8c8978188447476e9dd888cef34b1f79成功，六组件0478262/1.51，Agent315250/维护315245，current0478262/previous134fd25/pending空、11核心healthy/8数据库与配置sudo保持。QA37dbe58新账号真实管理员4版本卡/4预检取消、Agent页、普通5路403/两页隔离、两原上下文正常双端退出及6匿名401、11真PNG与独立指纹/准确清理通过，执行POST0；验收器错误role及早期失败原件保留，最终57离线门禁通过。未验安装态强杀/真实应用回退/物理冷启动/新文件HDD写入，P0-05/59项不升整项完成。[修复、真实页面与边界](qa/PLATFORM_MAINTENANCE_REAL_UI.md)。

- **2026-11-03 P0-05新维护工具已安装，134fd25真实正常覆盖及权威历史通过**：七固定来源受控交接不重启原宿主/容器后，只启动一次标准update（Invocation9e1f5d033b5f4538843bcb7a76711663，12:55:03–12:59:31/268秒），六组件及实际进程映射/HTTP均134fd25/1.51，Agent253100/维护253095；真实账本current=134fd25、previous=306f646、pending=null，原错误previous字节留legacy，完成Root回执正确。11核心healthy/8数据库平台ID-镜像-StartedAt不变，配置/能力/sudo保持；47相关测试、安装前后validator、真实锁忙/Agent权限隔离及已安装维护二进制隔离状态验证通过。216实际维护HTTP样本均200但观察器超时空窗保留；**没有新Authentik/文件-HDD页面或物理冷启动/真实手动回退验收**。P0-05和59项仍部分实现，不自动回滚/重启。[安装、真实历史与未验范围](qa/PLATFORM_CONTROL_INSTALLATION.md)。

- **2026-11-03 P0-05维护工具强杀恢复候选1176084/4eb4d15已验证，尚未安装**：新增root私有持久检查点、三固定工具完整核验/原子恢复、独立boot/timer与ExecStopPost，installer共用发布锁且手动恢复保留当前runner；恢复不执行Git/Docker/安装器或应用，不自动回滚。47相关测试、11构建回归、5安装CLI及语法通过；原样模块在两场景隔离systemd沙箱验证强杀退出钩子、锁忙跳过和影子/run丢失后恢复，最终11核心完整身份/StartedAt及两服务PID不变。**合成账本和影子目录不等于生产升级/恢复或物理冷启动验收**；生产仍306f646/1.51、Agent4135740/维护4135726，新工具/账本未安装或创建，旧错误记录保持，P0-05/59项不提升完成。[实现、原件与未验边界](qa/PLATFORM_CONTROL_RECOVERY.md)。

- **2026-11-03 P0-05可信发布记录候选463e19e/1811bd3已验证，尚未安装**：不再把checkout HEAD当上一版，新增root私有原子账本，从六组件实际来源初始化；只在发布完成且实际来源一致时晋升，失败/重试/同版重发保留原记录，手动恢复选择可信记录且不降级数据库。旧previous保留为legacy证据，兼容文件不再是权威；状态原子写入且不跟随目标符号链接，前置拒绝不改已有健康状态。30新隔离测试、11冻结构建/来源回归、5安装CLI及语法通过；包含两个真实临时子进程强杀，不是生产恢复演练。实际只读六组件探测通过，运行仍306f646/1.51、Agent4135740/维护4135726及11核心healthy，生产账本未创建/工具未安装/原记录未改。真实升级、手动恢复强杀/冷启动控制入口及完整P0-05仍待闭环。[实现、失败与真实验收边界](qa/PLATFORM_REVISION_HISTORY.md)。

- **2026-11-03 306f646覆盖及ACL旧版本预检拒绝已实机通过**：原唯一Invocation d38034cfaba24730ac1fe1acae325829在10:50:22成功结束（约1690秒），下载EOF/软件源超时后同次构建完成，无重复发布或回滚；六组件CLI/镜像/HTTP与实际宿主exe/DLL映射一致，Agent4135740/维护4135726、协议1.51，11核心healthy/8数据库不重建/无新迁移，配置和开发sudo保持。冻结2435d44/proofVersion1新资源只改准确FD的mtime，原Vue旧版本被真实预检拒绝：Failed/VERSION_CONFLICT、1规划Job/2审计，执行Job/Root/HDD均0，不能把null说成已修改或未修改的执行回执。15字段SQL/API、320暗色/1440亮色详情、5真PNG、正常双端退出、普通3路403/匿名2路401、独立核验及准确清理通过；89离线本轮通过。剩余执行摘要/共享写入/磁盘路径、ACL递归恢复/删除与59项继续，P0-05旧previous-revision缺陷未修。[部署、真实证据与范围](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

- **2026-11-03 10:44标准覆盖发布进行中，尚未签收**：运行checkout已由user干净快进至306f646，仅启动一次`whaledeck-dev-publish-306f646.service`，Invocation`d38034cfaba24730ac1fe1acae325829`。宿主已为306f646（Agent4135740/维护4135726），API/Gateway仍447070e，11核心容器原ID/镜像/StartedAt保持且healthy，8数据库不变；私密配置与开发sudo摘要保持。容器镜像构建遭遇NuGet EOF与Ubuntu源超时，软件源安装已完成、NuGet仍重试；unit仍activating，运行中的Result=success/ExecMainStatus=0不等于发布成功。正确8080维护入口本段1052次采样均HTTP200/updating，未覆盖整个发布窗口。**无迁移/容器替换成功回执，新真实旧版本预检门禁未开始，不计部署或功能通过**。89离线边界/范围本轮重跑通过；目标59项不变。[唯一发布、原件和接续约束](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

- **2026-11-03 ACL预检失败摘要候选caa9c64已验证，尚未部署**：修正planning/Failed误显示“执行失败”，改为“预检失败，未进入 ACL 修改执行”，执行失败仍保留原语义；版本冲突文案不再假定已完成预检。993前端单元、8生产预览浏览器、89相关离线门禁及类型/构建/lint/格式通过。新增独占文件FD仅改mtime→原Vue旧版本预检拒绝的真实门禁、无执行/Root/HDD断言与独立只读核验，已接CI；**新真实门禁未运行，测试接口/临时文件不能替代Authentik/实际Worker与磁盘整链**。运行仍447070e/1.51、Agent3947643/维护3947551及11核心healthy，无部署/迁移/服务重启；59主项和25%–35%粗估不变。[候选、失败与真实验收待办](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

- **2026-11-03 ACL写后同CID实例变化已实机签收**：QA冻结d2ca69d/proofVersion4，产品仍447070e/1.51，无新增部署。仅重启本案容器一次，CID/镜像/绑定保持、PID4044343→4047348及StartedAt改变；旧计划/回执仍绑定旧实例、Root/SQL Unknown且aclConfirmed=null，实际已写ACL不回滚或续做。不同当前管理员真实320×600暗色页面先暂不处置再明确确认，两次任务摘要“未证明”完整入视口；跨容器目录四次自然延期[2,4,6,8]后同ID才发布。原Root/HDD/迟到Apply不变、3Job/7审计/9真实PNG、独立原件与准确清理、81相关门禁及11核心healthy通过。首轮Docker弃用提示导致断言失败和降级确认已保留，不计整体首轮通过；其他共享写入/磁盘/路径与摘要分支、ACL递归恢复/删除及59项继续。[实际失败、实例绑定与验收边界](qa/CONTAINER_ACL_INSTANCE_CHANGE.md)。

- **2026-11-03 写后目标消失Unknown/处置摘要与447070e小屏修复已实机通过**：前两新资源在612f77c真实写后/Unknown/明确处置已发生，但Ack字段完整视口失败，原件与准确清理保留、不记通过。已补真实长错误码复现，447070e固定详情表格与长值换行并正常覆盖，六组件/HTTP/实际进程映射一致，Agent3947643/维护3947551、协议1.51，无新迁移。第三套全新资源严格门禁及独立复核通过：ACL已变后原容器移除，原Unknown/null/Root/HDD保持，另一管理员先暂不处置再确认，原Job Failed/Acknowledged，Unknown→Ack两任务摘要及“未证明”完整入视口，另一容器目录四次真实延期后同ID发布。75边界、27前端单元与8浏览器、9实际PNG/准确清理/11核心healthy通过；不记整体首轮通过。其他实例/磁盘/共享写入与摘要分支、ACL递归恢复/删除和59项继续。[失败、修复与真实证据](qa/CONTAINER_ACL_POST_UNKNOWN.md)。

- **2026-11-03 ACL写后Agent重启及原任务摘要实机通过**：运行612f77c/1.51不重发，冻结47a1769/schema6的新独占资源在目标ACL已写/Root Applying且未回执时实际重启Agent；产品0.013秒停原helper，Agent3747527→3835700，维护3747520不变/153次HTTP200 healthy-Ready。原任务Succeeded/Completed、完整写后清单/1HDD与迟到同操作Apply不再变化；原预检Ready及执行Succeeded两份SQL/API摘要、320×600暗色真实详情和同上下文正常双端退出通过。独立原件/准确清理、48边界/范围与语法通过，11核心healthy。其余Unknown/实例/共享写入/摘要分支、递归恢复/删除与59项继续。[实机证据与界限](qa/CONTAINER_ACL_POST_AGENT_RESTART.md)。

- **2026-11-03 612f77c已覆盖，ACL任务摘要真实常规整链通过**：标准update唯一Invocation689e10e7f8ba479ba2b0e97de386f33c成功，六组件/HTTP/实际宿主进程映射同源，11核心healthy、8数据库平台不重建；Agent3747527/维护3747520，协议1.51不变，无新迁移。冻结8c39d09的新独占资源九场景8完成/1预检后取消，17新摘要SQL/API/真实任务详情及1旧任务空态通过，原字节/两类ACL/xattr/8HDD、同上下文双端退出与准确清理通过。首轮独立观察器误读输出目录的FileNotFoundError保留，64f6d8b修正证据输入路径后只读复核通过，未重做真实写入。19边界/范围与语法通过；Unknown/处置等摘要分支及写后Agent/实例/跨写入剩余组合、ACL递归恢复/删除与59项继续。[实机证据与界限](qa/CONTAINER_ACL_JOB_RESULTS_REAL_UI.md)。

- **2026-11-03 ACL任务安全摘要候选2491add/9b419c2**：新增预检/执行/Unknown/明确处置的15字段受限摘要，与任务/事件同事务保存；读取校验原意图/执行ID、阶段、状态和严格字段，未知/错误数据不原样返回。Vue展示真实三态、普通模式与历史回执，预检不冒充已修改，处置不冒充恢复；旧任务不补造历史，成功/失败空文案已区分。摘要源码候选与回归已验证，运行仍633a2ca/1.51，未部署、未签收真实身份/文件/HDD页面整链。[候选与验收边界](qa/CONTAINER_ACL_JOB_RESULTS.md)。

- **2026-11-03 ACL写后实时移组/停用实机及独立核验通过**：运行633a2ca/1.51不重发，冻结8d93fbe/schema5/27依赖，两新资源目标ACL已写/Root仍Applying时准确移组或停用原身份；原正快照余7163秒，产品4.806/4.834秒停helper，原任务GET403、移组/me200非管理员并/forbidden、停用/me403并/login，引用键null/取消POST0。原任务按实际事实Succeeded/Completed，原字节/属主/mtime/另一ACL/xattr保持，写后清单/版本及迟到Apply不再变化，2HDD/4Job/8原主体审计与独立准确清理通过。不同当前管理员正常BFF只读GET200核对原终态并HTTP双端退出；后续0b7d43c真实任务中心原执行ID/Succeeded只读页面及正常退出通过，管理写0/原SQL不变；其完整ACL结果摘要仍待。6有效PNG/56边界/语法、11核心healthy通过；连接中断后未重做已完成操作。写后Agent/Unknown/其余组合、递归恢复/删除及59项继续。[实际角色与证据边界](qa/CONTAINER_ACL_POST_REVOCATION.md)。

- **2026-11-03 ACL目标消失Unknown/不同管理员处置与跨容器目录保护严格复验通过**：运行633a2ca/1.51不重发，d977c7a/proofVersion2/33依赖使用两新独占资源，Preparing/HistoryVerified后准确移除原容器、原上下文正常退出，产品4.981秒停helper；原ACL Unknown/null与原HDD/回执MAC/全源保持。另一容器原目录任务四次真实自然延期版本[2,4,6,8]，当前不同管理员320×600先暂不处置再唯一ACK后，原Job Failed/Acknowledged、原Root/HDD及迟到Apply仍Unknown；目录同一ID才实际发布并在Vue视口可见。3Job/7审计/5真实PNG、独立原件/准确清理、52边界/语法及11核心healthy通过。首轮cab4dbc把初始排队误计第四次且目录截图未入视口，已明确拒绝旧Passed、原件留存，非整体首轮通过。其余组合/递归恢复/删除及59项继续。[失败、严格复验和边界](qa/CONTAINER_ACL_UNKNOWN.md)。

- **2026-11-03 ACL写后取消/Worker回执中断实机通过**：运行633a2ca/1.51不重发；冻结517c42a，两新独占资源准确抓到目标ACL已写、Root Applying/尚无完成回执，取消后产品5.313秒停helper，Worker实际强杀后0.123秒停helper/42采样87.079秒等原自然租约。两案原任务最终Succeeded/Completed，不误报未修改或恢复旧ACL；目标ACL与新增ctime实际证明，原字节/inode/属主/mtime/另一ACL/xattr不变，写后清单/版本在迟到同操作Apply重放前后完全保持。2意图/4Job/9原主体审计/2HDD、原上下文正常退出、4真实PNG及独立原件/准确清理与v3兼容通过，54边界/语法通过、11核心healthy。剩余写后撤权/Agent/Unknown/跨写入、ACL递归恢复/删除及59项继续。[实际写后结果与边界](qa/CONTAINER_ACL_POST_CHANGE.md)。

- **2026-11-03 ACL写入前Worker强杀/Agent重启实机通过**：运行633a2ca/1.51不重发；冻结ed241e9，两新独占资源在Preparing/HistoryVerified暂停，Worker实际SIGKILL后产品0.116秒停helper，原token/expiry完全相等，42次真实采样/87.49秒等原自然租约；Agent实际重启后0.015秒停helper，PID3122630→3288068、维护PID3122598不变，152次维护HTTP均200 healthy/Ready。两案原业务Failed/Interrupted、Root Aborted、完整源/两类ACL/xattr与2HDD保留，2意图/4Job/8原主体审计、同上下文正常退出、4真实PNG及独立原件/准确清理通过。6fce7dc补实际Worker见证/严格租约后只读复核仍通过，47边界/语法通过，11核心healthy；变更后/Unknown/跨写入、ACL递归恢复/删除及其余59项继续。[实际重启、租约与边界](qa/CONTAINER_ACL_RESTARTS.md)。

- **2026-11-03 ACL原登录实时移组/停用实机通过**：633a2ca/1.51不重发；冻结f3ca007/27依赖，两新独占资源在Preparing/HistoryVerified且尚未写ACL时，仅准确变更原临时身份的组或active，原正快照余7164秒以上，产品5.195/5.213秒终止绑定helper。原登录旧任务403，移组/me200且非管理员、320暗色/forbidden；停用/me403、1440亮色/login，两案引用键null/取消POST0。2意图/4Job/8审计、Canceled/Aborted、完整字节/九字段/两类ACL/xattr及2永久HDD保留，独立原件/准确清理与旧v1证据只读兼容通过。38边界与语法通过，11核心healthy，除Worker正常启停外核心/两宿主保持；其余重启/变更后/Unknown/跨写入、ACL递归恢复/删除及59项继续。[真实失权与验收边界](qa/CONTAINER_ACL_REVOCATION.md)。

- **2026-11-03 ACL进行中取消与正常双端退出实机通过**：运行仍633a2ca/1.51；冻结78479ec，两新独占资源在Root Preparing/HistoryVerified且尚未写ACL时由真实Vue取消/原用户菜单退出，产品5.433/5.520秒终止精确绑定helper。两案Canceled/Aborted、完整2097159字节与九字段/两类ACL/普通属性不变、2永久HDD/4 Job/9审计、同原上下文双端退出和独立原件/准确清理通过，320×600暗色取消按钮可见。14专项+8范围与语法通过，失败未知ID/Unknown/活动租约清理已加固；11核心healthy，除Worker正常启停外其余10核心及两宿主PID/source不变。尚非ACL变更后中断、撤权/重启/Unknown/跨写入或递归恢复、删除等完整验收；59项保持部分实现。[实际切点、失败与未验边界](qa/CONTAINER_ACL_FAULTS.md)。

- **2026-11-03 ACL已正常覆盖633a2ca，真实页面九场景首轮及独立核验通过**：唯一发布Invocation5761b5620b27452d925f196bb134537e成功，六组件/HTTP/实际宿主映射同源、生产ACL迁移17 CHECK应用，11核心healthy且8数据库平台不变，user NOPASSWD保留。冻结0668215/21依赖，以正常Authentik→Vue→SQL/Worker→Agent→实际ACL/HDD完成8成功/1预检取消，显式ID/权限0、mask/NoChange/default清空、完整2097159字节/原inode-UID-GID-mtime/非目标ACL/子项逐轮核对，8份原HDD与35审计/17 Job、原key/确认重试不重做通过。同原上下文正常双端退出、普通8路403/匿名4路401、CSRF/Origin及缺字段拒绝、9真PNG、准确清理与独立原件核验通过；源/HDD同device不算异盘。14离线及语法通过，前批单元/预览不冒称复跑或新CI全绿；真实进行中故障/Unknown/跨写入、ACL递归/历史恢复、删除及其余59项继续，不计完整ACL。[实际部署、原件和未验边界](qa/CONTAINER_ACL_REAL_UI.md)。

- **2026-11-03 ACL管理员API/Vue候选062e361已通过，尚未部署**：实际永久业务入口与容器文件/全局Unknown处置页面已接；access/default完整条目、显式ID/权限0、mask有效权限/模式、默认清空、原两类ACL/执行身份/不可变原回执与确认/取消/原key恢复齐接。根/条目/确认/处置严格拒绝缺失、重复、额外和错误类型，保留实时权威身份/原登录/CSRF/Origin与十方保护；首登记执行回复只允许原GET核实。4272 Unit/432 Integration（7默认原生跳过）、972前端Unit、Release零警告错误及类型/构建/lint/格式通过；43 ACL和156关联生产预览浏览器分两组全通过，18明暗PNG及5175实际字节来源核验通过。真实桌面页脚越界已修正，脚手架/断言失败保留；**测试身份/API/仓储/Agent不是Authentik/实际ACL/HDD业务证据，无发布或生产迁移**。下一步正常冻结覆盖与真实常规/故障整链，再ACL递归/恢复、删除和其余59项。[实现、失败和验收边界](qa/CONTAINER_ACL_WEB_UI.md)。

- **2026-11-03 ACL永久SQL/十方共享保护与专用Worker候选已通过真实PG，未部署**：72d678e新增EF仓储/迁移17个CHECK、双Job/Outbox/审计、90秒租约/CAS与原回执独立MAC原子保存/不可改；既有八类容器写入及主机锁双向路径/发布/预算保护齐接。c9c9265补通用Worker专用消息排除与通用重试拒绝，最终4272 Unit/364 Integration（7默认原生跳过）、Release零警告错误及10范围/状态门禁通过。冻结72d678e双隔离真实PG完整1执行/1通过/0跳过，独立原件/准确清理、11核心不变且healthy/8数据库平台不变、生产ACL表不存在通过；原回执/身份/时钟为合成输入，**不是实际Worker/Authentik/ACL文件HDD或页面证据**。无发布/回滚，前批4a58961/1.51安装RPC事实保留；下一步严格管理员API/Vue及正常覆盖后真实业务常规/故障整链，ACL递归/恢复、删除与其余59项继续。[候选、失败和证据边界](qa/CONTAINER_ACL_PERSISTENCE.md)。

- **2026-11-03 ACL内部业务候选41f02fb已验证，尚未接永久仓储/页面**：新增独立意图、原登录/实时授权/幂等ID、请求/计划/执行/确认/回执分域HMAC；ACL列表深复制与逐条比较、default显式清空/mask模式、三态与不可变原回执、先Inspect原ID/Started后不重做及Unknown明确处置已接内部编排。Release零警告错误、4270 Unit/363 Integration（7默认原生跳过）及8范围通过；首轮夹具Unknown回执未保留的1失败已修正并保留。**仓储仍是显式内存测试夹具，无EF/迁移、共享保护实现、Worker/API/Vue或真人业务验收，本批未发布**；4a58961/1.51实机RPC/HDD保持前批事实，不重做、不当作本批业务证据。下一步具体永久SQL/双向写入保护和隔离真实PG，再API/Vue及真实身份文件/HDD整链；ACL递归/恢复、删除和其余59项继续。[候选、失败与边界](qa/CONTAINER_ACL_WORKFLOW.md)。

- **2026-11-03 ACL Agent1.51已安装，真实签名四RPC/HDD及旧能力兼容通过**：正常覆盖后宿主实际4a58961，Agent PID2818452/MaintenanceHost PID2818448；API/Worker/Gateway仍b1fafed，11核心自安装前ID/镜像/StartedAt不变且healthy。新ACL运行4a/工具224f744分列，9操作(8完成/1执行前Discard)、13预检/4执行绑定拒绝、8HDD原ACL元数据、完整2097159字节/原inode-owner-mtime/非目标ACL/子项及每轮重放核对通过；首个旧ID真实签名Apply不重写。主/独立准确清理通过；旧所有者6操作、递归chmod4/递归chown4及各自独立HDD兼容通过。真实入口历史类错误和探针非法篡改预期错误两次失败均在ACL写入前，已修复、保留Failed及准确恢复；NuGet EOF标准重试成功，无缓存/凭据注入或审计降级。469 Linux-root/97 ACL C#/3探针/12宿主/5安装CLI/8范围及语法通过；最终维护healthy/Ready、idle/transient0、user NOPASSWD保留。仍无永久ACL业务/共享保护/Worker/API/Vue或真人页面，同设备非异盘。[安装、原件和剩余边界](qa/CONTAINER_ACL_AGENT_RPC.md)。

- **2026-11-03容器ACL独立RPC1.51/SDK/固定传输候选6a62027通过，未安装**：新增四unary方法、完整请求/方法/单次短时capability独立签名、显式ID/权限presence、原access/default ACL与目标mask模式/回执绑定；SDK严格协商major1/minor≥51及专属能力，旧chmod/chown不扩大。固定stdin/unit/准确Invocation停止、独立状态和安装/验证接入；无CAP_CHOWN/SYS_ADMIN，初始user namespace及准确FD statmount BY_FD证明非IDMAP，不支持/错误直接拒绝。冻结Release零警告错误且编译Agent来源6a62027；4132 Unit(96新)、363 Integration(7默认原生跳过)、468 Linux-root(12新)、12宿主/5安装CLI/8范围及语法通过。首轮2测试编译错误/只读汇总sudo PATH错误保留并修正。实际只读apps FD依赖支持、11核心及宿主PID/b1fafed来源不变且healthy、idle0；当前ACL文件/状态未安装，真实签名RPC0，无业务/Worker/API/Vue或真人页面，不重复ed50实盘。下一步原生探针/正常宿主覆盖及真实RPC-HDD拒绝/兼容，再永久业务页面/ACL剩余/删除及59项。[完整候选与边界](qa/CONTAINER_ACL_RPC.md)。

- **2026-11-03记录：容器ACL底层候选ed50dc1已通过真实容器/HDD，未安装**：2026-11-02冻结源码新增独立FD access/default ACL、显式mask/数值ID、64项预算、独立原ACL HDD历史及只观察恢复；写后确认/恢复须原inode fsync成功，持续EIO保留Unknown。22新专项纳入456项Linux-root全套零跳过、8范围与语法通过；实际setuid/组权限、default继承、6隔离强杀切点有证据。新独占容器/HDD候选首次9操作(7完成/2中止)、2候选SIGKILL、8份HDD元数据/完整2097159字节/原inode-owner-mtime普通属性及迟到不重写通过，独立核验准确清理/11核心与宿主PID来源不变且healthy通过。产品仍b1fafed/1.50，未签名RPC/永久业务/Worker/API/Vue或真人页面；同设备历史非异盘，namespace映射须接入前明确。下一步C#协议/SDK/固定受限传输及安装态验证，再完整业务页面、ACL剩余范围/删除及其余59项。[实现、实际依赖与边界](qa/CONTAINER_ACL_ENGINE.md)。

- **2026-11-02递归所有者目标消失Unknown及跨容器目录延期/释放实机通过**：产品保持b1fafed/1.50，无发布/回滚；冻结003f504及a6f472b/各21依赖，两套新组合首次实机和独立核验均通过。准确首项fchown后只移除自有CID，源树保留，原管理员正常双端退出，产品2.755/2.197秒结束原helper；均汇总0/0/0/0/18待证明、物理1项实际修改，不能推断零修改。不同新管理员真实320×600五计数/暂不处置/明确确认及原Unknown/Root/HDD/完整源/迟到不改通过；第二例另一容器原目录任务版本2/4/6/8自然延期，23:38:46.298696处置后原ID才于23:38:53.318649实际Root写入并Succeeded、Vue列表可见。9真PNG/12哈希审计/4HDD元数据、准确清理与最终idle0、11核心healthy/8数据库平台不变通过。56递归+8范围和语法通过；仅跨容器目录组合、同设备历史，不扩大其他写入/异盘或未保存的历史文件系统重算结论。[事实与剩余边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_UNKNOWN_BARRIER.md)。

- **2026-11-02递归所有者真实组撤权/账号停用与Partial五计数页面通过**：产品保持b1fafed/1.50，无发布/回滚；冻结cf291aa/21依赖，两套新独占资源首次实机门禁及各自独立核验通过。首项实际fchown后只调整本轮临时身份，正快照仍余7161.151/7162.322秒，产品3.951/4.255秒结束原helper；旧任务403，移组后me200非管理员/403页，停用后me403/登录页，取消POST0。均原Partial 1完成/1修改/1中止/16未开始/0待证明，另一管理员真实320×600页面逐项五计数、先暂不处置再唯一确认及正常双端退出通过；原Root/HDD/完整源/迟到不改、准确清理、10哈希审计/10真PNG及11核心healthy/8数据库平台不变通过。旧页面只验证当前region/sessionStorage提示清理，不泛称全部缓存；撤权不记正常退出。56递归+12身份+8范围及语法通过，附加只读汇总器tuple/list错误保留后修正，不重做操作。Unknown/跨写入、ACL/删除及其余59项继续。[事实、原件及未验边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_REVOCATION.md)。

- **2026-11-02递归所有者Worker强杀/原自然租约及Agent重启/维护入口实机通过**：产品b1fafed/1.50不发布不回滚，冻结0442fb4/21依赖，两套新资源首次门禁及独立核验通过。首项fchown后准确Worker SIGKILL137/同CID镜像重启，产品2.255秒结束helper，40样本/83.985秒自然等待原租约后原ID恢复Partial；仅固定Agent真实重启PID2380326→2542378、维护PID2380322不变，17次入口采样200/healthy/Ready/no-store，产品2.341秒结束helper。两例均1完成/1修改/1中止/16未开始/0待证明、不续做，当前另一管理员真实页面处置/正常退出、Root/HDD/完整源与迟到不改及准确清理通过；10哈希审计/10真PNG、11核心healthy/8数据库平台不变。56递归+8范围及语法通过；原租约观测新增有界拒绝，不编辑SQL/时钟。实时撤权/Unknown/ACL/删除及其余59项继续。[原件、边界与恢复证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_PROCESS_RESTARTS.md)。

- **2026-11-02递归所有者真实进行中取消/正常双端退出与Partial处置通过**：产品保持b1fafed/1.50，不发布/回滚。冻结51e0ec3及21依赖，两套新资源首次真实门禁与各自独立核验通过；首项实际fchown后页面取消/同上下文正常双端退出由产品5.012/4.339秒结束原helper，均保留Partial（1完成/1修改/1中止/16未开始/0待证明）。退出前后原账号启用/组/身份与原CID/PID/StartedAt不变，正快照余7157秒、取消POST0、旧任务401/私有引用清空。不同管理员真实小屏先暂不处置再确认，原Job Failed/Acknowledged，Root/HDD/完整源事实不改、迟到不续做；11哈希审计/10真实PNG、准确清理/11核心healthy/8数据库平台不变通过。55递归离线+8范围及语法已接CI；同设备历史非异盘，旧六场景退出Failed保留。其余故障/ACL/删除/59项继续。[完整事实与未验边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL.md)。

- **2026-11-02递归所有者b1fafed已覆盖，真实页面六场景/源树HDD分项签收**：唯一正常发布Invocation32e9789056bf4728b58f189d160abefe成功，六组件/HTTP/实际宿主映射同源，生产迁移应用、11核心healthy且8数据库平台不变。正常Authentik→Vue→SQL/Worker→Agent完成5成功/1预检取消，显式0:0、零修改、完整2097159字节/原inode/模式/mtime/普通属性与5聚合/20子历史逐轮核对；23原主体审计、原key重试/刷新不重做。**原总脚本最后退出检查失败保持Failed**；观察器改为等实际OIDC导航/会话终态，另身份纯登录退出补证401/403，无文件修改。原六轮信号/SQL/Root/HDD重新核对并准确清理、独立只读核验通过，未重复所有者操作，不伪造原上下文双端退出。17离线门禁通过，17移出不算完成。进行中故障/同上下文退出、Partial/Unknown、ACL/删除和其余59项继续。[真实事实、失败与恢复边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_REAL_UI.md)。

- **2026-11-02递归所有者管理员API/Vue候选a806f56已验证，未部署**：严格显式UID/GID、原登录/实时身份/幂等登记、原树预检/确认/取消/原引用恢复及当前管理员Partial/Unknown处置已接；原根属主/目标/执行身份、五计数、历史证明与当前观察分列，容器文件/全局异常入口与旧写入互斥、退出清理保留。4036 Unit、363 Integration（7默认原生跳过）、909前端Unit及Release零警告错误/类型/生产构建/lint/格式通过；44项生产预览递归所有者及112项关联页面浏览器分两组全部通过。首轮旧夹具令牌断言失败与准确中断保留，**测试身份/API/仓储/Agent不是实机业务整链**；本轮未发布/未改生产schema。下一步正常冻结覆盖及真实Authentik→Vue→SQL/Worker→Agent→实际树/HDD常规和故障整链，ACL/删除与其余59项继续。[候选、失败和证据边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_WEB_UI.md)。

- **2026-11-02递归所有者永久SQL/九方保护/专用Worker候选a19f5c4通过真实PG，未部署**：新增EF仓储/迁移与16个CHECK、双Job/Outbox/事件/审计、90秒租约/CAS、原请求/计划/执行与Root计数历史不可改；九方双向数据库/路径/操作预算覆盖主机锁和八类容器写入，Partial/Unknown保留保护、只允许原ID明确处置，不自动续做/回滚。冻结Release零警告错误、4036 Unit/309 Integration（7默认原生跳过）、10范围/回执门禁通过；双隔离真实PG全套首次1执行/1通过/0跳过，独立准确清理/11核心ID镜像启动时间不变且healthy/生产新表不存在通过。编译与测试入口失败保留；Root/身份/时钟为合成输入，**不是实际Worker进程、Authentik、文件/HDD或页面证据，本轮未发布**。宿主22906f5/1.50、应用0dede08前批安装事实保留；下一步管理员API/Vue及正常冻结覆盖后的真人树/HDD常规与故障整链，ACL/删除和其余59项继续。[实现、失败与验收边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_PERSISTENCE.md)。

- **2026-11-02递归所有者内部业务编排候选7a94733已实现，未接永久仓储/Worker/API/Vue**：新增独立意图、原登录/幂等ID、请求/整树计划/执行/确认分域HMAC，原属主/目标UID-GID/执行身份分列；显式0与缺失拒绝分开，当前权威身份及原登录在读写返回前复核，执行期间每6秒检查授权/取消/租约。Inspect优先、已开始不续做、Partial/Unknown保留计数与保护，当前管理员明确处置使用原执行ID。并修复两种递归Dispatcher在身份依赖失联时延迟可信Partial/Unknown/Aborted回执的分支；仅保存已核对事实，不放宽写入授权。冻结Release零警告错误、4034 Unit/308 Integration（7默认原生跳过）、8范围检查通过。**当前只有内部候选和测试夹具，尚无具体EF仓储/迁移、九方共享保护、Worker注册或真实业务页面，本轮未部署**；上一批宿主22906f5/1.50与应用0dede08安装证据保留。下一步永久SQL/双向保护和隔离真实PG，再API/Vue/正常覆盖及真实身份树/HDD故障整链；ACL/删除和其余59项继续。[候选、验证与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_WORKFLOW.md)。

- **2026-11-02递归所有者Agent1.50已安装，真实签名四RPC/HDD通过**：冻结22906f5正常宿主覆盖，实际Agent PID2113282/MaintenanceHost PID2113274同源22906f5，标准validate/运行库/SHA/动态入口/user NOPASSWD保留通过。新原生四RPC在准确HDD树完成4操作(3完成/1执行前Discard)，含显式root 0:0、11预检/5执行绑定拒绝、完整2097159字节/原inode/mode/mtime/普通属性、每次重放原树与Root不改，3聚合/12子项HDD和独立准确清理通过。另旧递归chmod4操作及旧单对象所有者6操作与各自独立核验通过；11核心跨安装/RPC ID/镜像/状态/StartedAt不变且healthy，API/Worker/Gateway仍0dede08。两个构建收尾失败及旧核验参数错误保留，未重复已完成构建或真实操作。**仍无本功能永久EF业务/共享保护/Worker/API/Vue或真人登录页面**；同设备历史不计异盘灾备，CI四路成功/前端运行中不冒称全绿。下一步永久业务及真实身份页面/故障矩阵，ACL/删除与其余59项继续。[安装态证据与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)。

- **2026-11-02递归所有者RPC/SDK候选2d3b7e0通过，未安装**：独立Plan/Apply/Inspect/Discard、完整请求/方法/单次capability签名、显式HasUid/HasGid（0不等于缺省）、五计数/历史/绑定校验与SDK major1/minor50专属能力协商已接，固定stdin/unit/准确Invocation停止、安装依赖和CI检查已登记。3904 Unit（106专项+4新增EOF）、308 Integration（7默认原生跳过）、434 Linux-root及12 host artifact检查通过，冻结Release零警告错误。新unit有CAP_CHOWN，旧chmod未扩大。**仍无永久业务/Worker/API/Vue或真实签名UDS/HDD验收**，运行0dede08/1.49、两宿主原PID/source及11核心healthy/上一实盘baseline不变，只读预检0，无发布/回滚。下一步宿主正常覆盖与真实四RPC/HDD/拒绝/兼容，再永久业务及真实身份页面；ACL/删除与其余59项继续。[完整候选与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_RPC.md)。

- **2026-11-02递归所有者底层候选ef5425a通过真实容器/HDD**：新增显式UID/GID、有界双遍清单与独立聚合/子项历史域，复用单对象fchown与永久回执，Partial/Unknown只观察恢复、不续做。421项容器文件Linux-root（16新专项）零跳过通过；冻结10模块新资源4操作(2完成/1部分/1中止)、2次真实候选子进程SIGKILL、完整2097159字节/原inode/mode/mtime/普通属性与4聚合/9子项HDD和迟到不重做通过，独立准确清理/11核心不变且healthy通过。共享框架默认chmod的另4操作/2强杀及独立HDD回归也通过；schemaVersion严格整数加固未覆盖线上。**未安装/无签名RPC/永久业务/Worker/API/Vue或人类页面**，产品仍0dede08/1.49，不发布/不回滚；同设备元数据不计异盘灾备。下一步C# RPC/SDK/固定传输与安装实测，再永久业务及真实页面；ACL/删除和其余59项继续，17移出不算完成。[候选证据与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_ENGINE.md)。

- **2026-11-02递归进行中正常双端退出实机通过**：产品保持0dede08/1.49、不发布/不回滚。冻结8dc8d55及20份运行依赖，新资源首次真实门禁及独立核验通过。首项实际chmod后正常可见用户菜单完成BFF/Authentik双端退出，产品4.257秒结束原helper、取消POST0；退出前后原账号仍启用且组/Subject/PK不变、原快照尚余7157秒，原容器CID/PID/StartedAt不变。旧任务HTTP401、旧region/引用清空；原ID保留Partial(1完成/1修改/1中止/16未开始/0未知)，切点/退出后/恢复后全树相同，不续做。不同管理员真实明色小屏逐dt/dd核对五类计数，先暂不处置再确认，原Job Failed/Acknowledged、Root/HDD/完整源事实保留，迟到不重做。5哈希审计/5真实PNG/52检查通过，11核心healthy及8数据库平台不变；同设备历史不计异盘灾备。继续递归所有者/ACL/删除和其余故障/59项。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_LOGOUT.md)。

- **2026-11-02递归进行中实时组撤权/账号停用两例实机通过**：产品保持0dede08/1.49、不发布/不回滚。冻结3c76ecc及20份运行依赖，两套新资源首次真实门禁及各自独立核验通过。首项实际修改后只改准确临时Authentik账号，原正快照仍剩约7160秒；产品4.116/4.113秒结束原helper，无取消POST，原实例保留。旧任务均HTTP403，移组后me200/admin=false及forbidden，停用后me403及login，旧任务region/引用清空。两例原ID保留Partial(1完成/1修改/1中止/16未开始/0未知)，切点/退出后/恢复后全树相同，不续做；不同管理员真实页面先暂不处置再确认，原Job Failed/Acknowledged、Root/HDD/完整源事实保留，迟到不重做。共10哈希审计/8小屏PNG/51检查、11核心healthy及8数据库平台不变；撤权不计正常双端退出，同设备历史不计异盘灾备。其余切点/退出、递归所有者/ACL/删除和59项继续。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_REVOCATION.md)。

- **2026-11-02递归进行中Agent重启/维护入口17次观测通过**：产品保持0dede08/1.49，不发布/不回滚。冻结6d257a2门禁首次完整通过，首项实际修改后只重启固定Agent，PID1432315→1851523且实际apphost同源，MaintenanceHost PID1432309/source不变。重启前至结束后17次本机维护状态HTTP200/healthy/Ready/no-store，产品2.290秒结束原helper；切点/退出后/恢复后整树相同，原ID/计划恢复Partial(1完成/1修改/1中止/16未开始/0未知)，不续做。不同管理员真实页面处置/迟到不重做、完整文件/Root/HDD及独立核验通过，5哈希审计/5小屏PNG/11核心healthy、8数据库平台不变。后续QA异常收尾finally加固以离线注入验证，共30检查；非再次真实重启/启动失败。其余切点/退出/撤权、递归所有者/ACL/删除与59项继续。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_AGENT_RESTART.md)。

- **2026-11-02递归Unknown跨容器目录真实延期/释放整链通过**：产品保持0dede08/1.49，不发布/不回滚。冻结7e8af47新组合通过，原Unknown18/实盘1修改保留保护时，另一独占容器目录原Job在Worker运行下观测到至少4次真实延期，版本2/4/6/8、延期时刻各异、无租约/目标目录/Root回执；排除其他任务/锁/终端。另一管理员真实页面先暂不处置再确认，原异常17:47:25.137157处置后，原目录Job才于17:47:25.353502开始并Succeeded，唯一登记、实际inode/1000:1000/0750及Vue列表可见。独立核验、5递归+2目录哈希审计、5小屏PNG、26检查及11核心healthy/8数据库平台不变通过；原件/HDD不变、两套资源准确清理。首轮Queued/Pending QA错误与正常失败清理保留。仅签收跨容器新建目录组合，其他类型/故障及递归所有者/ACL/删除等继续。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN_BARRIER.md)。

- **2026-11-02递归目标消失Unknown不同管理员真实处置通过**：产品保持0dede08/1.49、不部署/不回滚。冻结300d19e门禁首次完整通过，首项实际修改后仅移除准确自有容器，源树保留；原页面正常双端退出由产品2.757秒结束原helper。SQL/Root为0完成/0已证明修改/0中止/0未开始/18待证明并保留保护，宿主侧独立证实1项修改，不能把Unknown汇总0推断为物理零修改。原账号删除后另一管理员320×600真实页面先暂不处置再确认，原Job Failed/Acknowledged、Root仍Unknown、计数/HDD/全文件不改；迟到原固定helper不续做（追加探针非签名RPC）。独立核验、5条哈希审计/4 PNG/24项检查/11核心healthy及8数据库平台不变通过。同设备元数据不计异盘灾备；其他组合、Unknown期间其他写入延期/释放、递归所有者/ACL/删除与59项继续。[完整证据和未验边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN.md)。

进度与代码量口径见[当前比例及规模估算](PROGRESS_ESTIMATE.md)：40/59（67.8%）为部分实现覆盖，不是完成率；整项签收0，整体工作量粗估25%–35%。非空产品源码不含迁移约9.49万行、测试/验收/工具约9.99万行；剩余新增产品约14.5万–29.5万行，加测试工具约26.5万–54.5万行，仅作规划，不替代验收。

- **2026-11-02递归Worker强杀/原自然租约/Partial真实页面通过**：产品保持0dede08/1.49，不部署/不回滚。冻结23704b9的新资源在首项实际修改后准确SIGKILL原Worker（137），同容器/镜像重启；产品2.329秒结束原helper，Root可先保存Partial，SQL40次观测保留原token/expiry/保护/Job及文件指纹，实际83.827秒等原16:51:41.333302+08:00到期后才恢复。原任务1完成/1修改/1中止/16未开始/0未知，恢复未续做；原账号删除后另一管理员真实页面先暂不处置再明确确认，原Job Failed/Acknowledged、Root/HDD/完整文件保留。独立只读核验、5条哈希审计、5真实320×600 PNG、22项检查与11核心healthy/8数据库平台不变通过。两轮QA失败与准确清理保留；其余组合/Unknown/ACL/删除与59项继续。[证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_WORKER_CRASH.md)。

- **2026-11-02递归进行中取消与Partial不同管理员真实处置通过**：产品保持0dede08/1.49、不重复部署。冻结c154d0a门禁在18项独占树第一项真实修改后捕获原helper，真实320×600页面取消由产品4.943秒结束helper，得到1完成/1修改/1中止/16未开始/0待证明，Partial保留保护与原计数。原账号删除后另一管理员实际页面先暂不处置再明确确认，原Job Failed/Acknowledged、保护释放；原Root/HDD/全文件字节与元数据不改，迟到固定helper不续做（该追加探针非签名RPC）。增强独立核验3fbbd45、6条哈希审计、4真实PNG、11核心healthy及8数据库平台不变通过；只有同一Worker容器按预检停启。首轮QA误要求未处置Outbox为0的失败和准确清理保留，第二轮新资源通过，20项探针/范围检查通过。同设备元数据不计异盘灾备；Unknown及退出/撤权/强杀/重启等组合、递归所有者/ACL/删除与其余59项仍待。[证据与未验边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_PARTIAL.md)。

- **2026-11-02递归普通权限0dede08已覆盖，真实身份/目录树/HDD常规页面6场景通过**：唯一发布Invocation f396efd8f67f4c0ca9e02a48315dc9f2成功结束，六组件/HTTP及实际宿主进程同源，生产迁移入库、11核心healthy且8数据库平台不重建。冻结85d7d11验收器以正常Authentik登录完成5成功/1执行前取消，含0变更、显式0000/0777、每场景实盘完整字节/九字段/混合属主/mtime/xattr、5聚合/20子项HDD、6登记/5确认原HTTP重试及刷新、匿名/普通拒绝/CSRF/Origin/退出，独立只读核验准确清理通过，23原主体哈希审计/11唯一Job通过。6真实PNG及320×600取消可见性已核验；同设备元数据保留不计异盘灾备，进行中故障及业务Partial/Unknown仍待。发布源码0dede08的CI五路最终全部成功（41402013851），user NOPASSWD保留，不自动回滚；其余59项/递归所有者/ACL/删除继续，17移出不算完成。[实机证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_REAL_UI.md)。

- **2026-11-02递归普通权限管理员API/Vue候选2056191与验收器f706c86通过，未部署**：已接原树异步预检/明确文件与目录模式/确认/原引用接续取消、Partial/Unknown计数与HDD历史/观测区分及当前管理员处置，原登录/实时权威身份与写入互锁保留。3794 Unit、308 Integration（7默认原生跳过）、832前端Unit、Release零警告错误及类型/构建/lint/格式通过；生产预览44递归+104关联分批通过，148联合首轮147通过/1个null测试断言失败与修复记录保留，不冒称单次148全绿。小屏/桌面动作可见、过期提示和编辑禁用已修，截图/源码/构建摘要核验通过；测试接口/身份/仓储与Agent夹具不是实机业务证据。宿主仍e668dbe/1.49，应用d1fcca8；下一步正常冻结覆盖与真实Authentik→Vue→SQL/Worker→实际目录树/HDD及故障整链，递归所有者/ACL/删除/长期清理和其余59项继续，17项移出不算完成。[候选与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_WEB_UI.md)。

- **2026-11-02递归普通权限永久SQL/八方保护/专用Worker候选cca8ec9通过，未部署**：真实EF迁移/双Job/Outbox/审计、90秒租约、原请求/计划/执行/回执计数与历史不可改、Partial/Unknown保留保护且不自动Claim、原ID明确处置及旧七方双向数据库/路径/预算保护已接。3794 Unit、259 Integration（7默认原生跳过）、Release零警告错误及10项范围/回执门禁通过；冻结cca8ec9双隔离真实PG全套1执行/1通过/0跳过，独立准确清理、原Unknown锁不改、11核心门禁期间不变且healthy、生产候选表不存在。四轮失败/中断保留，Root/身份/时钟为合成输入；不是实际树/HDD/UI证据。宿主仍e668dbe/1.49，应用仍d1fcca8；下一步接管理员API/Vue、实时身份及真实树/HDD页面与故障整链，递归所有者/ACL/删除/长期清理及其余59项继续，17项移出不算完成。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_PERSISTENCE.md)。

- **2026-11-02递归内部业务候选71b6334通过，回执丢失保护已覆盖e668dbe**：独立请求/计划/执行HMAC、确认与实时授权、Partial/Unknown保留计数/保护及原ID处置已实现，尚未EF仓储/迁移/八方保护/Worker/API/Vue。3792 Unit（本批114新）、258 Integration（7默认原生跳过）、405 Linux-root及Release零警告错误通过。修复递归及单对象chmod/chown缺失Root时误报Aborted：先查可信HDD/子项，遗留即Unknown，依赖不可用不作缺失。宿主最终e668dbe/1.49正常覆盖；递归4+chmod6+chown6共16个真实签名操作及各自独立HDD/原件/清理通过。递归准确自有回执实际丢失、原件保留、Discard/迟到Apply Unknown且全树/HDD不变与独立核验通过；单对象对应丢失组合仍需安装态验收。应用容器仍d1fcca8、11核心不变且healthy，59项继续，17项移出不算完成。[候选、修复及边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_WORKFLOW.md)。

- **2026-11-02递归普通权限Agent1.49已安装，真实签名RPC通过**：宿主冻结95c0787正常覆盖，真实进程/10.0.13/UDS/权限/HDD/动态入口通过；API/Worker/Gateway仍d1fcca8，11核心容器未重建。原生四RPC在准确HDD树完成4操作（3完成/1执行前取消）、8预检/3执行绑定拒绝，完整2097159字节/原inode/属主/mtime/属性、逐次重放源九字段/Root原件不变、3聚合/12子项HDD与独立准确清理通过。旧单对象权限6操作兼容与独立核验通过，最终任务/终端/锁/屏障与未结束意图预检0、11核心healthy。安装正常重启两宿主，RPC期间PID稳定；未真实人类/永久业务/Worker/API/Vue，仍需完整链、进行中故障、ACL/删除及其余59项，17项移出不算完成。[实机证据及边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_AGENT_RPC.md)。

- **2026-11-02递归普通权限签名RPC/SDK候选aead344通过，未安装**：独立完整请求签名与四RPC、强类型DTO/回执计数、能力1.49协商、固定stdin helper/准确Invocation停止及安装/校验已接，未新增HTTP/Vue入口。3678 Unit（93新专项）、258 Integration通过（7默认原生跳过），33递归/395关联Linux-root零跳过、12宿主artifact/8范围与Release零警告错误通过。复核补SIGTERM逐项取消与Unknown正计数，真实子进程信号后Partial不续做通过；非真实UDS/身份/实盘签名证据。运行仍最后记录d1fcca8/1.48，25281a1内部实盘保持原批次；继续宿主覆盖/原生RPC、永久业务/Worker/API/Vue及真实身份树/HDD全链、ACL/删除和其余59项，17项移出不算完成。[候选与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_RPC.md)。

- **2026-11-02递归普通权限内部候选25281a1通过真实HDD**：有界乐观清单/独立HDD聚合与子项历史、最深项先执行、逐项永久回执和Partial/Unknown只观察恢复已实现。20专项/382关联Linux-root零跳过、8范围与语法检查通过；冻结独占容器4操作（2完成/1部分/1中止）、2次候选真实SIGKILL、每文件完整2097159字节及原inode/属主/mtime/属性、4份聚合/9份子项HDD和迟到重放不变均通过，独立复核准确清理/11核心不变且healthy。失败与修复保留。未安装/helper/RPC/永久业务/Worker/API/Vue，不算真实身份页面或完整递归权限；运行仍d1fcca8/1.48，继续全链/更多故障、递归所有者/ACL/删除及其余59项，17移出不算完成。[证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_ENGINE.md)。

- **2026-11-02所有者实际变更后取消/Worker回执中断实机通过**：产品保持d1fcca8/1.48，不重部署。精确原helper仍Applying且owner未确认时，原inode已为目标UID/GID、属性/HDD及完整字节符合；真实页面取消与Worker SIGKILL均由原操作恢复Succeeded/Root Completed（退出5.321/0.113秒），不误报未发生或自动恢复。Worker原自然租约保持；迟到Apply重放保持Root/版本/ctime不变。两例及独立SQL/Root/HDD/原件和准确清理通过；8新/16原root边界及宿主检查通过。仍需其余组合、递归/ACL/删除等，59项active。[证据与边界](qa/CONTAINER_OWNERSHIP_POST_CHANGE.md)。

- **2026-11-02所有者d1fcca8已覆盖，真实组撤权/账号停用及正常授权回归通过**：六组件/镜像/HTTP/宿主进程同源，数据库平台未重建。原正快照未过期时，只改本轮临时Authentik身份，产品分别5.221/5.177秒结束helper；原接口拒绝、403/登录实际页面与提示清理、SQL Canceled/Root Aborted、原完整文件/HDD及独立准确清理通过。常规7场景同版本重跑与独立核验通过。首轮截图仅BootScreen已保留并补强后重验；不冒称其他模块即时撤权或全部BFF注销。12新边界/16原观察器通过；继续变更后/回执丢失、递归/ACL/删除和其余59项。[实机证据及边界](qa/CONTAINER_OWNERSHIP_REVOCATION.md)。

- **2026-11-02所有者实时权威身份候选通过，未部署/未实机撤权**：发现6秒巡检原来仍读取2小时快照，新增独立Authentik权威接口与显式DI；所有者预检/执行/恢复校验绕过缓存和claims回退，核对uuid/is_active/真实组，拒绝停用或失联并保留原登录检查。3585 Unit、258 Integration通过（7默认原生跳过），Release零警告错误；新增11个身份Stub边界和4个所有者断言包含在全量中。普通页面快照与其余模块保持，运行仍为最后记录e91a770；继续发布及真实组撤权/账号停用、Root/HDD/页面整链。[实现和验证边界](qa/CONTAINER_OWNERSHIP_LIVE_AUTHORITY.md)。

- **2026-11-02补齐2026-10-31所有者真实故障与小屏修复记录**：d5e62ca实际页面取消/退出、Worker SIGKILL自然租约、Agent重启四例及独立SQL/Root/HDD/原文件核验通过；Unknown不同管理员真实处置、等待目录保护与迟到Apply不重做也通过。目视发现小屏按钮问题后，e91a770已正常覆盖并重验Unknown，确认/取消完整可见，六组件同源、11核心healthy（对应当时检查）、数据库平台未重建。新16/原33+12 root、8范围、类型/构建/lint/格式及32+8分批浏览器通过；两类验收器/夹具失败保留。仍需撤权/变更后等故障、递归/ACL/删除及其余59项；记录补齐不冒称当日重跑。[完整证据与边界](qa/CONTAINER_OWNERSHIP_FAULTS.md)。

- **2026-10-31所有者d5e62ca已覆盖，真实身份/文件/HDD常规页面通过**：六组件/镜像/HTTP与宿主实际进程同源、生产所有者迁移入库；同一次发布208.435秒成功，11核心healthy且数据库平台未重建。正常Authentik登录后7场景（含0:0/无变化/恢复原属主/目录/320×600取消）为6成功1执行前取消，原登记7/原确认6重试与刷新、权限/CSRF/Origin/双端退出通过。SQL/6 Root/6份物理HDD元数据、完整2097159字节/原inode/模式/mtime/子项和准确清理由独立进程复核通过；活动/屏障/租约/临时身份0。验收器Invocation读取与root索引副作用已说明并修复，未重复发布或回滚。所有者进行中故障/Unknown、递归/ACL/删除等仍待，59项active。[实机证据及边界](qa/CONTAINER_OWNERSHIP_REAL_UI.md)。

- **2026-10-31所有者管理员API/Vue候选与小屏取消修复通过，未部署**：显式UID/GID与稳定私有输入错误、原引用预检/确认/取消、当前管理员Unknown处置及身份清理/互锁已接。3581 Unit、247 Integration通过（7默认原生跳过），755前端单元、34所有者生产预览（含320×600明暗取消和原确认不可改写）、89关联浏览器及Release/类型/构建/lint/格式通过。小屏正文内滚动保留底部动作，已复核取消可见；过期提示与原确认不可改写校验也已修正。浏览器为明确测试接口，不冒称真实Authentik/文件/HDD整链。尚未覆盖发布，继续真实身份/实盘与故障验收、递归/ACL/删除及其余59项。[候选、修复与验证边界](qa/CONTAINER_OWNERSHIP_WEB_UI.md)。

- **2026-10-30所有者永久SQL/七方保护与Worker候选通过真实隔离PG，未部署**：ef08f76/ac5c0f3实现独立意图表/EF迁移/永久双Job/Outbox/审计/租约、七方双向发布保护/路径与预算及专用Worker，禁止通用重试/Outbox接管。3581 Unit、214 Integration通过（7默认原生跳过），Release零警告错误；冻结98bdcca真实PG1执行/1通过/0跳过并清理，合成回执不冒称Root。修正旧最新降级断言且独立保留所有旧守卫，原失败记录保留；独立确认11核心不变且healthy、3测试容器和临时库0。管理员API/Vue与真实身份/实盘/HDD页面仍待，运行版本未改。[完整证据及失败](qa/CONTAINER_OWNERSHIP_PERSISTENCE.md)。

- **2026-10-30所有者内部业务编排候选通过，未部署**：独立请求/计划/执行及确认签名、原登录/幂等/期限、取消/租约/Unknown与不重Apply、原inode属主/执行身份/目标UID/GID分离已实现。3579 Unit全部通过（新增101），全方案Release零警告错误；Repository/Gateway为明确内存夹具，不算真实PG/实盘/页面。EF仓储/七方保护/专用Worker注册/API/Vue和真实身份整链继续，运行版本未改。[候选与验证边界](qa/CONTAINER_OWNERSHIP_WORKFLOW.md)。

## 当前目标与最后记录状态（2026-11-03）

当前目标以[可复制目标正文与59项主表](GOAL_P0_P5.md)为准，包含完整C#酒馆、统一供应商、AI驱动Live2D、272ST/6PET父级和16个子门槛，以及原有UI/所有者/递归/ACL/删除等全部任务。17项移出不恢复，文档更新不算功能完成。

当前API/Worker/Gateway/Migrator与Agent/MaintenanceHost均为`306f646`（协议1.51）；原唯一发布已成功，旧版本预检Failed及真实摘要已独立签收，生产schema保持原633a2ca的ACL迁移/17 CHECK，无新迁移；真实常规9场景/源-HDD/35审计、同原上下文正常退出及独立准确清理通过，见[ACL实际记录](qa/CONTAINER_ACL_REAL_UI.md)。递归所有者/权限此前常规与取消/退出/重启/撤权/Unknown跨容器目录签收分别保留；ACL已补执行前留历史的取消/正常退出/实时移组与停用/Worker强杀自然租约及Agent重启，此前Agent PID3288068、维护PID3122598，此前正常覆盖后为3747527/3747520，后续ACL写后Agent真实重启后为3835700/3747520，前批447070e正常覆盖后为3947643/3947551、本次306f646为4135740/4135726；另已补ACL写后取消/Worker回执中断的Succeeded对账与迟到重放；已补原容器消失Unknown及另一容器目录四次延期/不同管理员处置；已补写后实时移组/停用与新当前管理员只读API；已补不同当前管理员任务中心历史Succeeded只读页面；完整ACL结果摘要2491add/9b419c2已随612f77c覆盖，常规17新摘要与18真实详情、独立原件复核通过，Unknown/明确处置的有回执摘要已随本批写后目标消失实机签收，无回执/绑定不可信等摘要仍待；写后Agent重启及原成功任务摘要已实机签收；写后目标消失Unknown及目录延期保护已签收；实例变化Unknown已由d2ca69d冻结QA在447070e实机签收；其他写入保护、递归/历史恢复，另继续容器可恢复删除/长期清理、目录/实例/跨盘剩余故障、其他模块即时撤权、统一UI、网站/开发空间、C#酒馆/供应商/Live2D及59项完整验收；不按分项提升整项完成率。下方旧候选/运行版本和待办均按原批次理解。

下方是按批次保存的历史实施日志。其“当前/最新/下一步”、运行版本、70/76项与旧HOLD仅属于原记录日期；发生冲突时以上述目标和最新对应版本证据为准，不从旧日志恢复移出任务或重复发布。


- **2026-10-30供应商与Live2D计划已细化，未实现/部署**：新增[专项契约](AI_PROVIDERS_LIVE2D.md)，统一看板娘AI→C#酒馆→参考Cindy的集中供应商配置；正常对话页必须真实Live2D，AI选择动作/表情后经服务端校验及Vue执行。补充用途/能力/Secret、模型发现/显式测试、动作目录/绑定事件/播放租约、音频同步、睡眠/用户优先级、停止/退出/重启/晚到cue恢复等16个子门槛；272ST/6PET和59主ID保持。只读核对Cindy固定公共源码/帮助与Live2D官方文档，未迁移真实账号、调用模型、下载SDK/模型或改变运行版本；现有所有者/递归/ACL/删除计划保留。

- **2026-10-30完整酒馆与看板娘已纳入现有阶段，仅调研/设计，待实现**：新增[.NET10/C#重构与Vue集成方案](AI_TAVERN_MASCOT.md)、[272个ST+6个PET逐项账本](AI_TAVERN_ACCEPTANCE.md)及[dsh-pet固定源码事实](research/DSH_PET_REFERENCE.md)。酒馆206核心/随包能力、66目录扩展全部建账；14随包模块不重复计数，66扩展尚未逐仓库审计/运行。后端迁移权威提示/世界书/历史编排，前端完整聊天/配置与动画/交互物理/碎碎念/对话/余额/三段睡眠；8批实施及权限、安全、失败恢复、实际依赖与真实UI门槛已登记。59个主ID不变、P4-08/P0-08/P5-05子范围扩大，17个移出主项不恢复；旧仅调研声明已覆盖。没有AI产品代码/数据库迁移/模型请求或部署验收；现有所有者/递归/ACL/删除和运行版本不由此次文档修改改变。

- **2026-10-30 前端UI风格已纳入当前计划，未实现/未部署**：根据用户四张明暗参考图，新增[视觉规范](UI_STYLE_GUIDE.md)及原图，归属既有P0-08；DEV-UI-001共享样式→002指标卡→003任务/变更摘要与当前P1/P2页面→004全页真实浏览器回归。目标清单、路线、页面规范及产品/设计索引同步；59项不增加。保留现有主题/顶栏/响应式、权限、真实数据及确认/Unknown事实；所有者等原功能任务继续。此条只记录计划，不能作为UI实现或实机验收证据。

- **2026-10-30 所有者独立引擎与Agent1.48安装态通过，业务/UI待接**：新增显式UID/GID存在位、独立完整请求签名/四RPC、SDK版本能力门禁及单独CAP_CHOWN固定helper；旧chmod未扩权。3478 Unit/213 Integration（7默认原生跳过）、103新增专项、362容器文件Linux-root及Release/探针零警告错误通过。冻结532ceb9实际HDD/NVMe文件/目录20操作、8专属候选强杀、完整2MiB+7/原inode/模式/mtime/子项及20份HDD元数据独立核验准确清理通过；2a9ceb6宿主安装成功，运行exe/DLL映射同源。真实签名UDS6操作5Completed/1Aborted、5份HDD/6 Root与独立精确清理通过，旧权限6 RPC回归通过，11核心不变。Agent/MaintenanceHost为2a9ceb6/1.48，API/Worker/Gateway仍cb4c400；永久意图/七方保护/Worker/API/Vue及真实身份页面仍待，不计完整所有者功能完成。59项active，17项移出不算完成。[实施恢复点与证据](qa/CONTAINER_OWNERSHIP_AGENT_RPC.md)。


- **2026-10-30 普通权限真实页面故障五例通过**：产品仍cb4c400/1.47，无重复部署或回滚。真实页面取消/正常退出、Worker准确SIGKILL与自然租约、Agent实际重启，产品结束helper分别5.457/5.433/0.121/0.024秒；原文件完整2097159字节和九字段指纹不变，SQL双任务/原主体审计/Root Aborted及物理HDD元数据独立核验通过。准确临时容器移除后Unknown保留null/历史/保护；原账号删除，另一管理员320暗色页面先取消再明确处置，原执行Failed、Root仍Unknown，迟到Apply不重做；另一容器目录只在释放保护后创建，唯一新管理员审计通过。独立准确源/CID/身份清理、活动/租约/屏障0、11核心healthy。12新观察器/35权限传输Linux-root与8范围通过；首轮Future等待、Unknown换行帧的验收器失败保留，完整重验通过。所有者/递归/ACL、删除及剩余故障仍待，59项active、17项移出不算完成。[实机故障证据](qa/CONTAINER_PERMISSION_FAULTS.md)。


- **2026-10-30 普通权限cb4c400已覆盖并通过真实页面常规整链**：六组件/镜像/HTTP同源、运行进程映射与权限迁移核对通过。真实Authentik登录后，桌面/320明暗6场景（文件变更/无变化/恢复、目录变更/恢复、Ready取消）、原登记6次/原确认5次HTTP重试、刷新原引用与正常双端退出通过；5 Succeeded/1 Canceled，独立SQL/Root/HDD五份元数据、完整2097159字节/原inode/UID/GID/mtime/子项及准确清理通过。普通8路403/匿名4路401、CSRF/Origin拒绝，活动/屏障/租约/临时身份0、11核心不变。前两轮关闭抽屉选择器、第三轮执行身份与源属主混淆均为验收器失败，原证据保留且准确清理，第四轮完整命令0及独立只读复核通过；产品未回滚。此次重跑3375 Unit/213 Integration（7默认原生跳过）、695前端、35 Linux-root权限/传输、8范围与类型/生产构建通过；GitHub CI仍执行，不签全绿。完整权限和故障矩阵继续，59项active、17项移出不算完成。[发布与真实页面证据](qa/CONTAINER_PERMISSION_REAL_UI.md)。


- **2026-10-30 容器普通权限永久业务与Vue候选已生成**：0d31641已接三段独立业务签名、永久双Job/SQL/Outbox/审计、不可变模式/HDD事实、六方双向排斥、专用Worker/管理员API、预检确认/原引用接续/取消与Unknown页面。3375 Unit/213 Integration（7默认原生跳过）、Release零警告错误，冻结13c0f2a隔离真实PG1执行/1通过/0跳过、独立准确清理与11核心不变；695前端单元、28权限/72关联生产预览浏览器及类型/构建/lint/格式通过，320暗色/1440明色截图复核。SQL回执与浏览器API为明确夹具，不冒称真实身份/实盘业务；候选未覆盖，Agent/MaintenanceHost仍c1f3cd7/1.47、API/Worker/Gateway仍a59ec97。继续冻结发布与真实身份/权限/实际文件/故障整链及其余59项，17项移出不算完成。[SQL证据](qa/CONTAINER_PERMISSION_PERSISTENCE.md)、[页面与边界](qa/CONTAINER_PERMISSION_WEB_UI.md)。


- **2026-10-30 容器普通权限Agent1.47安装态已通过**：冻结c1f3cd7的专用强类型Plan/Apply/Inspect/Discard、整请求单次签名、SDK能力门禁与受限固定helper已安装。3280 Unit/209 Integration（7默认原生跳过）、91新增专项、329容器Linux-root零跳过、74安装/8范围基线及Release零警告错误通过。真实签名UDS完成文件改模式/无变化/恢复、目录改模式/恢复、永久取消共6操作（5Completed/1Aborted）；完整内容/原inode/UID/GID/mtime/子项、5份物理HDD元数据与6份Root原件通过，第二进程只读独立核对和准确清理通过，原目录RPC实机回归通过，11核心不变。Agent/MaintenanceHost为c1f3cd7，API/Worker/Gateway仍a59ec97；永久业务SQL/共享屏障/Worker/API/Vue及完整权限/删除继续，不冒称公开权限页面已完成。59项active，17项移出不算完成。[证据与边界](qa/CONTAINER_PERMISSION_AGENT_RPC.md)。


- **2026-10-30 容器普通权限内部候选通过真实HDD/NVMe**：原inode非递归模式变更、固定HDD元数据快照/独立域、永久原回执与只观察不重做已生成。26新/320关联Linux-root零跳过；冻结996a25c实际两设备文件/目录20操作（16完成4中止，8真实SIGKILL）、完整2MiB+7/原inode/UID/GID/mtime/子项及20份HDD元数据通过，独立只读核验准确清理/11核心不变。首轮卷命名被保护拒绝，修夹具不放宽白名单，失败另证准确清理且不改记通过。未安装/helper/RPC/永久业务/API/Vue，仍a59ec97/minor46；chown/递归/ACL/删除等保留，59项active、17项移出不算完成。[证据与边界](qa/CONTAINER_PERMISSIONS_ENGINE.md)。

- **2026-10-30 目录移动部分源留存Unknown真实页面通过**：实际a59ec97，16份共33554544字节文件的目标已完整发布、1份原件转存/15份仍在源时准确pidfd中断；SQL/Root保留true/null/null与发布保护。原临时身份删除、另一管理员320暗色页面先取消再一次确认，原Job失败/Acknowledged而非移动成功；另一容器写任务仅解除保护后成功。晚到原固定helper Apply仍Unknown，原Root/完整目标及分布两处原件不变；严格准确清理，独立复核活动/屏障/租约/临时身份0、11核心healthy。9新/14原观察器及47移动root回归通过；同HDD、非全部交换/撤权/跨盘验收。59项active、17项移出不算完成。[完整证据](qa/CONTAINER_MOVE_UNKNOWN.md)。

- **2026-10-29 容器移动实际部分文件中断四例通过**：运行a59ec97不重部署；真实BFF明确取消/正常退出、Worker SIGKILL与原自然租约、Agent systemd重启均在已登记部分私有inode时通过，产品结束helper分别4.008/4.041/0.116/0.268秒。完整134218176字节源SHA及9项原指纹不变、目标未发布、SQL/Root三事实false，原任务/回执/审计保留；第三进程独立核对四例与准确清理，活动/屏障/租约/临时身份0、11核心healthy。14项root观察器测试接CI；不冒称可见页面中断、目录树发布/源留存/Unknown或跨盘业务已验。59项active、17项移出不计完成。[证据与边界](qa/CONTAINER_MOVE_INFLIGHT.md)。

- **2026-10-29 a59ec97容器移动真实常规整链通过**：真实Authentik用户名/密码登录，1440/320明暗四次文件/目录移动、四次原登记HTTP重试与刷新原意图、Ready明确取消通过；独立SQL双任务/Root原件、完整2097159字节、混合UID/GID/普通属性与原文件inode匹配。普通8路403/匿名4路401、CSRF/Origin拒绝及双端退出通过。准确测试容器/源和临时身份清理，4 Succeeded/1 Canceled永久历史保留，活动/屏障/租约0、11核心healthy；只读第三进程重核通过。此次实际业务同HDD，不冒称跨盘；进行中取消/撤权/重启/Unknown等仍待，59项active、17项移出不计完成。[实际证据与边界](qa/CONTAINER_MOVE_REAL_UI.md)。

- **2026-10-29 容器移动a59ec97已正常dev覆盖，真实身份整链待验收**：同一发布Invocation的journal成功结束，六组件/镜像/HTTP来源一致；宿主实际可执行文件与DLL映射也为当前安装，新增移动迁移/表已入库。11核心healthy、database-platform未重建，活动移动/屏障/租约0，两个新增GET匿名401；托管容器运行库及PKCS来源分别核验10.0.13。3189 Unit/209 Integration（7默认原生跳过）、冻结实际PG1执行通过及独立清理、655前端和生产预览78/78保持。尚未真实管理员/普通用户、实际业务移动/页面与故障验收；59项active、17项移出不计完成。[发布与边界](qa/CONTAINER_MOVE_DEPLOYMENT.md)。

- **2026-10-29 容器移动永久SQL/Worker/API/Vue候选已生成，尚未覆盖**：迁移与不可变三态事实、Job/Outbox/审计、五方双向发布屏障、专属Worker和禁止通用重试已接通。冻结73fa4dd真实隔离PG1执行/1通过/0跳过及独立准确清理、11核心不变；首三轮测试问题保留并修正。Release零警告错误、3189 Unit/209 Integration通过（7默认原生跳过）；655前端单元及生产预览78/78、类型/构建/lint/格式通过，320明色/1440暗色目视复核。待冻结覆盖与真实身份/实际磁盘/故障整链；宿主仍53701f8/minor46，API/Worker/Gateway仍7b3f44e。59项active、17项移出不算完成。[SQL证据](qa/CONTAINER_MOVE_PERSISTENCE.md)、[页面证据](qa/CONTAINER_MOVE_WEB_UI.md)。

- **2026-10-29 容器移动Agent1.46已安装签收，业务候选继续**：53701f8宿主707文件匹配、签名UDS两类完整移动/取消及复制回归通过；e839a43已安装固定helper三次HDD↔NVMe完整移动/混合所有者与独立Root摘要、准确清理及11核心不变通过。失败观察器现场保留，单备份命名目录属主定点修复。新业务模型/三层签名/调度候选新增96项，全量3187 Unit/205 Integration通过（7默认原生跳过），Release零警告错误；尚未SQL/Worker注册/API/Vue。API/Worker/Gateway仍7b3f44e，59项active、17项移出不计完成。[安装态证据](qa/CONTAINER_MOVE_AGENT_RPC.md)、[业务边界](qa/CONTAINER_MOVE_WORKFLOW.md)。

- **2026-10-29 容器移动独立计划/永久回执候选c2c78b7：Move独立摘要域、完整内层绑定、三态发布/移除/原件事实、仅观察不重做已实现；目录混合UID/GID保留，复制属主规则保持，内层摘要漂移使当前绑定失效。19专项含9次测试子进程SIGKILL、389容器Linux-root/35宿主移动/27复制通过；冻结真实Docker三次跨文件系统2MiB+7、混合UID/GID、原外层回执/原件/清理及11核心不变独立验证通过。未安装/RPC/业务SQL/Worker/API/UI，仍7b3f44e/minor45；59项active、17项移出不算完成。** [证据及首轮失败](qa/CONTAINER_MOVE_INTENTS.md)。

- **2026-10-29 容器移动内部FD候选d48aaae：独立源/目标/私有原件与操作ID绑定、两端可写、父inode及固定FD校验已实现；17专项/368关联Linux-root零跳过，冻结真实非root独占容器完成HDD→NVMe文件/目录和NVMe→HDD文件3次完整2MiB+7、原件inode/普通属性/重试验证，准确CID/命名卷/源清理及独立复核通过、11核心保持。未安装/RPC/永久业务任务/Worker/API/UI，运行时仍7b3f44e；继续外层容器域协议与整链，59项active、17项移出不计完成。** [证据与边界](qa/CONTAINER_MOVE_SCOPE.md)。

- **2026-10-29 容器复制部分文件中断通过：真实BFF取消/退出、Worker SIGKILL/自然租约及Agent systemd重启共4例，原SQL任务/Root原件及准确清理独立复核，零活动/租约/屏障、临时身份0、11核心healthy；12项root观察器测试通过。验证器15秒退出时钟与30秒服务drain已分开，原子回执读取改为固定FD，失败轮保留。上午属主事故定点修复546对象及Agent应用根，六类数据库临时生命周期复验通过；六类实际HDD备份与校验也通过。产品仍7b3f44e，CI五作业最终success；目录树故障/Unknown可见页面/其余59项继续，17项移出不算完成。** [中断与失败证据](qa/CONTAINER_COPY_INFLIGHT.md)，[属主事故](qa/RUNTIME_OWNERSHIP_REPAIR.md)。

- **2026-10-29 7b3f44e真实容器复制常规整链通过，并修复动态入口双端退出**：实际浏览器正常Authentik登录，1440/320明暗四次文件/目录复制、刷新原意图、四次原登记HTTP重试、Ready明确取消、普通8路403/匿名4路401通过；独立SQL双任务/Root原件/完整2MiB+7/属性与准确清理匹配，5意图4成功1取消，零活动/租约/屏障，临时身份0、11核心healthy。首轮退出Authentik400仍200，修复精确批准issuer虚拟Host后，伪造hint400保持会话、正常双页退出WD401/Authentik拒绝通过；2996 Unit/205 Integration（7默认原生跳过）及真实YARP回环通过。进行中取消/撤权/Worker或Agent重启/Unknown真实故障整链与其余59项继续，17项移出不算完成。[证据与失败](qa/OIDC_DYNAMIC_LOGOUT.md)。

- **2026-10-29 容器复制页面/API/Worker已覆盖7c3facd，真实业务整链待验收**：584前端单元、28复制及77关联生产预览页面通过；42复制/294关联Linux-root零跳过。发布单元成功0，六组件及HTTP同源，新复制迁移入库、11核心healthy。Agent独立实际签名UDS已通过全字节/属性/永久取消与准确清理；真实登录页面到SQL/Worker/Root的复制及撤权/重启/Unknown整链仍待，CI40801153829未签终态。59项active，17项移出不算完成。[实际证据](qa/CONTAINER_COPY_WEB_UI.md)。

- **2026-10-29 容器复制页面候选与 Agent1.45 安装态通过**：新增两阶段复制/原引用恢复/显式取消/Unknown管理员处置页面，584前端单元、28生产预览浏览器及检查构建通过；42复制专项与294关联容器Linux-root全部执行零跳过。冻结ab406eb已安装Agent/MaintenanceHost，实际签名UDS两次完整文件/目录复制与永久取消通过，独立Root原件/属性/准确清理及11核心healthy。验证器相对模块路径失败已修；关联77页面回归通过；API/Worker/前端覆盖、真实身份SQL/Worker/UI整链仍待。59项active，17项移出不算完成。[证据与边界](qa/CONTAINER_COPY_WEB_UI.md)。

- **2026-10-28 容器复制 SQL/Worker/API 候选通过隔离真实 PG**：`e911452` 实现永久意图/EF migration、双 Job/Outbox/审计、共享发布排斥、不可变历史、专用 Worker 与管理员 API。2970 Unit/205 Integration 通过（7 默认原生跳过）；`a6156a0` 冻结真实 PG 1 执行/1 通过/0 跳过，包含并发确认、四方双向触发器、取消/租约/Unknown/保留，独立清理复核及11核心healthy。首轮夹具保留锁导致 Started 断言失败，修正顺序并保留全部新旧保护；未放宽锁。Agent1.45、API/Worker 尚未部署，复制 UI/真实身份与实际文件整链仍待。59 项 active，17项移出不算完成。[证据与失败记录](qa/CONTAINER_COPY_PERSISTENCE.md)。

- **2026-10-28 容器复制异步业务候选已生成，未持久化/部署**：`24238fb` 新增请求/完整计划/确认执行三层独立签名、永久意图实体与事务仓储契约、异步服务和六秒复核调度。HTTP 不扫描/复制，Ready 明确确认后独立执行；原 ID 丢回复只观察或封闭，Unknown 不释放保护。新增 86 项，2968 Unit/201 Integration 通过（7 原生默认跳过）；进行中取消与租约丢失各真实等待一次六秒并取消夹具 RPC。SQL/EF、跨写入排斥、Worker/API/真实 UI 与安装态尚待，不把内存测试算落库。59 项 active，17项移出不算完成。[证据及下一步](qa/CONTAINER_COPY_WORKFLOW.md)。

- **2026-10-28 容器复制 Agent 1.45 候选已接通，未安装**：`6be0751` 新增独立强类型四 RPC、完整请求体签名、版本/能力门禁、SDK 及固定受限 helper/安装源。Release 零警告错误，2882 Unit/201 Integration 通过（7 原生默认跳过），TerminalProbe 编译通过。当前复制 Python 42 项仅 2 通过/40 非 root 跳过；进程 NoNewPrivs=1 阻止 sudo，未覆盖运行服务。前序 CI 四路成功、前端 408/1 的抽屉入场测量已修，开发/生产预览各 18 项通过，新整条 CI 待终态。实际安装态 UDS、永久业务任务/共享屏障/Worker/API/真实 UI 仍待，59 项 active，17项移出不算完成。[证据与边界](qa/CONTAINER_COPY_AGENT_RPC.md)。

- **2026-10-28 容器复制独立回执候选实机通过**：f1edb43新增容器独立计划/永久回执、元数据观察、固定传输和不重做的恢复。40复制专项与294原容器文件Linux-root均全执行通过；真实Docker九项、七次自有候选子进程SIGKILL，4 Completed/3 Aborted/2 Unknown，完整字节/树摘要、属性、容器ETag及Root原件匹配。两个准确CID/来源清理，11核心healthy；尚未安装/RPC/业务SQL/Worker/API/UI。继续Agent签名协议、固定helper及完整业务链路，59项active，17项移出不算完成。[证据及边界](qa/CONTAINER_COPY_INTENTS.md)。上一轮元数据原文CI失败已修，3d7db9f五作业全success；不代表本候选CI已通过。

- **2026-10-28 Prepared真实进程中断与容器复制基础推进**：安装态仍5f3622d/minor44；Worker SIGKILL/同CID显式启动及自然90秒租约、Agent实际重启分别通过，helper由产品0.674/0.165秒退出，维护PID不变，原任务均Failed/Interrupted、Root Aborted。独立SQL/Root/准确清理：24任务13成功/3取消/4失败/4Acknowledged，活动/租约/屏障0、11核心healthy。[过程与失败保留](qa/CONTAINER_DIRECTORY_PROCESS_RESTARTS.md)。容器复制新增固定root FD适配器，19专项/292关联及CI原294容器Linux-root全执行通过，冻结b8466a7在真实独占Docker容器完成两类全量复制/属性/重试，以及发布前实际目标消失Unknown/不重做，两个精确容器与来源清理；两轮验证器失败残留已私有转存未删除。复制尚未安装/RPC/业务/UI，继续原59项；17项移出不计完成。[候选证据与后续](qa/CONTAINER_COPY_SCOPE.md)。

- **2026-10-28 目录发布准备故障/Unknown真实页面与进行中撤销通过**：运行仍5f3622d/minor44，产品源码无差异，未重复构建部署。pidfd同时绑定准确容器root FD/原操作锁FD，实际Preparing目标消失与Prepared非空对象均自然Unknown保留屏障；另一容器两次延后。原身份删除后不同管理员真实320暗色页面取消再确认，旧Job Failed、原绑定/Root原件保留，只有解除屏障后等待任务才创建；存活原容器的固定Root helper迟到Apply仍Unknown。Prepared中明确取消5.312秒、正常退出5.387秒由产品主动终止helper，Root Aborted/目标不存在；分别Canceled和Failed/Interrupted，不冒报同一终态。294 Linux-root/零跳过及独立SQL/Root/准确清理通过，20总任务12成功/3取消/1中断/4Acknowledged，活动/Unknown/租约/屏障0、临时身份/unit0、11核心healthy。保留报文缺换行那轮失败，不把Root固定入口冒称新增签名RPC。交换/Worker/Agent进行中组合及其余59项继续，17项移出不计完成。[证据与边界](qa/CONTAINER_DIRECTORY_UNKNOWN.md)。

- **2026-10-28 目录排队重启/取消/正常退出实机通过**：产品运行时仍5f3622d/minor44，未重复构建部署。b7dc949验证实际4次Worker停止启动与1次Agent重启，维护入口PID不变；API持续可访问，永久原任务两成功/两取消，Root Completed/Aborted、SQL/实际目录inode属性匹配。成功轮次准确源/CID/临时身份清理，独立10总任务8成功2取消、活动/Unknown/租约/屏障0、11核心healthy。第一次Agent观察exec时序错误已修，原任务成功且无生产服务故障；未冻结原inode的唯一原始测试文件转存root私有证据，未放宽删除门禁。Root发布中强杀/撤权/Unknown真实页面及其余59项仍待，17项移出不计完成。[证据与边界](qa/CONTAINER_DIRECTORY_RESTARTS.md)。 运行时5f3622d的CI40611513488已最终五作业全部success；不代表之后QA提交的CI也已执行。

- **2026-10-28 容器新建目录已覆盖并通过真实页面常规链路**：实际5f3622d六组件/HTTP同源，更新0/SUCCESS、3分35.989秒，新永久目录迁移入库。真实管理员1440/320明暗共4次中文目录、0750/0700/0755、取消零登记/同HTTP重试原任务/刷新只查/两页完整注销通过；独立SQL/Worker/Root计划指纹、实际空目录inode/1000:1000/权限匹配，普通8路403/匿名401、缺CSRF400/错误Origin403通过。精确测试源/容器/临时身份清理、永久历史保留；4成功、活动/租约/屏障0、11核心healthy。2802 Unit/201 Integration（7默认跳过）、522前端、50联合页面及补充目录10项、冻结真实PG1执行通过并清理。CI40611513488四路success、frontend仍运行。实际业务重启/进行中取消/Unknown跨管理员页面及其他容器写入/隔离/留存仍需继续；59项active，17项移出不算完成。[证据及失败记录](qa/CONTAINER_DIRECTORIES.md)。

- **2026-10-27 容器目录Agent minor44已安装验收**：82850b2宿主/固定helper与SDK独立签名四RPC已完成，API/Worker/Gateway保持852ca6f。实际UDS中文目录/内核所有者/不重复创建、永久取消拒绝迟到写入、篡改/错误域/nonce重放/核心/只读根拒绝通过；独立Root回执/源码/运行PID、准确资源清理、11核心healthy与原活动/Unknown/屏障0通过。Release零警告错误、2784 Unit/199 Integration（7原生默认跳过）、287容器Linux-root/12宿主检查通过。更新整unit尾部探针权限失败已准确记录，安装与host validate成功，剩余探针发布独立0/SUCCESS；不重复安装或自动回滚。永久目录业务SQL/Worker/API/UI及实际身份页面仍待，59项active，17项移出不算完成。[证据与剩余](qa/CONTAINER_DIRECTORIES.md)。

- **2026-10-27 容器新建目录底层候选b43fedf，未安装**：精确实例/父版本/普通模式/内核所有者计划、私有准备与原子不覆盖发布、永久回执和只观察恢复已实现。23新增/279容器Linux-root、8范围检查通过；真实独占Docker容器正常创建及四个实际SIGKILL切点均通过，准确资源清理、独立回执/精确CID/源不存在和11核心healthy通过。已安装852ca6f/minor43不变，RPC/持久任务/Worker/API/UI尚待，59项继续active，17项移出不算完成。[证据及后续](qa/CONTAINER_DIRECTORIES.md)。

- **2026-10-27 852ca6f 发布准备故障和明确目标消失已实机通过**：正常dev覆盖0/SUCCESS、3分18.741秒，六组件/HTTP同源与宿主检查通过。Root私有副本实际8617984字节且覆盖前HDD已Verified后强制重启Worker，原任务Unknown保留全局屏障，另一应用Ready真实延后两次；原身份删除后不同管理员真实手机UI一次计划/取消确认/再确认、schema4封存，只有解除保护后原等待任务才发布成功，目标/属性与128MiB+7来源及2MiB+7备份完整核对。明确Docker404修复在真实删除容器后10.448秒进入TargetUnavailable/Unknown，零接收/发布、独立管理员UI封存；两整链正常退出与准确清理通过。独立79意图56成功30恢复(属性16)、活动/Unknown/租约/屏障0、三类身份0、11核心健康。2720 Unit/199 Integration(7默认原生跳过)、256 Linux-root/496前端/41接管浏览器/8范围检查通过；CI40518574767四路success、前端运行。首次失败均保留并正常收尾，全部59项继续active，17项移出不算完成。[证据及边界](qa/CONTAINER_RESTORATION_PUBLICATION.md)。

- **2026-10-27 2e13376 真实恢复进程重启与发布前能力复核通过**：正常dev覆盖0/SUCCESS、2分54.329秒，六组件/HTTP同源及宿主检查通过。排队恢复经Worker/Agent实际重启后完整134217735字节/4属性发布一次；Root实际收到64KiB后Worker SIGKILL显式启动、32KiB后Agent重启，均自然对账Failed/Discarded、目标/来源不变、发布0。修复Stage后能力丢失仍会申请发布屏障的问题，针对性用例修前失败修后通过；同任务恢复能力后不重Stage只发布一次。随后当前网页4次2097159字节/两宽明暗/正常双端退出及普通五路403通过；独立核对72意图53成功28恢复(属性14)、活动/Unknown/租约/屏障0、身份/准确测试资源0、11核心健康。2718 Unit/199 Integration(7默认原生跳过)、冻结真实PG1执行通过并清理；CI40509563766四路success、前端运行。两次观察器失败及收尾明确保留，不宣称自动拉起或完整59项完成；17项移出不算完成。[证据与边界](qa/CONTAINER_RESTORATION_RESTARTS.md)。

- **2026-10-27 d8822c6 显式属性恢复已覆盖并实机通过**：六组件/HTTP同源、正常dev覆盖成功、实际新增属性策略迁移及10.0.13/UDS/权限/动态入口通过。真实网页4次各2097159字节的原路径/其他路径新建覆盖，SQL永久true策略/Root schema4/HDD来源/旧inode/完整内容及UID/GID/权限/纳秒mtime匹配；另4次默认内容恢复仍schema1通过。两宽明暗、取消零登记/浏览器内容0、正常跨页退出、实际普通五路403/匿名401通过；精确身份/源/容器清理，独立重核55意图40成功18恢复(属性4)、活动/Unknown/租约/屏障0、11核心健康。2717 Unit/199 Integration(7默认原生跳过)、496前端/13历史浏览器、冻结真实PG1执行通过并清理；CI40501605203四路success、前端仍运行。ACL/xattr/特殊位、进行中重启故障、重建身份/长期清理与其余59项继续，17项移出不算完成。[证据与边界](qa/CONTAINER_RESTORATION_ATTRIBUTES.md)。

- **2026-10-27 宿主06b6eed/minor43已安装并通过签名属性恢复**：宿主更新0/SUCCESS、10.0.13/权限/UDS/动态入口通过；API/Worker/Gateway仍5fd8d3b。实际BFF正常上传产生旧属性捕获，独占容器重启后独立签名来源完整读取/Stage/Publish/Inspect，原路径及另一新路径各2097159字节、4属性/旧inode/HDD匹配，来源篡改/重放/错误摘要拒绝。升级后旧版内容恢复网页4次、两宽明暗/跨页退出及schema1兼容另验通过；最终45业务意图30成功、恢复10、活动/Unknown/租约/屏障0；两次属性发布是明确原生协议验证，没有业务Job或页面；三类身份/精确测试容器与源均0、11核心healthy。Release零警告错误，2704 Unit/199 Integration（7默认跳过）、251 Linux-root/12宿主检查通过。安装器范围说明已修，b9685da新CI四路成功、前端仍执行；持久属性策略/迁移/页面及更多故障仍待，59项active、17项移出不算完成。[证据及边界](qa/CONTAINER_RESTORATION_RPC.md)。


- **2026-10-27 历史普通属性发布引擎候选00f50b7，未安装**：可信HDD完整来源派生UID/GID/mode/纳秒mtime，schema4及独立指纹域、当前ID映射检查、交换前原子设置/精度校验和中断私有内容证明已实现。20专项/251容器Linux-root通过，含5次真实SIGKILL，12宿主安装检查通过；属性专用32KiB长路径回执/封存预算已通过，普通保持16KiB；首轮utime失败后清理缺证明已修且保留失败。实际独占容器重启后原路径/其他路径各2097159字节、4属性、旧inode/物理HDD及错误摘要通过；精确资源与冻结SHA独立核对。生产仍5fd8d3b、39意图零活动/Unknown/租约/屏障、11核心healthy。没有签名RPC/业务属性策略/页面选项，ACL/xattr/特殊位尚不支持，不签完整属性恢复或59项完成；17项移出不算完成。[证据及边界](qa/CONTAINER_RESTORATION_ATTRIBUTES.md)。


- **2026-10-27 5fd8d3b 同容器其他路径恢复实机通过**：正常dev覆盖0/SUCCESS，六组件/HTTP同源，冻结真实PG1执行/1通过/0跳过及清理。真实网页4次各2097159字节，含原路径、其他路径新建与覆盖、取消零登记/浏览器内容0/跨页正常退出；独立SQL来源与目标、Root、全字节/inode/HDD匹配。实际普通五路403/匿名401/零登记，临时身份与精确测试源/容器/隔离库0；39意图24成功/8取消/7失败，恢复6，活动/Unknown/租约/屏障0、11核心healthy。2688 Unit/199 Integration（7默认原生跳过）、494前端/9历史浏览器和检查通过；CI四路success、前端仍运行，上版3bac全success。历史完整属性、重建身份、长期清理与真实重启/发布故障继续，59项active、17项移出不算完成。[验收与边界](qa/CONTAINER_FILE_RESTORATION.md)。


- **2026-10-27 3bac49d原路径内容恢复已实机通过**：正常dev覆盖0/SUCCESS，六组件/HTTP同源、宿主校验及实际新迁移通过。冻结真实PG1执行/1通过/0跳过且清理；实际BFF生成2MiB+7旧捕获并重启测试容器，桌面明色/320暗色各一次恢复不同历史版本，浏览器内容0、取消确认零登记、正常跨页双端退出清空通过。SQL永久目录/来源MAC、Root、最终全字节、旧inode/HDD链独立核对；34意图19成功/8取消/7失败，恢复2，活动/Unknown/租约/屏障0，三类临时身份/测试容器源/原生库0，11核心healthy。原始属性、跨路径、容器重建迁移、真实Worker/Agent及发布故障等仍缺，59项active、17项移出不算完成。[实际证据与边界](qa/CONTAINER_FILE_RESTORATION.md)。

- **2026-10-27 容器历史内容恢复候选，尚未部署**：永久目录/迁移、独立来源绑定计划、现有发布任务/保护复用、Worker从HDD完整校验到暂存、API与原路径内容恢复页面已接。普通上传null字段保持旧MAC/快照，恢复拒绝浏览器内容与通用重试，原Unknown接管保留。Release零警告错误、2682 Unit/198 Integration（7原生默认跳过）、494前端/检查/构建通过；组合浏览器首轮34/35，修取消夹具版本后历史7/7通过。冻结真实PG与实际BFF/Root/HDD/网页尚待；原属性完整恢复/跨路径/跨容器重建及其他文件功能仍缺，当前e6b5034不变，59项active、17项移出不算完成。[边界和门禁](qa/CONTAINER_FILE_RESTORATION.md)。

- **2026-10-27 e6b5034/minor42已覆盖，编辑与历史来源实机通过**：两次正常dev覆盖0/SUCCESS，当前六组件/实际HTTP一致、宿主权限/UDS/动态入口及10.0.13校验通过。实际桌面明色/320暗色两次编辑，BOM/CRLF、取消零内容/单次确认发送、旧inode/HDD和跨页退出清空通过；真实BFF生成2MiB+7旧版本，独立签名List/ReadSource完整读回及实际测试容器重启后旧捕获读回、签名/重放/核心拒绝通过。真实只读沙箱误报可写要求已修且保留失败证据，写路径仍失败关闭。最终31意图16成功/8取消/7失败，Unknown/活租约/屏障0，临时身份0、11核心healthy；231容器Linux-root/22备份安装通过，CI后端/原生PG/原生Valkey/安装器success、前端尚运行。自然两小时撤权已完整通过；恢复永久目录/任务/计划/API/UI及其余59项继续active，17项移出不算完成。[真实协议与边界](qa/CONTAINER_HISTORY_RPC.md)、[编辑证据](qa/CONTAINER_TEXT_EDITOR.md)。

- **2026-10-27 自然两小时撤权通过；历史 RPC 候选待部署**：原登录实际7203.941秒/246次观察，移组后原快照到期首次403、到期后成功授权0、未改时钟/SQL/缓存，独占身份清理。minor42独立List/ReadSource/固定只读helper/严格EOF SDK及原生门禁候选完成，Release零警告错误、2666 Unit/191 Integration通过（7原生夹具默认跳过）、230容器Linux-root/22备份安装测试通过。尚未部署，实际仍fab6e2e+72c72cc helper；下一步覆盖并执行真实编辑与HDD来源RPC，恢复意图/目录SQL/API/UI仍待，59项继续active。[协议与边界](qa/CONTAINER_HISTORY_RPC.md)、[两小时证据](qa/CONTAINER_UPLOAD_INTERRUPTION.md)。

- **2026-10-27 容器HDD历史校验基础候选**：独立v2容器历史域、字段/游标预算、存在与完整内容校验区分、绑定当前实例的旧捕获只读来源已实现。20新增Linux-root及最终216容器组通过；冻结引擎在真实HDD完整2MiB+7读回/实际独占容器重启后读取旧捕获、错误UUID/旧当前范围拒绝通过，源/沙箱精确清理。尚未安装或接RPC/目录SQL/恢复意图/API/UI，不宣称已恢复文件。运行时fab6e2e+72c72cc helper不变，两小时仍观察；继续独立签名协议与完整恢复链。[证据与剩余](qa/CONTAINER_FILE_HISTORY_ENGINE.md)。

- **2026-10-27 真实浏览器进行中断通过，并修复只读清理竞态**：在实际Root已接收32KiB后，页面中止及跨页正常退出均有真实CDP ERR_ABORTED/canceled与独立SQL/Worker/Root终态，零目标发布；两页login、旧Cookie401/Authentik拒绝通过。门禁抓到只读/宿主--collect退出竞态，24确定性测试修复前10失败、修后全通过；helper已原子热修，无服务重启，程序集仍fab6e2e。最终25意图10成功/8取消/7失败、Unknown/活租约/屏障0、11核心healthy；失败输入精确核验清理。自然两小时仍观察，文本编辑未部署，下一次覆盖须包含helper修复。[浏览器证据](qa/CONTAINER_UPLOAD_BROWSER_INTERRUPTION.md)、[修复及安装摘要](qa/FILE_TRANSPORT_STOP_RACE.md)。

- **2026-10-27 有发布保护的Unknown真实整链通过**：原fab6e2e上512MiB旧版本实际HDD partial写入后停止独占容器，Unknown保留RootStarted/全局屏障；另一正常容器上传两次被Worker推迟。原身份正常退出并删除后，不同管理员持久计划/二次确认永久封存及失败对账解除屏障，后者才成功发布。原件全SHA/inode、Root载荷与HDD未完成副本保留，测试源精确清理/11核心healthy；清理观察器首轮遗漏空私有目录已修正并完整重跑通过。22意图10成功/7取消/5失败，Unknown/活租约/屏障0。权限两小时仍观察，编辑候选未部署；发布交换故障/重启、真实编辑和历史恢复继续。[证据与边界](qa/CONTAINER_UPLOAD_PUBLISH_INTERRUPTION.md)。

- **2026-10-27 容器文本编辑候选与部分接收中断门禁**：前端已支持独立容器位置、1MiB UTF-8/BOM/换行、打开时版本绑定、确认取消零字节/草稿返回与同步私有清理；469单元/46组合浏览器、检查和生产构建通过，候选尚未部署。实际fab6e2e三项先接收64KiB后取消/退出/RST，Worker终止及Root清理通过，无发布；18意图8成功/7取消/3失败、活租约/屏障0、11核心healthy。两小时自然权限降级仍在观察，不改时钟/缓存、不提前签通过；实机编辑、发布中故障/重启/有屏障Unknown及历史恢复继续。[编辑边界](qa/CONTAINER_TEXT_EDITOR.md)、[中断证据](qa/CONTAINER_UPLOAD_INTERRUPTION.md)。

- **2026-10-27 容器上传常规BFF/UI已覆盖并实机通过**：fab6e2e六组件/HTTP一致、11核心healthy；真实2MiB+7新建/覆盖/全内容读回、旧inode/HDD，显式取消/退出后Worker撤销，原账号删除后不同管理员封存/失败对账通过。真实1440明色/320暗色各一次发送、确认取消零字节、目录刷新/无溢出、跨页正常双端退出与旧Cookie拒绝通过；14条永久意图/审计/Root历史保留、Unknown/活租约/发布保护均0，临时身份0，测试源精确清理。发布中故障/撤权及有屏障Unknown、编辑/历史恢复与其余59项继续。[真实证据与失败](qa/CONTAINER_UPLOAD_BFF_UI.md)。

- **2026-10-27 minor41已安装并验证七RPC，业务入口未部署**：宿主Agent/MaintenanceHost实际b681e1d、10.0.13运行库/PKCS和权限/动态入口通过；真实签名五方法完整2MiB+7发布/读回/旧inode/HDD及取消通过，独立protobuf客户端验证历史Observe/Seal、实际停止重启、精确批准/范围拒绝与幂等。自有数据清理、两个失败验证留下的空锁保留，11核心healthy；API/Worker/Gateway仍f5a0029。下一步部署新业务并验收真实Cookie/SQL/Worker/HDD/UI，59项继续。[安装及失败证据](qa/CONTAINER_UPLOAD_HTTP_WORKER.md#b681e1d-宿主安装态验证)。

- **2026-10-27 容器上传HTTP/Worker/精确代理候选，未部署**：独立管理员计划/登记/原始暂存/状态/取消、真实SQL DI与专属HostedService已接通；minor41门禁保持，目标丢失或过期无法确认进入Unknown并保留保护。Release零警告错误、2604 Unit/191 Integration通过（7原生夹具默认跳过），17新HTTP/文档/边界、实际YARP 2MiB+7/6秒/Host与原生nginx精确额度通过；456前端单元/检查/构建通过。仍待minor41安装、真实Cookie→Worker/SQL→Root/HDD及页面整链，59项继续active。[证据与失败](qa/CONTAINER_UPLOAD_HTTP_WORKER.md)。

- **2026-10-27 容器上传管理员接管候选，未部署**：第12类container-upload的永久计划/确认、当前登录与权限复核、独立容器定位/观察验证、不可变审计及共享屏障原子解除已接通。丢封存回执只观察，收包前丢请求只重试幂等Seal，绝不重发文件或Publish；历史Published不伪成功。2593 Unit/174 Integration（7默认原生跳过）、冻结34ffb2e真实PG1执行/1通过/0跳过及清理通过；前端455单元/检查/构建、最终3浏览器合成流程通过。前两轮降级断言/永久意图清理失败均保留并修正，两个真实降级保护及所有历史保留。HTTP/专属上传Worker/代理/安装态整链仍待，59项继续active。[证据与边界](qa/CONTAINER_UPLOAD_ADMINISTRATOR_RECOVERY.md)。

- **2026-10-27 容器历史观察/永久封存候选，未安装**：minor41新增独立签名ObserveRecovery/SealRecovery，历史观察不进原namespace；原journal原子封存并完整保留原件/HDD，冻结minor40 reader与迟到writer也拒绝。2563 Unit/174 Integration通过（7原生夹具跳过）、180容器Linux-root通过；实际固定systemd profile完整2MiB+7、真实停止/重启、历史摘要/封存与当前实例迟到发送拒绝通过，临时资源清理/11核心healthy。没有安装候选或开放恢复SQL/API/UI；59项继续active。[证据与剩余](qa/CONTAINER_UPLOAD_RECOVERY_SEAL.md)。

- **2026-10-27 容器永久上传业务与界面候选，未部署**：SQL意图/Job/Outbox/共享发布保护、原登录接收、单次发布/持久取消调度及Vue上传确认生命周期已实现；minimum Agent40完整能力预检拒绝低版本新写。Release零警告错误，2501 Unit/174 Integration通过（7原生夹具明确跳过）；前端453单元/lint/格式/类型/生产构建通过。6fbca7b冻结隔离真实PG1执行/1通过/0跳过，包含双连接幂等及发布互斥；后续预检/WebUI不以该结果冒充整链验证。HTTP/Worker注册、独立容器Unknown管理员恢复、精确代理和真实BFF/Root/HDD/页面验收仍待，59项继续active。[证据与边界](qa/CONTAINER_FILE_UPLOAD_INTENTS.md)。

- **2026-10-27 登录撤销安装及真实Worker门禁**：当前f5a0029六组件/HTTP一致、11核心healthy；双管理员登录中退出原会话后18次旧Cookie401，第二登录仍管理员，Worker自动取消原上传，无显式取消/无目标写入，锁/Outbox释放且审计/root拒收凭据保留。真实Valkey ACL缺TIME已最小补权并持久化；b764741生产类别原生1执行/1通过/0跳过，两CI五路success；OIDC双端退出、33MiB+7宿主完整上传/下载/留存、容器读取/中断/普通拒绝和PTY通过。真实320暗色/1440明色跨页退出已通过，进行中撤销、容器永久上传/共享写屏障/BFF/UI及全部59项继续。[完整边界](qa/BFF_LOGIN_LIVENESS.md)。

- **2026-10-26 首字节前取消已安装签收**：136b8a0/minor40保存独立Prevented拒收标记；a58a489真实签名UDS先取消/查询/重放，再发送合法首帧，精确CONTAINER_UPLOAD_CANCELED且零暂存载荷。完整2MiB+7发布读回/HDD旧原件仍通过，2441 Unit/160 Integration（6原生夹具跳过）、166容器Linux-root通过；本批载荷精确清理、11核心healthy。永久容器任务/共享写屏障、登录撤销/权限审计及BFF/UI仍待，59项继续。[证据和后续约束](qa/CONTAINER_FILE_CANCEL_FENCE.md)。

- **2026-10-26 容器写入Agent已安装签收**：ac72b41/minor39与维护入口已覆盖，真实签名UDS五方法、完整2MiB+7发布/原只读RPC逐字节读回、旧inode/HDD原件、owner/请求体/MAC/重放拒绝与显式暂存取消通过。修复collect卸载竞态掩盖权限拒绝，8故障注入通过；Release零警告错误、2411 Unit/160 Integration通过（6原生夹具跳过）、154容器Linux-root通过。成功与两次失败现场精确核验清理，11核心healthy，应用容器未更新。永久任务/当前权限审计、BFF上传编辑/历史恢复/Unknown及真实UI仍待，59项继续。[完整证据和失败](qa/CONTAINER_FILE_WRITE_RPC.md)。

- **2026-10-26 容器写入协议候选（未安装）**：Agent minor39独立五RPC与请求体绑定一次性签名、SDK/Agent/root三层完整流校验、固定受限helper已实现。Release零警告错误、2410 Unit/160 Integration通过（6原生夹具跳过）、138容器Linux-root通过；真实systemd profile完成2MiB+7全内容发布/HDD旧原件、精确InvocationID活任务取消和清理，11核心healthy。没有产品安装态、永久任务/当前权限/审计或BFF写入页面；59项继续active。[证据及边界](qa/CONTAINER_FILE_WRITE_RPC.md)。

- **Linux 接续，容器原子发布候选**：跨盘准备/输出全量读回、HDD 前置证明/NOREPLACE/EXCHANGE、旧 inode 保留与取消/Unknown 观察已实现。23 项新 Linux-root（10处实际 SIGKILL）、127 容器/184 宿主/14 目录备份/8 范围通过；实际绑定与容器可写层、镜像原件覆盖和强杀后新真实会话对账通过，精确清理/11核心healthy。没有产品写入 RPC、helper、任务/权限审计或 BFF/UI，实际运行时不变，59项保持active。[证据](qa/CONTAINER_FILE_PUBLISHING.md)。

- **Linux 接续，容器 HDD 捕获候选**：暂存冻结根/命名空间/配置证明，容器独立历史域及原件全内容捕获已实现；19 项新真实 Linux-root（含三处真实 SIGKILL）、104 项容器/184 项宿主文件联合通过。实际容器 2 MiB+7 物理 HDD 捕获、原件/父版本不变、重放/错误 UUID/停止拒绝和清理通过（源/备份同 HDD，仅版本留存）；另宿主实际 NVMe→HDD 沙箱通过。原子发布、协议/helper、永久任务/权限审计/BFF/UI及历史恢复继续，未安装、不算完整文件功能或59项完成。[证据](qa/CONTAINER_FILE_HDD_CAPTURE.md)。

- **Linux 接续，容器上传暂存候选**：新增容器/启动实例/父目录和叶版本/主体绑定的私有暂存、严格流/回执/重放/丢弃，25 项 Linux-root（含实际 SIGKILL）及独占真实只读容器 2 MiB+7 完整内容与清理通过；Release/2308 Unit/160 Integration通过，6专用原生夹具跳过。尚无HDD发布、协议/helper、永久任务/BFF/UI，不算完整上传或59项完成，实际产品运行时不变。[证据与后续](qa/CONTAINER_FILE_STAGING.md)。

- **2026-10-22实际下载网络终态补齐，服务仍0d18dd3/minor38**：真实手机大文件页面取消及另一1440标签正常退出，CDP均记录部分字节/HTTP200后loadingFailed ERR_ABORTED/canceled；SQL两条Incomplete/0 Validated，双端401/403与界面清空通过。取消后的完整2MiB+7浏览器全字节/SHA及CDP loadingFinished通过。新只读观察器8项Node测试已接CI，不把模拟单元当实机；精确清理后独立SSH身份0/11核心healthy。当前同场景网络pending已解决，更广撤权、容器写入/HDD历史/恢复与59项继续。[证据](qa/SESSION_EXPIRY_UI.md)。

- **2026-10-22实际0d18dd3/minor38**：六组件与实际HTTP来源一致，冻结PG dispatch1执行/1通过/清理、CI39726926076四路success。真实320暗色完整2MiB+7下载摘要、另一同源1440明色正常退出双页回login/清空管理壳及WD401/Authentik403、无Storage信号的实际401刷新兜底通过；普通真实拒绝页0文件请求/API403、首页/个人门户可用及正常退出通过。新SSH确认身份0/独占合成数据清理/11核心healthy，13单元与60 Linux-root重新通过。大文件SQL为Incomplete/0 Validated，但浏览器网络终态观察pending并在关闭时触发工具重置，明确未签；容器写入/HDD历史/恢复与全部59项继续。[实际证据和遗留](qa/SESSION_EXPIRY_UI.md)。以下候选/未部署为对应历史。

- **2026-10-22真实页面与跨标签退出候选**：dd290e9实际1440明色/320暗色完整2MiB+7下载/摘要、64MiB+7响应后29%取消及正常另一页退出中断/SQL已验证；发现旧页仍显示管理员壳，已补401清空/登录跳转、无敏感信息的跨标签退出信号与迟到身份栅栏。444前端/22生产预览、2308 Unit/160 Integration（6本机原生跳过）及60真实Linux-root通过，候选尚未覆盖，不签P0或文件整项。[证据及原始失败](qa/SESSION_EXPIRY_UI.md)。

- **2026-10-22前端CI工具链启动修正**：两类实际失败分别为固定pnpm二进制下载超时、setup-node在无版本声明的仓库根探测缓存并下载浮动12.14.0。d302303关闭安装前缓存探测，仅在web使用锁定12.10.1；特定下载超时有限重试、其他错误不重试。5项Linux离线进程/配置门禁通过，工作站只快进QA/CI源码、未部署；CI39716700162已确认执行中，不能记全绿。[失败与证据](qa/CI_FRONTEND_BOOTSTRAP.md)。

- **2026-10-22容器文件扩展BFF子门禁**：44c4d1d验证工具在已装dd290e9通过真实2MiB+7完整下载、64MiB+7响应中断Incomplete/无成功及后续完整下载；普通四路403、退出原Cookie重放四路401、独立SQL审计/完整源身份/精确清理通过。51真实Linux-root测试通过，三类临时身份/专用容器/目录均0、11核心healthy。真实网页登录提交后工具超时，未签收；未部署新服务、未重启Docker。CI前端安装失败待查，其他三路success，整体59项继续。[证据与失败记录](qa/CONTAINER_FILE_BFF_UI.md#44c4d1d扩展bff实际通过运行时仍dd290e9)。

- **2026-10-22实际dd290e9/minor38**：容器文件只读业务/审计/HTTP/详情面板已覆盖，五运行组件build-info一致、维护healthy/11核心healthy，未回滚。2308 Unit/160 Integration（6本机原生跳过）、431前端、51联合+2专项浏览器/11新修后重跑通过，6136c1f与dd290e9冻结实际PG各1执行/1通过并清理；真实短时管理员Cookie/BFF/SQL/UDS896字节对独立Docker内容及拒绝/WD退出通过。新SSH身份0、容器清理、8审计且原名泄漏0；普通/撤权/真实页面/大文件中断等未签，当前59项继续。[证据与剩余](qa/CONTAINER_FILE_BFF_UI.md)。以下“未部署”是对应历史。

- **2026-10-22容器文件只读安装态子门禁**：fd3bda2 Agent/minor38和MaintenanceHost已安装、宿主校验通过；API/Worker/Gateway保持9ec8721。43真实Linux-root、固定helper沙箱及签名UDS四方法/完整2097159字节/保护拒绝通过，三轮载荷与容器新SSH独立证明清理，11核心healthy。Gateway源码候选11项内存协议测试及完整2275 Unit/159 Integration（6本机原生跳过）通过，业务权限/审计/BFF/UI仍缺。当前59项继续，后文版本为对应历史。[安装态与下一步](qa/CONTAINER_FILE_READS_INSTALLED.md)。

- **2026-10-21用户授权精简单工作站范围**：当前59项，旧76项中17项移出而非完成；40项部分实现、17项待实现、2项待最终验收。多节点/企业租户/订阅、邮件DNS托管、虚拟机、插件SDK、专用CMS/WAF及自动云备份体系不再是交付要求；多语言开发空间/调试报告、资源上限/用户隔离、安全和备份恢复保留。[范围决策](SCOPE_SINGLE_WORKSTATION.md)。现有部署/数据/QA证据不删、不改，旧76项动态仅作历史。
- **2026-10-21目录清理真实网页子范围**：实际e46ef29/1.36，4b02e6b观察器验证1440明色/320暗色两个Trash-All页面流；刷新/取消零写、一次明确确认、独立完整2MiB+7磁盘前后证明、历史Purged禁止重复。8任务零活动/锁/pending Outbox、临时账号0与测试目录不存在另验通过；普通真实拒绝页0文件请求、有效管理员正常退出通过，普通门户/退出及中断撤权仍待补。[实际证据](qa/FILE_TREE_PURGING_REAL_UI.md)。

- **2026-10-21实际e46ef29/Agent1.36**：六组件来源一致，正常dev覆盖发布服务success/0，11核心healthy。验证工具afb41b8完成真实Cookie/BFF/Worker/SQL/UDS/HDD两捕获×三范围6执行及4显式补集，六份2MiB+7完整证明、旧路径新对象保留；独立复核33 Job、10成功、零活动/锁/pending Outbox及临时账号0。成功批次载荷精确清理，两失败fixture保留；同HDD不算异盘。已部署版本CI四路success；真实网页/撤权/中断与完整76项仍active。[实机证据与失败](qa/FILE_TREE_PURGING_BFF.md)。以下候选记录为对应历史。

- **2026-10-21清理WebUI候选通过本机/SQL子门禁**：6a0083f接入显式三范围计划/双任务/二次确认/取消/未知与互斥；39d10e2补手机高度及实机客户端。415前端单元、Release/2186 Unit/159 Integration（6原生跳过）、构建/type/lint/格式、139联合浏览器及320×780收紧19重跑通过；同源码工作站隔离PG1执行/1通过且清理。客户端9项纯校验通过，但尚无本批真实BFF/网页/覆盖发布证明；CI前端继续，完整76项active。[证据、失败与下一步](qa/FILE_TREE_PURGING_UI.md)。

- **2026-10-21 Authentik管理员/全局界面已更新**：f788a1e已应用，管理员Logo135×36→224×59.7；管理/门户纯色明暗工作面、去掉门户斜切背景，四个原生界面属性窄白名单合并且实际me/render验证。原图匿名完整哈希匹配；34测试在本机/工作站均通过、320窄屏与真实登录/主题/导航/退出通过、临时账号独立计数0、11核心healthy。CI仍执行，镜像/身份流程不变；[证据](qa/AUTHENTIK_ADMIN_GLOBAL_UI.md)。清理面板工作副本保留未发布，全部76项active。

- **2026-10-21用户指定Authentik壁纸/门户Logo已上线**：最终b92ca3e仅Brand外观，指定JPG匿名完整1,565,840字节/SHA匹配；门户Logo120×32→224×59.7，手机160×42.7两行页头，明暗/刷新与真实应用内容视觉、实际登录/搜索/原生退出通过。29脚本纯测试、默认同步保留/幂等、临时账号独立计数0、自有浏览器清理及11核心健康。SVG实际颜色不足经截图发现再修，新CI仍执行；[完整证据](qa/AUTHENTIK_WALLPAPER_LOGO.md)。清理面板源码/39单元/19浏览器登记工作副本保留、未提交/部署，下一步恢复其完整门禁和真实链路；全部76项active。

- **2026-10-21清理接管当前摘要子门禁**：6ce7dad增加经过冻结/当前原操作双重绑定的8字段公开摘要，原版本/状态及归档/原件删除事实不再取旧快照；旧10类JSON保持，错绑/倒退/事实变化禁确认。Release零警告错误、2186 Unit/159 Integration（6本机原生跳过）、376前端单元/构建/lint/格式、38生产预览浏览器均通过；同源码工作站独立PG实际1执行/1通过且精确清理/11核心健康。新CI仍执行、未部署；[证据和失败修正](qa/FILE_TREE_PURGING_RECOVERY_SUMMARY.md)。下一步三范围发起/确认面板与真实BFF/UDS/HDD/发布；完整76项active。

- **2026-10-21清理Worker/API/第11类接管子门禁**：独立六路由、专用Worker及Outbox隔离、跨账号当前管理员Inspect/Discard与永久组件回执事务处置已实现。Release/2145 Unit/159 Integration通过（6本机原生跳过）；3eca529工作站独立PG库实际1项完整编排通过、临时资源清理确认，CI四路success。捕获历史与第11类界面消费者已接字段，Vue构建/373单测/lint/格式及102项相关生产预览浏览器回归通过（Chrome155、0跳过）；发起清理面板/真实BFF与发布仍未完成。见[证据及剩余](qa/FILE_TREE_PURGING_HTTP_RECOVERY.md)，完整76项仍active。

- **2026-10-21清理SQL子门禁通过**：永久意图/签名回执/组件历史叠加、迁移和仓储事务已实现；确认共用捕获/层级锁，两种恢复尊重清理状态。Release与2124 Unit/147 Integration通过（6本机原生跳过）；563f48a在工作站独立PG库实际1项完整编排通过、临时资源清理确认。CI后端/原生PG/安装器通过，前端待终态。未接Worker/API/UI、未部署。详见[SQL证据与边界](qa/FILE_TREE_PURGING_POSTGRES.md)，前轮无SQL说明为历史状态；完整76项继续。

- **2026-10-21清理业务材料候选**：HTTP仅capture/scope、后端签名目录解析、独立请求/计划/回执/确认、当前管理员与续租调度、minor36 Gateway严格映射已实现；Unknown已选组件无正面证明显示null。Release及2124 Unit/146 Integration（6原生跳过）通过；旧Windows维护快照并发失败已修，同一原测试10轮及全套再通过。[证据及剩余](qa/FILE_TREE_PURGING_BUSINESS_MATERIAL.md)。没有SQL仓储/迁移/Worker注册/清理API/UI，未部署；实际56101c8/1.36与2c8d1bb应用、11核心healthy保持。完整76项active。

- **2026-10-21目录清理安装态子门禁通过**：Agent/MaintenanceHost实际56101c8/1.36、默认授权原件备份/宿主权限及UDS验收通过；6c60ea3客户端与双检查点独立磁盘验证两捕获类型/三范围、2MiB+7及1GiB+7完整内容，6清理完成/6取消。测试载荷与journal精确清理，81路径另验缺失、5份私有证据保留，11核心healthy/宿主重启0。[证据及剩余](qa/FILE_TREE_PURGING_INSTALLED.md)。同HDD不宣称异盘/已装中断；尚无清理业务任务/SQL墓碑/真人权限与页面，API/Worker/Gateway保持2c8d1bb，完整76项active。以下未安装为前轮历史。

- **2026-10-21目录清理Agent源码候选**：minor36独立签名方法、严格计划/回执、可信固定helper/loader和精确默认根升级已实现；1856 Unit/146 Integration（6本机原生跳过）及Release通过，冻结Linux清理30/文件184通过。新增安装器元数据验证，尚未安装，不冒充真实HDD/UDS/BFF或页面验收。[证据](qa/FILE_TREE_PURGING_AGENT.md)。实际2c8d1bb/1.35、11核心healthy；当前管理员/永久任务/SQL墓碑/页面及全部76项继续。下列无helper/RPC为前轮历史。

- **2026-10-21目录清理内部候选**：新增Archive/Originals/All单捕获冻结计划、已完成原任务证明、隔离/逐文件销毁、墓碑和不续删Unknown；未开放helper/RPC/业务任务或页面、未安装。最终v6清理24/普通文件181实际Linux-root通过，含1GiB+7完整内容及6强杀；v5目录253整组通过，三个旧宿主模块与v6逐个cmp一致。早期重建目录错误码失败保留并修复，未放宽断言。Release零警告错误、1758 Unit/146 Integration通过（6本机原生跳过），44bcd66四CI全绿、11核心healthy。[候选证据与剩余](qa/FILE_TREE_PURGING_ENGINE.md)。实际2c8d1bb/1.35不变；签名RPC/任务/页面、孤儿与长期清理及全部76项继续。

- **2026-10-21覆盖恢复真实网页子范围通过**：在已装2c8d1bb/1.35上，1440明色及320暗色各2MiB+7真实登录/回收/历史选择/覆盖完成，独立全字节/原件inode/两组HDD归档与不同内容域核验通过；Ready刷新零文件写请求、取消不改目标、真实普通用户拒绝/零文件请求及正常退出通过。自有载荷精确清理并独立确认，5操作/9任务/4捕获永久保留、active/锁/pending Outbox均0。新增观察器14项真实Linux-root通过；运行时与文档四路CI均成功、11核心healthy。[网页证据](qa/FILE_TREE_OVERWRITE_REAL_UI.md)。本批不重复部署，目录彻底清理/容器文件与完整76项仍active；下列网页未签收为前轮历史。

- **2026-10-21实际2c8d1bb/Agent1.35**：独立归档内容证明修复已覆盖发布，六组件/HTTP来源一致、两宿主active及11核心healthy。冻结真实PG1执行1通过、临时清理独立复核；同源码签名UDS/2MiB+7与1GiB+7完整校验，以及真实Cookie/BFF/Worker/SQL的覆盖、Overwrite历史经两条流程再次恢复、CSRF/普通拒绝/审计/零锁均通过。1758 Unit/146 Integration（6本机原生跳过）、Linux32+23+5通过；首轮共用测试旧预期失败已保留并正确修复。新CI三路成功/前端原作业继续。真实网页仅匿名登录1440视觉，主题工具超时，覆盖交互仍未签收。[最新证据](qa/FILE_TREE_OVERWRITE_CONTENT_PROOF.md)。完整76项active，下列未安装为历史。

- **2026-10-21摘要绑定修复候选，未部署**：实际复用验收准备中发现Overwrite目录把inode树摘要误存为归档ContentSha，后续严格恢复会拒绝。新增独立TargetContentSha256全链绑定及RPC1.35专用能力，保留旧树/文件inode证明、不放宽校验。1758 Unit/146 Integration及Release零警告错误通过（6本机原生跳过）；真实Linux/PG/新UDS/BFF/网页仍待验证。[故障与证据](qa/FILE_TREE_OVERWRITE_CONTENT_PROOF.md)。1ea9de6上一候选已正常dev覆盖发布，六组件/HTTP来源一致、11核心healthy；不冒称新修复已装，完整76项继续。

- **2026-10-20安装态覆盖恢复子门禁通过**：验证源码57877a9在已装6a6f8e2上完成专用签名UDS/固定helper、2MiB+7与1GiB+7原件/被覆盖目录/新输出/两组归档manifest完整校验，精确清理后独立确认11核心healthy及两宿主服务active。首轮观察器错误要求回收私有新目录沿用旧inode，已修正并补5项真实Linux root回归；旧失败现场原样保留，原文件inode/元数据/全SHA断言未放宽。6e68746四CI全成功，候选API/Worker/WebUI仍未部署；真实身份BFF/页面、Overwrite捕获复用及全76项继续。文件根与备份实为同一HDD，不能算异盘备份；与数据库NVMe运行目录分开记录，未移动数据。[完整证据与边界](qa/FILE_TREE_OVERWRITE_RESTORING_UI.md)。下列“正在运行”为前序历史。

- **2026-10-20覆盖恢复WebUI候选 f35ee05**：独立3字段登记、当前目录版本冻结、计划/执行双任务、by-key与丢确认只查、取消/Unknown二次确认、覆盖前备份事实及第10类接管提示已接通；历史Trash/Overwrite严格类型均可复用。364前端单元、类型/生产构建/lint/格式通过，主代理Chrome生产预览88/88通过（含320/1045/1440明暗、普通拒绝页就绪后0文件请求）。一次DEV演示身份测试环境不匹配已保留、未削弱断言；[证据与未完成边界](qa/FILE_TREE_OVERWRITE_RESTORING_UI.md)。独立观察器3项真实Linux root范围/清理测试通过，Linux客户端已发布，安装态专用签名UDS/完整磁盘核验正在观察原句柄；未部署候选，真实身份BFF/页面与全76项仍active。实际Agent/MaintenanceHost/Gateway6a6f8e2、API/Worker18f38bb保持；以下为前序历史。

- **2026-10-20覆盖恢复管理员接管/Worker/API候选 d50b765**：第10类现任管理员Inspect/Discard对账、原身份冻结、备份证明单调、原子目录登记/审计/Job/Outbox/锁释放，以及独立HTTP/Worker/取消分流已接通；恢复kind字段扩至32并新增防截断降级保护迁移。Release零警告错误、1738 Unit/146 Integration通过（6本机原生明确跳过）；冻结d50b765工作站隔离真实PG1执行/1通过，临时DB/容器已清理并独立复核、11核心healthy。[证据及未完成边界](qa/FILE_TREE_OVERWRITE_ADMINISTRATOR_RECOVERY.md)。尚未部署：覆盖恢复UI、安装态专用签名UDS/独立磁盘证明、真实身份BFF/页面验收继续；实际Agent/MaintenanceHost/Gateway6a6f8e2、API/Worker18f38bb。完整76项仍active；以下为前序历史。

- **2026-10-20覆盖恢复持久化候选**：SQL仓储、永久双任务/Outbox/共享锁/租约、不可降级的覆盖前备份目录及Trash/Overwrite签名兼容已实现。Release零警告错误、1700 Unit/144 Integration通过（6本机原生跳过）；冻结62cd5dc在工作站隔离真实PG实际1执行/1通过、旧schema升级与审计回滚/取消/Unknown/跨Kind重用通过，临时容器清理独立确认、11核心healthy。前五次夹具/JSON查询失败均保留并修正后完整重验。[证据及未完成边界](qa/FILE_TREE_OVERWRITE_RESTORING_PERSISTENCE.md)。尚未部署：当前管理员对账、Worker/API/UI与安装态UDS/BFF/页面继续；实际Agent/MaintenanceHost/Gateway6a6f8e2，API/Worker18f38bb，完整76项仍active。以下为对应历史。

- **2026-10-20恢复在线后**：11核心healthy、Agent/维护服务重启0、WD live200。Authentik品牌最终验收通过：320/390/740短窗口/1440明暗无横向溢出，修复系统主题切换背景回退及语言控件遮Logo；独立匿名背景全SHA吻合，真实密码/OIDC/双端正常退出与临时身份清理通过。见[最终证据](qa/DISK_CAPACITY_AUTHENTIK_BRANDING.md)。新增覆盖恢复Application绑定/服务/调度与Gateway候选，1678 Unit/143 Integration通过（6本机原生跳过）、183项新纯测试；Windows原子快照并发问题也已修复并回归。尚无覆盖恢复SQL仓储/迁移/Worker/API/UI，未部署候选；见[业务材料边界](qa/FILE_TREE_OVERWRITE_RESTORING_MATERIAL.md)。实际Agent/MaintenanceHost/Gateway仍6a6f8e2，API/Worker18f38bb；完整76项继续active。以下离线/旧候选状态是对应历史。

- **2026-10-20最新实机**：磁盘永久100%已修复并发布，Snap/loop虚拟挂载不再污染容量；真实WD状态卡57.4%，对应最高有效EFI挂载，系统盘26.9%、数据盘0.28%。Agent/MaintenanceHost/Gateway为6a6f8e2（Agent1.34），API/Worker仍18f38bb；宿主校验和11核心健康通过。6a6f8e2四CI全绿，1493 Unit/143 Integration通过（本机6原生依赖跳过），品牌脚本26纯fake通过。Authentik海洋图/Logo/原生CSS已发布且真实登录/OIDC通过；最终手机暗色/页脚和独立背景下载/正常退出验收待恢复在线。用户已通知离线，停止所有工作站操作及重连、继续本地开发。见[事实及边界](qa/DISK_CAPACITY_AUTHENTIK_BRANDING.md)。下面“RPC1.34尚未安装”为前序历史；新覆盖恢复业务/API/UI与完整76项仍未完成。

新增**目录覆盖恢复RPC1.34候选，尚未安装**：独立签名方法/专用capability、严格目标和捕获证明、固定helper/可信模块、默认根精确升级及保护测试已接通。Release零警告/错误、1463 Unit/143 Integration通过（6本机原生跳过）；27覆盖恢复/驱动与23根升级实际Linux通过。前三路GPT代理补测试/盘点、主代理完成生产改动和统一复核。实际18f38bb/1.33与完整76项范围保持；[安装前边界与下一步](qa/FILE_TREE_OVERWRITE_RESTORING_AGENT.md)。

- 新增**目录覆盖恢复内部引擎，尚未部署/开放 RPC/API/UI**：独立目标授权及双树证明、HDD目标捕获、原子目录交换与完整旧原件留存、中断只观察/不自动重做已实现。最终24真实Linux通过（含1GiB+7旧内容全字节及8真实强杀、交换后取消和权限/路径变化）；受影响旧文件引擎180项、Release零警告/错误、1383 Unit/143 Integration通过（6本机原生跳过）。前一f3f9a80四CI已全绿，实际18f38bb/1.33不变。首次失败自有现场7源对象/4捕获文件经完整核验可恢复隔离，11组既有实机回归通过；[新引擎及后续](qa/FILE_TREE_OVERWRITE_RESTORING_ENGINE.md)。全部76项和P2-03/P2-04保持部分实现。

- 当前**18f38bb/Agent1.33目录不存在目标恢复网页子范围已签收**：真实1440明色原路径、320暗色新管理员恢复已删除旧账号捕获至新目标，各2MiB+7独立全字节/原inode/HDD归档/新输出核验、刷新不重做、两管理员及普通用户正常双端退出、普通首次/刷新0文件请求与有效CSRF403通过。自有网页载荷精确清理，8永久Job全部Succeeded、路径锁0、11核心healthy。328单元/474本地浏览器为前序契约证据；1GiB+7为独立安装态UDS证据，不冒称网页再次执行。观察器10项/QA路径栅栏6项Linux通过并补持续门禁，首次失败现场保留。[完整证据与边界](qa/FILE_TREE_RESTORING_UI.md)。继续目录覆盖恢复/彻底清理、容器文件及全部76项，P2-03/P2-04仍部分实现。

- 当前**18f38bb / Agent1.33**已发布，四CI成功、六组件/HTTP来源一致、11核心healthy。验证专用a90b9d7修正单一owner拒绝预期后，安装态签名UDS+独立完整2MiB+7/1GiB+7磁盘证明通过；真实OIDC/Cookie/BFF/SQL/Worker/UDS与完整2MiB+7恢复、取消/不覆盖/幂等/审计/普通有效CSRF403/精确载荷清理通过。8观察器Linux为QA工具证据。真实网页只完成登录/只读浏览，REPL定位器超时已确认浏览器/临时身份清理，尚未产生回收/恢复行；两个自有现场保留供接续/精确清理。[最新实际边界与恢复入口](qa/FILE_TREE_RESTORING_UI.md)。全部76项继续，不签完整文件管理。

- 冻结**18f38bb已实际覆盖部署**：四CI全部成功、更新单元成功退出、六组件/HTTP来源一致，宿主权限/动态入口与11核心healthy。首轮1.33探针对替换捕获owner误预期FailedPrecondition，实机按私有资源契约返回NotFound；仅验收代码修正预期/细分阶段，不改产品拒绝。自有失败现场保留，完整新UDS/真实BFF/网页仍未签收；[实际边界](qa/FILE_TREE_RESTORING_UI.md)。新网页观察器8项隔离Linux通过，不冒充产品恢复。全部76项继续。

- 目录恢复页面本地门禁完成：**328前端单元、474完整浏览器**及类型/lint/格式/生产构建通过；首次3定位器失败已修且完整重验。新冻结候选**18f38bb**的同源码工作站实际PG1执行/1通过、清理及独立临时容器消失复核通过；CI39367120491后端/PG/安装成功，前端仍在执行。新1.33原生探针已发布仅编译，真实UDS/身份BFF/网页及覆盖部署仍缺；实际仍e0175a0/1.32。本文后续验证记录先留本地，安装态验收后再提交；[最终本地证据](qa/FILE_TREE_RESTORING_UI.md)，完整76项不缩减。

- 新目录恢复WebUI候选 **9d83056** 已接历史捕获/独立根授权、原路径恢复、永久双任务确认/只读接续/Unknown及第九类当前管理员页面；328前端单元、类型/lint/格式/生产构建通过，工作站同源码冻结PG实际1执行/1通过且独立清理核实。首轮39浏览器36通过/3定位器失败已修，完整474浏览器仍运行；新CI后端/PG/安装成功、前端仍运行，另补入恢复专项持续门禁。真实恢复BFF脚本仅编译未执行。实际仍e0175a0/1.32；[页面证据与边界](qa/FILE_TREE_RESTORING_UI.md)，完整76项继续。

- 目录恢复后端 **66451f7** 已通过同源码工作站隔离真实PG（1执行/1通过、cleanupConfirmed及独立Docker移除复核），1383 Unit/143 Integration（6本机原生跳过）保持。CI39359695663三项成功、前端继续；首次原生断言失败已修且保留。恢复WebUI/新身份BFF/真实UDS及部署仍缺，实际e0175a0/1.32不变，完整76项继续，见[最新证据](qa/FILE_TREE_RESTORING_BFF.md)。

- 目录恢复业务后端新增候选（未部署）：捕获ID/目标专用公开请求、签名历史事实解析、永久计划/执行、捕获和路径锁/预算、Worker/取消/事务审计、第九类当前管理员处置及迁移已实现。1383 Unit/143 Integration通过，6原生本机跳过；新冻结实际PG/CI/身份BFF/WebUI待验收，实际e0175a0/1.32不变，见[证据](qa/FILE_TREE_RESTORING_BFF.md)。

- 目录恢复 **RPC1.33 新源码候选，未部署**：独立恢复契约/Agent方法、固定helper/可信模块、精确旧默认根授权升级及原捕获/当前操作owner分离已实现。Release零警告/错误，1336 Unit/141 Integration通过（6本机原生跳过），工作站隔离19恢复/19根升级通过；真实签名UDS与独立全磁盘探针已编译未执行。业务永久任务/Worker/HTTP/WebUI与覆盖恢复/清理继续；实际仍e0175a0/1.32，完整76项不缩减，见[边界](qa/FILE_TREE_RESTORING_AGENT.md)。

- 当前实际 **e0175a0 / Agent协议1.32**：目录回收常规BFF和真实管理员桌面/手机网页已覆盖发布并验证；四CI39339940419、冻结实际PG、1302 Unit/141 Integration（本机6原生跳过）、316前端单元/304完整浏览器通过。07:46:24 CST更新成功，六组件/HTTP来源一致、宿主权限与11核心健康。两端各2MiB+7完整归档/原inode独立核验、刷新不重做、普通真实身份0界面请求/有效CSRF403、真实双端退出和精确清理通过；既有七类文件/OIDC/宿主/8资产回归整体exit0。[完整证据及失败边界](qa/FILE_TREE_RECYCLING_UI.md)。原账号删除后新类型实盘接管、目录恢复/清理、容器文件及全部76项仍继续，P2-03/P2-04保持部分实现。下方旧未部署状态为历史。

- 8cd0daa原生PG与CI发现整根只读记录查询误用禁止空路径的写范围类，已修专用只读范围并新增5回归；1302 Unit/141 Integration（6本机原生跳过）、Release零警告/错误。新增候选实际PG/CI待验，前端304联合原句柄继续，不重新启动；[保留失败及修正](qa/FILE_TREE_RECYCLING_UI.md)。实际adc6cae/1.31、全部76项保持。

- 目录回收新增第八类当前管理员事务处置、根范围捕获目录/索引及完整回收与只读记录页面候选：1297 Unit/141 Integration（6本机原生跳过）、316前端单元及类型/lint/格式/生产构建通过，首轮22浏览器通过。最终304联合、冻结PG、新CI和安装态仍待签收；线上adc6cae/RPC1.31不变。见[页面与边界](qa/FILE_TREE_RECYCLING_UI.md)，完整76项继续。

- 新目录回收业务后端候选：永久计划/确认双任务、共享层级锁/预算/Worker/审计/API及独立签名HDD捕获目录、增量迁移与不可变触发器已实现，1294 Unit/141 Integration通过（6本机原生跳过）。新同源码实际PG/CI尚未执行、未安装，当前管理员第八类接管及WebUI仍缺；实际adc6cae/1.31不变，完整76项继续。[边界](qa/FILE_TREE_RECYCLING_BFF.md)。前序1722242四CI已全绿。

- 新目录回收Agent1.32为未安装候选：独立强类型RPC、固定helper/可信模块/精确默认升级，1265 Unit、139 Integration（6本机原生跳过）及40真实Linux通过。cf7a6a3引擎冻结214项分批通过（第一次选择命令ImportError保留）；完整新提交门禁/永久业务任务/Worker/API/UI/真实身份验收仍待继续。实际adc6cae/1.31不变，全部76项active；[证据边界](qa/FILE_TREE_RECYCLING_AGENT.md)。

- 目录回收20项、HDD目录恢复16项和目录移动安全增强34项真实Linux测试通过；本批仍为未安装源码，不是公开RPC/API/UI。下一步固定提交完整门禁与Agent/业务/页面接通。实际adc6cae/RPC1.31和全部76项范围不变；下方19项为历史工作副本。

- 新目录回收内部源码：HDD完整捕获/核验→逐文件原inode隔离→仅移除核对的空目录、父路径替换防护与只观察Root恢复已实现。19项实际Linux分批通过，含1GiB+7回收后独立完整还原/哈希/旧inode保留、7实际强杀及坏归档/替换/争用/撤权；更新恢复16项完整通过。尚无固定helper/Agent RPC/持久任务/API/UI或安装，见[回收证据和边界](qa/FILE_TREE_RECYCLING_ENGINE.md)。cfbf64b四CI39308300290已全绿；当前实际仍adc6cae/RPC1.31，目录回收/覆盖恢复/清理完整业务与全部76项继续，不签完P2-03/P2-04。

- 当前实际adc6cae / RPC1.31：目录位置修复已于02:32:53 CST覆盖发布，四CI39299194692成功（302前端单元/279生产浏览器+22布局）、同源码工作站冻结PG执行1/通过1/清理；六组件及HTTP来源一致、真实管理员1440明色/320暗色刷新返回原目录与根、响应200/遮罩0/文件写请求0、退出清空与双端注销通过，自有空目录及临时账号精确清理。OIDC/Host/8资产/宿主和当前USB入口通过，11核心健康，PKCS10.0.13；见[部署签收](qa/FILE_BROWSER_LOCATION.md)。新增HDD目录恢复内部源码15真实Linux（含1GiB+7全字节、7强杀栅栏及精确旧策略兼容）/176旧回归通过，尚无新RPC/API/Worker/UI或安装，见[恢复引擎边界](qa/FILE_TREE_RESTORING_ENGINE.md)。目录回收/覆盖恢复/容器文件及全部76项继续；下方旧候选/运行时为历史。

- 最新实际49c7495 / RPC1.31：四CI39284738314成功、同源码冻结PG实际1通过/清理，00:53:19 CST开发更新成功且六组件/HTTP来源一致。真实解压Cookie/BFF/SQL/Worker/UDS及管理员桌面TAR/320暗色TAR.GZ、完整2MiB+7输入输出/独立Root与磁盘证明、普通真实用户UI0文件请求/有效CSRF403与退出通过；测试夹具/账号精确清理、审计保留。七类既有文件/OIDC/Host/8资源回归通过，11核心健康，PKCS10.0.13。新目录位置记忆候选已实现、302前端单元通过，尚未部署；见[解压UI](qa/FILE_TREE_UNPACKING_UI.md)、[后端](qa/FILE_TREE_UNPACKING_BFF.md)、[位置候选](qa/FILE_BROWSER_LOCATION.md)。全部76项继续，下方旧运行时/未部署说明为历史。

- 独立解压WebUI源码候选已完成：文件更多入口、独立根授权、输入格式与新目录、双任务确认/取消/Unknown和原引用只读恢复；281前端单元、127联合浏览器（含26解压）及另1解压接管专项、类型/格式/lint/生产构建和视觉复核通过。真实BFF/Worker/Agent/完整输入输出验收脚本已补但未执行；后端实库证据属于e365f4c，CI39278674346三作业成功、frontend仍运行，不冒称最终候选已全绿。未部署，实际73513a4/1.31保持；下一步最终候选门禁与真实覆盖验收，全部76项继续。详见[页面](qa/FILE_TREE_UNPACKING_UI.md)与[后端](qa/FILE_TREE_UNPACKING_BFF.md)。下条联合“仍运行”为历史。

- 解压后端候选 e365f4c 已通过同源码工作站隔离实际PG（1执行/1通过/清理，外部发送0）；1237 Unit/139 Integration及前端恢复类型证据保持。独立解压操作页面已新增，本地281前端单元/26生产预览浏览器、类型/格式/lint/构建与独立视觉复核通过；联合旧文件回归仍在运行，尚未部署或真实身份BFF/UI验收。见[实库边界](qa/FILE_TREE_UNPACKING_BFF.md)及[页面清单](qa/FILE_TREE_UNPACKING_UI.md)。实际运行时保持73513a4/1.31；完整76项继续，下方“尚无独立页面”为前一候选历史。

- 新增解压业务源码候选（尚未部署）：独立永久双任务/确认/共享路径锁与预算、Worker/取消/Unknown只观察恢复、当前管理员第七类恢复、输入归档证明和不可变EF迁移、API及恢复页面类型已接通。1237 Unit/139 Integration通过，6本机原生PG明确跳过；前端恢复类型13测试/类型/格式/lint通过。尚无独立解压操作页面，候选原生PG/实际身份BFF/安装态与网页待验收，实际运行时仍73513a4/1.31。见[源码与证据边界](qa/FILE_TREE_UNPACKING_BFF.md)；完整76项继续。下条“未接持久仓库/Worker/API”为前一材料提交历史。

- 当前实际 **73513a4 / Agent1.31**：解压Agent已安装并签收真实签名UDS/helper、PAX/GNU与TAR/TAR.GZ、2MiB+7和1GiB+7完整新目录/独立磁盘及精确清理。四CI39258251245、同源码工作站PG实际1通过/清理，21:43:28 CST更新成功终态；客户端bc8037d仅验证修正、不改变产品。7类旧文件/OIDC/8资产/宿主通过，活跃文件/锁/终端0、11核心健康、PKCS10.0.13，见[最终账本与首次失败](qa/FILE_TREE_UNPACKING_AGENT.md)。新的业务绑定/接口/网关材料及19测试已实现，全量1215 Unit/137 Integration通过、6本机PG跳过；未部署，也未接持久仓库/Worker/API/UI。全部76项继续，不能把Agent等同完整解压；下方为历史。

- 解压Agent1.31源码候选：独立RPC/小计划与输入证明回执、固定helper/可信模块、精确默认根升级已接通，1196 Unit/137 Integration、153原生树与15根升级通过，Release 0警告0错误。6本机PG跳过；尚未安装或验收签名UDS，也未接BFF/Worker任务/API/UI，实际运行时仍abef670/1.30。见[候选及下一步](qa/FILE_TREE_UNPACKING_AGENT.md)，完整76项继续；下条“无CLI/RPC”为前序内部引擎历史。

- 目录解压内部引擎新增源码（未安装）：独立计划/回执域、TAR/TAR.GZ及有界GNU长名称、私有全量解包与NO_REPLACE新目录发布、只观察恢复已实现。152项真实Linux回归通过，含22解压、超过1GiB完整落盘/全SHA和七SIGKILL栅栏；首轮安全拒绝异常断言失败保留。尚无新CLI/RPC/BFF任务/Worker/WebUI，线上仍abef670/1.30；见[证据与后续](qa/FILE_TREE_UNPACKING.md)。完整76项继续，不把本批算成完整P2。

- 当前实际运行时 **abef670 / RPC1.30** 已覆盖目录压缩WebUI：四CI39238574798、269前端单元/245生产浏览器、同源码工作站冻结PG1执行/1通过/清理通过；19:06:59 CST更新服务成功终态。真实管理员桌面TAR/320暗色TAR.GZ、刷新只读恢复/恰好2登记2确认、完整原生下载/独立磁盘与Root成员/原inode校验、普通真实身份0界面请求与有效CSRF下403、完整两端退出通过。六旧文件BFF、8资产及宿主回归通过，11核心健康，活跃文件任务/锁/终端0，自有夹具和精确临时身份清理；见[安装态网页证据](qa/FILE_TREE_PACKING_UI.md)。超过1GiB网页下载及新类型实盘故障/跨管理员处置仍未签收，解压/目录回收恢复等全部76项继续；下方源码候选/旧版本是历史。

- 目录压缩WebUI新增源码候选（尚未部署）：独立源/目标授权、TAR/TAR.GZ格式和目标选择、双任务计划确认/取消/Unknown、URL原引用GET恢复、陈旧响应禁用和浏览器原生大归档下载已接通。269前端单元、86联合浏览器、类型/格式/lint/生产构建通过，独立小屏/桌面明暗视觉复核通过；最终CI/冻结PG和真实身份安装态网页仍待验收。见[页面证据与失败记录](qa/FILE_TREE_PACKING_UI.md)。线上仍0661051/RPC1.30；完整76项继续。

- 当前实际运行时 **0661051 / Agent RPC1.30**：修复压缩/复制路由冲突后，目录压缩后端已覆盖并通过真实身份/BFF/SQL/Worker/已安装 UDS/helper、TAR/TAR.GZ 和完整 HTTP/磁盘全 SHA 验收；另有 1GiB+7 源、1,073,756,160 字节 TAR 的完整 HTTP 大归档证明。四 CI39222897941、1169 Unit/137 Integration、同源码冻结实际 PG1执行/1通过/清理及六类既有文件真实回归通过；OIDC完整退出/8资产/宿主验证通过，11核心healthy，活跃文件操作/锁/终端0。**压缩操作 WebUI 尚未实现**，解压、目录回收恢复与完整76项继续。见[最终证据和首轮失败历史](qa/FILE_TREE_PACKING_BFF.md)。本条覆盖下面旧候选/未签收描述，不签完整P2-03/P2-04。

- 最新实际安装 `9675bbf/1.30`，四CI/冻结PG/六组件来源和宿主验证通过，但真实新压缩 API 验收在创建任务前失败：控制器误沿用目录复制路由，压缩路径缺失且与旧路径冲突。现已修正源码并补全局精确路由冲突及负控制测试，待覆盖重验；不可称压缩完整链路已签收。失败夹具按策略保留，见[真实发现](qa/FILE_TREE_PACKING_BFF.md)。完整76项继续。

- 新增目录压缩业务后端候选（未部署）：独立双 Job/持久计划、共享层级锁与预算、Worker、原登录确认/取消和当前管理员第六类恢复、回执专用 17GiB HTTP 下载/普通文件仍 1GiB、当前下载者权限与最终审计、不可变 EF 迁移已实现。1169 Unit/135 Integration、恢复页 13 定向单元与类型检查通过；6 原生本机跳过。冻结实盘 PG、实际 Cookie/BFF/Agent/完整下载与新压缩页面仍待验收，安装态保持8aa73c0/1.30；见[业务后端证据](qa/FILE_TREE_PACKING_BFF.md)。完整76项继续，下方“BFF仍缺”为旧安装态边界。

- 最新实际运行时8aa73c0 / Agent RPC1.30：独立压缩/回执绑定下载已安装及真实签名UDS/helper、两格式2MiB+7/超过1GiB原生TAR整流和独立磁盘校验通过，原源不变与自有夹具清理。1119 Unit/127 Integration、291 Linux、四CI39198233873和冻结实际PG1执行/通过/清理、六类既有文件BFF/OIDC/8资产回归通过；12:22:48 CST更新成功，六组件来源一致、11核心健康/活跃文件任务锁终端及临时身份0。新压缩BFF/任务/Worker/审计/HTTP/UI仍缺，完整76项继续；见[最终Agent签收](qa/FILE_TREE_PACKING_AGENT.md)。下方候选/未安装为历史。

- 新增压缩Agent1.30源码候选（尚未部署）：独立RPC/固定helper/显式根授权与默认精确升级、回执绑定17GiB下载/旧1GiB不变、可信加载和安装态验证脚本已实现。Release1119 Unit/127 Integration通过，6原生PG本机跳过；真实安装态UDS/大归档探针尚未执行，线上仍2a60668/RPC1.29，见[Agent候选证据](qa/FILE_TREE_PACKING_AGENT.md)。BFF/任务/Worker/审计/UI和完整76项继续。

- 新目标目录压缩发布内部引擎已实现：完整计划绑定/私有流式TAR和TAR.GZ/全量验证/新建发布/只观察回执恢复；20真实Linux测试含1GiB+7全量和7个真实SIGKILL栅栏、实机登记根两格式2MiB+7及精确清理通过。与22 codec/14留存合计56新增测试；尚未接RPC/API/Worker/UI或安装。线上仍为独立UI修复2a60668/RPC1.29，见[压缩发布边界](qa/FILE_TREE_PACKING.md)。

- 最新实际运行时2a60668/RPC1.29：容器详情等宽指标/整行配置/小屏单列已覆盖，265前端单元/22浏览器、真实管理员/worker inspect映射及5宽度明暗10组合、六组件来源/HTTP与11核心健康通过。固定CI39191684883四作业已全部成功，见[容器详情验收](qa/CONTAINER_DETAIL_LAYOUT.md)。完整76项继续。

- 目录归档/HDD留存内部引擎源码已实现，**未接产品入口/未部署**：有界TAR/PAX/GZIP、完整manifest/哈希、私有解包与独立磁盘验证、HDD策略/容量/不可重做回执；22 codec/14留存及96旧Linux回归、真实SIGKILL两栅栏、实际HDD2MiB+7与精确清理通过。实盘同设备，只称版本留存；没有新增RPC/API/Worker/UI或镜像。完整目录回收/恢复/压缩解压链路及76项继续；当前运行时仍b666042/RPC1.29，见[证据与首轮失败](qa/FILE_TREE_ARCHIVES.md)。

- 2026-10-19最新实际运行时 **b666042 / Agent RPC1.29**：按用户要求修复系统日志进程列，名称保留原样式单行省略/完整title，PID在第二行12px主题辅助色显示。四CI39183928285成功、265前端单元/219浏览器与同源码冻结实际PG1执行1通过/清理；09:32:49 CST发布成功终态。真实管理员网页前20条实际journald字段对应、320/1440明暗几何/无页面溢出、六组件实际来源/HTTP、宿主及11核心健康通过，临时身份/活跃文件工作流/锁/终端0，OIDC完整退出及8资源回归通过。详见[验收和首轮超时记录](qa/JOURNAL_PROCESS_LAYOUT.md)。正文搜索及此前文件能力保持，完整76项继续；下方旧运行时为历史。

- 2026-10-19最新实际运行时 **5e131e9 / Agent RPC1.29**：正文搜索API/gRPC/受限Root读取和WebUI已覆盖部署并完成真实身份/安装态链路签收。CI39088548813四作业成功，1089 Unit/127 Integration、265前端单元/213 CI浏览器和冻结实际PG1执行1通过/清理；更新08:13:27 CST成功终态。实际六组件/HTTP提交一致、真实管理员320/1440明暗/完整2MiB+7及普通403/匿名401/CSRF/Origin通过，源全字节/inode/元数据未改且自有夹具清理。验收脚本错误及QA-only修正保留，见[最终证据](qa/FILE_CONTENT_SEARCH.md)。目录回收/压缩解压、容器文件、隔离/长期清理和全部76项继续，下方旧运行时/候选为历史。

- 新增正文搜索候选（尚未部署）：专用API/gRPC/Root字面UTF-8读取及前端页签、双权限/返回前复核、版本校验/有界游标与资源限制已实现。1089 Unit/127 Integration、265前端单元、24本地联合浏览器及17真实Linux正文/47旧文件回归通过。真实身份/已安装UDS/页面验证脚本已准备但未执行；最后运行时仍b118454/RPC1.28，见[候选证据和失败记录](qa/FILE_CONTENT_SEARCH.md)。完整76项继续。

- 最新实际运行时 **b118454 / Agent RPC1.28** 已覆盖统一 Linux 构建来源：四镜像冻结提交/OCI标记、五程序集离线CLI与API/Gateway实际HTTP、维护页版本卡完成实机签收。四CI39083441049成功，1070 Unit/126 Integration、250前端单元/203浏览器、11 Linux边界测试与同源码实际PG1执行1通过/清理；08:57:56 CST更新成功。真实管理员320/1440明暗、完整SHA对应和0管理写请求通过；OIDC完整退出与既有HTTP/Gateway资产回归通过，四前缀临时身份0、11核心healthy与宿主验证通过。见[来源证据与未验收边界](qa/BUILD_PROVENANCE.md)。目录回收归档、正文搜索、容器文件/长期清理与完整76项继续；下方为历史。

- 最新实际运行时 **8d77293 / Agent1.28** 已覆盖当前管理员恢复工作流与专用 WebUI。CI39079871013 四作业成功，1060 Unit/125 Integration、242 前端单元/197 CI 浏览器及同源码冻结实际 PG 执行1/通过1/清理；08:02:16 CST 更新成功。真实删除原账号/销毁原登录后，另一管理员网页接管目录移动 Unknown、当前审计与原锁释放通过；原 Job 不伪造成功、Root 回执及原件完整字节/inode未变，没有重放 Apply，精确夹具清理。11核心 healthy、宿主服务 active。见[最新签收与证据边界](qa/FILE_TASK_RECOVERY.md)。API/Worker统一构建来源、其余故障组合及全部76项继续；下方旧候选/运行时均为历史。

- 最新实际运行时 **0defa90/Agent1.28** 已签收当前管理员五类Unknown只读清单与独立确认签名基础：四CI39071442629成功，1032 Unit/123 Integration、229前端/183 CI浏览器及同源码冻结实际PG1执行1通过/清理；05:53:36 CST覆盖成功。另一个真实管理员跨Subject/Cookie读取元数据，旧原登录接口仍404、普通用户清单403，原锁/Root回执未变，原有效登录清理自有任务和数据；最终11核心健康、临时身份/活跃写入/锁/终端0。见[只读子范围与证据边界](qa/FILE_TASK_RECOVERY.md)。完整持久计划/Worker/封存/当前审计/WebUI与真实原登录丢失处置仍缺，全部76项继续，下方未部署为历史。

- 新增当前管理员恢复基础候选（尚未部署）：五类Unknown只读清单、当前权限双重复核、安全DTO/有界seek与独立管理员确认MAC/ID域；1032 Unit/123 Integration和Release通过，6原生本机明确跳过。新增实际SQL断言尚待冻结门禁，写计划/Worker/封存/当前审计和WebUI仍缺，不把清单当作解锁功能，见[恢复证据与剩余项](qa/FILE_TASK_RECOVERY.md)。线上1399563/Agent1.28，全部76项继续。

- 最新实际运行时 **1399563/Agent1.28**：目录移动WebUI覆盖发布及真实身份网页验收通过。CI39065824243四作业成功，229前端单元/183 CI浏览器、47本地复制/移动联合浏览器和冻结实际PG1执行1通过/清理；更新unit04:34:09 CST成功。真实管理员桌面/320明暗两次2MiB+7移动、刷新只读恢复/查询不重做、普通用户首次/刷新0文件请求与有效CSRF GET/POST403通过，独立root核验完整字节/原inode/回执后精确清理。移动/复制真实BFF再回归通过，资产本地/HTTP/Gateway一致、临时身份清理，见[最终签收](qa/FILE_TREE_MOVE_UI.md)。原登录失效的当前管理员Unknown处置、目录回收/归档及全部76项继续；下方未发布描述为历史。

- 最新已验收运行时 **39cb274/Agent1.28**：目录移动BFF/永久双任务/migration/锁/Worker/人类审计已覆盖发布，四作业CI39059348941和同源码冻结实际PG执行1/通过1/清理通过。真实身份与安装态UDS完整2MiB+7字节、原inode/元数据保留、取消/保护/源变更/普通403通过，五类旧文件链路回归通过；见[移动后端证据与失败记录](qa/FILE_TREE_MOVE_BFF.md)。移动WebUI仅本地实现和测试，尚未发布；原登录丢失后当前管理员受控处置Unknown仍是P0-03缺口。下方旧候选是历史，全部76项继续。

- 新增目录移动完整后端候选（尚未部署）：独立API/永久双任务与migration/共享层级锁owner/Worker租约取消与不重做/人类审计已接通；发布/源移除/原件保留在Root回执前为nullable未知事实。997 Unit/122 Integration和脚本AST通过，6原生明确跳过；真实SQL、安装态身份BFF及页面仍待验收，见[移动BFF](qa/FILE_TREE_MOVE_BFF.md)。线上仍4fd9ba7/Agent1.28，不把Root 1GiB探针当本批业务链路证据，完整76项保持active。

- 最新实际部署**4fd9ba7/Agent1.28**：目录移动独立RPC/helper/显式根授权已安装。CI39054987899四作业成功、同源码实际PG1执行1通过且清理；实际更新02:01:18 CST成功，宿主程序集提交/模块哈希/root权限/旧策略备份与宿主校验通过。安装态2MiB+7及1GiB+7全字节、原inode保留/目标独立、方法/nonce/owner/版本/计划/取消栅栏与精确清理通过；仅Agent范围，移动BFF/Worker/人类审计/UI仍缺。见[安装态证据](qa/FILE_TREE_MOVE_AGENT.md)。下方“候选/未安装”是历史，完整76项保持active。

- 2026-10-18新增Agent1.28目录移动候选：独立RPC/固定helper/受信loader/显式根授权/旧默认精确升级及安装态验证脚本已生成，尚未部署。32真实Linux移动/loader、152旧文件/root和26目录复制通过；源码最终门禁和实机证据见[移动Agent](qa/FILE_TREE_MOVE_AGENT.md)。线上仍6c5b1d6/1.27。下方“缺RPC”为88d265e内部引擎阶段历史，BFF/Worker/UI仍待接通，76项目标不缩减。

2026-10-13 范围已扩大为 [P0–P5 全量目标](GOAL_P0_P5.md)，原预留已解除；下方旧范围证据不能证明扩展目标完成。工作站已接回，用户确认直接开发、不先做原型；新版完整目标已正式激活。user 全量 sudo 已重新启用并校验，之后由用户自行撤销/改密码；Agent 规则仍受限。断线遗留的临时应用经终态、未暂停和所有权核实后精确清理，正在推进 P0 恢复安全与故障验收。

截至 2026-10-18（Asia/Shanghai），Whale Deck 的 MVP 与 P1 主体已部署，仍有功能细项和验收缺口。本文分别记录源码、已部署运行时和剩余功能；模块拥有页面或通过构建不等于全部功能完成。

- 2026-10-18目录移动新增源码基础（未安装/开放）：独立复制域/回执、完整目标发布后逐文件保留原inode，仅移除已核对空目录，迟到保护/替换/未知结果不整祖先rename、不自动重做或递归清理。30真实Linux（1GiB+7/跨设备/bind/强杀/锁）+176原文件复制+3诊断全部通过，Release935 Unit/120 Integration通过，6原生本机明确跳过。新RPC/Agent固定路由/BFF任务/SQL owner/人类审计/UI仍缺，线上继续6c5b1d6/Agent1.27。见[移动引擎证据](qa/FILE_TREE_MOVE_ENGINE.md)，完整76项不缩减。上一批QA69bee7f的CI39047810258四作业已全部成功；不将它冒称新增移动源码CI。

- 最新实机 **6c5b1d6/Agent1.27** 已覆盖并签收独立目录复制BFF/永久双任务/Worker/共享层级锁owner/人类审计/WebUI。935 Unit/120 Integration、222前端单元/92本地联合文件浏览器、CI39042732294四作业及154 CI浏览器通过；同源码冻结实际PG1执行1通过且清理。实际更新23:11:53 CST成功结束，安装SHA/宿主校验与FilesView HTTP/Gateway/已测dist三方一致。真实OIDC/SQL/Worker/UDS完整2MiB+7复制、原登录/取消/保护/源变更不重做、管理员桌面及320明暗两次复制、刷新仅原GET及普通用户0界面请求/有效CSRF下403通过。四条原文件链路回归与精确清理通过；最终11核心healthy、活跃写入/锁/终端/临时身份0。保留脚本暂态Unknown误判/清理契约假设/浏览器按钮等待失败并修正；详见[最终证据与边界](qa/FILE_TREE_COPY_UI.md)。下方未发布/未接通均为历史。继续目录移动回收/归档、容器文件/长期清理/构建来源及完整76项，不能把目录复制签收当完整文件管理完成。

- 目录复制BFF/持久双任务/共享锁owner/Worker/人类审计源码2ece1c1已推送，CI39038465007四作业及同源码冻结真实PostgreSQL执行1/通过1/清理通过，Release935 Unit/120 Integration通过。配套WebUI与原登记键只读恢复已实现，本地222前端单元/18新增浏览器及生产构建检查通过；追加已开始后传输失败持久Unknown/保锁/不重做与重复失败不写事件风暴。**本批新前后端尚未覆盖发布或真实身份端到端签收**，线上仍e0efb8e/Agent1.27。后续同源码门禁、发布与真实字节/身份/UI验证，目录移动回收/归档、容器文件和完整76项继续。见[页面证据边界](qa/FILE_TREE_COPY_UI.md)。下方“目录BFF尚未实现”为历史状态。

## 历史新增范围与当时继续点

- 2026-10-17最新实机 **e0efb8e/Agent1.27** 已安装独立目录复制RPC和固定helper：CI39031927056四作业及同源码冻结实际PG执行1/通过1/清理通过；实际更新unit20:39:50 CST成功，安装SHA/新根策略/旧策略备份与宿主校验一致。最终QA源码2ae4184的已装UDS探针完成2,097,159及1,073,741,831字节复制，原方法/nonce/owner/版本/计划/取消栅栏、独立全SHA/权限/mtime/登记归属/回执和精确清理通过。探针诊断、错误预期状态和下载漏版本三处问题已修，未改鉴权，保留失败事实；898 Unit/118 Integration、176 Linux及3诊断用例通过。四条原有真实BFF/Worker文件链路复验并清理，11核心healthy、临时身份及活跃写入/锁/终端0；前端资产未变。**目录BFF异步计划、持久任务/锁owner/人类审计及UI仍未实现**，不把Agent探针当作用户功能完成；继续目录移动/回收/归档、容器文件及全部76项。详见[安装态签收](qa/FILE_TREE_COPY_AGENT.md)。下面旧运行时和未安装描述为历史。

- 2026-10-17最新实机 **6dd60dd/Agent1.26** 已覆盖共享层级锁子范围：873 Unit/118 Integration、171 Linux、218前端单元/136浏览器和CI39026564309四作业成功；同源码冻结实际PG执行1/通过1/清理确认，实际更新unit19:07:34 CST成功结束。宿主程序集提交/生产三列模型和API/Worker新实现确认，真实OIDC/BFF/SQL/Worker/UDS的34,603,015字节上传/7秒暂停、单文件变更、文本/HDD及普通文件回收恢复复验通过，自有文件/副本/临时身份清理，活跃写入/锁/终端0、11核心healthy。容器程序集未嵌入Git SHA，不能套宿主核验规则；统一构建来源标记仍待补齐。Python目录引擎仍未安装或开放，继续独立任务/契约/页面、目录移动回收/归档及全部76项。详见[最终签收与边界](qa/FILE_HIERARCHY_LOCKS.md)；下方源码待验说明为历史。

- 2026-10-17共享层级锁新增源码（未部署）：上传/普通变更/回收三流程统一父子范围冲突、保留叶Hash/副本Token、Legacy旧锁从原意图验证还原与事务升级、Unknown/过期租约保锁、不可验证范围失败关闭；独立非破坏迁移保留原主键/外键/锁。Release零警告错误、873 Unit/118 Integration通过，6原生本机明确跳过；新增真实SQL用例待冻结执行。目录引擎be0d98d的CI39024786933四作业已成功（含171文件Linux），自有验证副本已清理。线上仍c60a7c7/1.26，目录复制RPC/任务/UI、移动回收/归档及全部76项继续。详见[层级锁源码边界](DIRECTORY_TREE_OPERATIONS.md#共享层级锁新增源码待原生验收与部署)。

- 2026-10-17目录操作新增内部执行引擎（尚未安装）：完整树清单/全SHA、私有区完整复制、原子不覆盖发布、持久原任务回执和取消栅栏、强杀后只查询不重做；21清单+23复制+127旧回归共171项真实Linux通过，含实际同设备bind别名、不同设备tmpfs、四阶段SIGKILL和外部目标保留。没有接helper/RPC/API、层级锁、Worker/人类审计或UI，不把它签为完整目录管理；线上仍c60a7c7/1.26。继续独立契约/异步计划、共享层级锁及目录复制移动/回收/归档全链路，见[目录执行设计与证据](DIRECTORY_TREE_OPERATIONS.md)。

- 2026-10-17最新实机 **c60a7c7/Agent1.26** 已覆盖小屏长目录指针修复，不再是候选。CI39020311959四作业成功（218前端单元、136浏览器；后端Release/测试/全间接依赖审计和真实PG作业成功）；实际更新unit17:29:48 CST结束，Result=success/ExecMainStatus=0，安装程序集提交一致，FilesView HTTP/Gateway/本地已测JS与CSS SHA256三方一致。真实Authentik管理员320×760明暗、长父目录/子目录正常指针点击与1440×900回归通过，没有强制点击或新文件写请求；空测试目录/临时账号精确清理。11核心healthy与宿主服务正常。下一步目录操作：完整树清单内部基础新增21项真实Linux测试通过，含实际同设备bind mount；尚未接RPC/API/Jobs/UI或安装。完整76项保持active；下方版本和“修复未部署”为历史。详见[小屏最终签收](qa/FILE_ARCHIVE_UI.md#c60a7c7小屏修复实际签收)。

- 2026-10-17 runtime **7ece209/Agent1.26**已覆盖发布普通文件历史/回收/恢复/单HDD版本清理子范围。CI39016011071四作业/135浏览器、856 Unit/117 Integration/127 Linux、同源码冻结实际PG1通过且清理；实际unit16:23:47–16:25:27 CST成功，安装程序集来源/发布SHA和FilesView HTTP/Gateway/已测产物一致。真实OIDC/API/SQL/Worker/UDS/HDD、任务/人类审计、计划刷新/取消与管理员320px恢复下载42字节/普通用户请求0及403通过，原上传/变更/搜索/编辑及8资产回归通过，临时对象/账号清理、11核心healthy、活跃写入/锁/终端0。实机发现长目录中心被固定列遮挡，已有修复源码和新25生产预览/218单元/独立目测，**修复尚未部署**。详见[最终证据](FILE_HISTORY_RECYCLE.md#实际覆盖与已验证子范围7ece209--agent126)；目录、压缩解压、容器文件、隔离/长期清理和全部76项继续，下方未部署为历史。

- 2026-10-17历史/回收站新增源码已接Agent1.26、独立RPC/helper、持久任务/共享路径锁/Worker/审计、五分钟原登录计划恢复与WebUI，**仍未部署**。后端8da9f90：856 Unit/117 Integration、127 Linux、冻结实际PG1通过/清理及CI39011917539四作业成功。前端218单元、类型/lint/格式/生产构建、71文件联合浏览器和最终24生产预览通过，17新用例连续两轮34通过；修复程序关弹窗的迟到取消竞态并保留失败记录。准备真实OIDC/API/SQL/Worker/安装态/HDD验收脚本，尚未执行；线上仍931a24c/1.25。详见[历史回收站](FILE_HISTORY_RECYCLE.md#webui接入与实际链路验收准备新增源码尚未部署)，下一步实际发布与角色/磁盘/页面闭环，再继续目录、容器文件和长期清理；全量76项继续。下方“RPC/UI尚未接通”是历史证据，不代表当前源码。

- 2026-10-17文件历史/回收站基础源码`241e5bc`已推送dev，**未部署**：独立Trash/Restore/Purge状态机、旧元数据兼容、专用签名契约与关系模型/增量迁移/不可变事实及人类Job来源约束；123真实Linux（含强杀/外部改写）及822 Unit/116 Integration通过，6原生本机明确跳过。同源码冻结非超级PostgreSQL实际1通过/清理，真实HDD2,097,167字节回收/恢复覆盖先备份/历史/版本清理和错误UUID拒绝通过、专用目录清理；初轮RESTRICT错误码断言失败保留并修正。CI39006685063四作业最终completed/success。线上仍931a24c/Agent1.25，宿主服务/运行时及Pkcs10.0.13、动态入口、UDS/权限和11核心healthy复核；新Agent RPC/固定helper、BFF任务/锁/Worker/审计和页面尚未接通，没有开放新的生产写入口。见[基础源码证据](FILE_HISTORY_RECYCLE.md#基础源码签收241e5bc尚未部署)。完整76项保持，下一步继续闭环。

- 2026-10-17最新runtime **931a24c/Agent1.25**已覆盖并签收UTF-8预览/编辑、BOM/换行、版本冻结保存及直接覆盖/Worker发布统一完整HDD前置备份子范围。807 Unit/115 Integration（Windows六原生跳过）、100 Linux、215前端单元、81本地/118 CI联合浏览器及四作业CI38998917321成功；同源码冻结PG实际1通过且清理。首次216e7cc安装误要求用户所有的HDD祖先均root，已修而未改共享目录所有权；保留真实失败。修正更新11:43:20–11:45:22 CST成功、安装SHA一致，实际Cookie/API/SQL/Worker/UDS/私有HDD旧字节及审计、33MiB+7/7秒停顿和原变更/目录搜索/8资产回归通过。真实管理员320px明暗编辑确认、一次正文/独立42字节下载、普通用户直达/刷新0文件界面请求及文本403通过；临时账号/文件/HDD副本/本地浏览器和验证服务器清理，11核心healthy、活跃写入/锁/终端0。见[最终运行时账本](qa/FILE_TEXT_HDD.md#最终运行时签收931a24c--agent125)。历史/回收站/恢复、目录操作/归档、容器文件及隔离清理仍缺；P2-04仅从待实现改为部分实现，完整76项继续。

- 2026-10-17最新runtime **036dfcb/Agent1.24**已覆盖发布并签收两处边界修复：普通/目录列表按实际JSON转义字节预算分页、游标无丢项；经典滚动条下320px顶栏主题/角标不裁切。771 Unit/115 Integration、78真实Linux、204前端单元/63本地联合浏览器及CI38986541530四作业（100联合浏览器）成功；同源码冻结PG实际1通过且清理。实际更新08:40:19 CST成功结束、安装SHA一致，真实长名称BFF/UDS、变更/审计、宿主与8资产/11核心healthy通过；真实新管理员网页305px内容区与主题/搜索/滚动目测、产物CSS三方对应已记录。见[运行时证据](qa/FILE_LIST_HEADER_BOUNDARIES.md#最终运行时签收036dfcb--agent-124)。下一步编辑/覆盖统一HDD挂载身份与前置备份、回收站/归档、容器文件及隔离/长期清理；完整76项仍active。

- 2026-10-17最新runtime **d3e49c1/Agent1.24**已覆盖目录/续页节点键命名空间修正，支持合法more:目录与“加载更多”共存；204前端单元/14目录树浏览器、类型/lint/生产构建和CI38980535186四作业成功。实际更新07:32:04 CST成功结束、安装程序集SHA一致，真实OIDC/API/UDS/root搜索/分页/版本/保护及宿主/8资产/11核心健康复验通过；活动FilesView HTTP、Gateway与本地已测生产产物SHA全等。服务端与eee26bf无差异，工作站冻结PG及完整管理员/普通用户网页证据仍明确归属eee26bf，不把组件fixture冒称生产100目录演练。详见[最终覆盖证据](qa/FILE_BROWSER.md#最终覆盖d3e49c1--agent124)。下一步窄屏顶栏内容区域、普通/目录列表转义后的帧预算、编辑与覆盖统一HDD备份；完整76项继续。

- 2026-10-17 runtime **eee26bf/Agent1.24**目录树与递归字面名称搜索已覆盖签收：有界扫描/分页、私有MAC游标/所有祖先版本、保护目录/链接/文件与目录bind mount、停止/旧响应隔离及明确部分结果。771 Unit/115 Integration、77 Linux、203前端、86 CI联合浏览器和同源码冻结PG实际1通过且清理；CI38973477051四作业成功。实际更新06:15:23 CST成功结束，真实OIDC/BFF/UDS/root、管理员90字节上传/搜索/定位/类型大小写/下载SHA和普通用户首次/刷新0界面请求及有效CSRF下403通过。原变更/审计/资产/宿主与11核心健康、私有Key0600/32字节/单链接通过，自有文件和临时账号清理。QA脚本参数重名、浏览器收尾超时与后续QA-only CI取消均保留边界，见[实机账本](qa/FILE_BROWSER.md#最终运行时签收eee26bf--agent124)。下面1e72ac4和未部署描述为历史；下一步编辑前HDD备份（覆盖上传同策略）/回收站归档/容器文件和隔离，完整76项继续。

- 2026-10-17 已部署runtime **1e72ac4/Agent1.23**：新建目录、普通文件复制/移动/权限已走通真实OIDC→API/SQL→Worker→UDS及管理员网页，永久任务/共享路径锁/人类审计和普通用户拒绝通过。CI38962862118四作业成功，757 Unit/115 Integration、61 Linux、200前端单元、12角标及26文件/搜索浏览器回归；同源码冻结PG执行1通过且清理。实际更新04:41:25 CST成功结束，最终BFF回归、宿主PKCS10.0.13/动态入口与11核心healthy复核通过。320px真实页主题角标碰撞已修；详见[最终运行时证据](qa/FILE_MUTATION_WORKFLOW.md#最终运行时签收1e72ac4)。下面未部署描述为历史。下一步目录树/搜索、编辑前HDD备份、回收站/归档、容器文件和隔离清理；完整76项仍active。

- 2026-10-17 最终源码**3e2da89**（未覆盖运行时）：文件变更BFF/任务/Worker/人类审计/API/WebUI全部接通本次子范围，757 Unit/115 Integration、200前端单元/24文件浏览器及Release/类型/lint/格式/生产构建通过；CI38954590064四作业全成功。同源码冻结PG实际执行1通过并清理，含跨工作流锁、双连接共享源争用、实际Job锁等待后过期、HMAC/JSONB、取消/Unknown和审计事务失败；相同root引擎61 Linux通过。独立生产构建浏览器目测及手机/主题交互通过，验证预览/浏览器已清理。11核心healthy且宿主服务正常，实际runtime仍3f47300/Agent1.22。下一步覆盖部署及真实OIDC/BFF/Worker/UDS/网页验收，见[最终源码记录](qa/FILE_MUTATION_WORKFLOW.md#最终源码签收3e2da89尚未覆盖运行时)。不签完整文件/终端或76项。

- 2026-10-17 文件变更BFF/SQL/Worker/API与WebUI已继续实现，当前处于最终原生验证/待覆盖签收：永久HMAC意图、共享上传/源/目标锁、Job/Outbox/人类审计、1.23取消tombstone、旧请求冻结恢复和Unknown显式处置；页面新增目录及文件行更多→复制/移动/权限，路由任务ID支持刷新。后端77e73ac已通过756 Unit/115 Integration、四作业CI38950535172和同源码真实PG/清理，Linux61项通过；新UI200单元/24文件浏览器、类型/lint/格式/生产构建及独立目测通过，正在补双连接争用/真实锁等待后租约验证和新UI CI。真实工作站运行时仍3f47300/Agent1.22；新Probe/BFF验证脚本准备不算已执行。详见[工作流账本](qa/FILE_MUTATION_WORKFLOW.md)。完整76项继续，不把源码实现当作部署或完整文件管理。

- 2026-10-17 runtime **`3f47300`** 文件变更Agent基础已覆盖部署：1.22独立Plan/Apply/Inspect强类型RPC、固定helper/安全模块加载、仅旧默认注册表升级/保留自定义、与上传共用namespace提交锁。733 Unit/115 Integration、57真实Linux、Release及CI38935700816四作业全绿；同源码冻结PG实际1通过/清理。实际更新unit01:04:26 CST成功终态；同源码原生UDS探针通过实际已安装helper/sandbox的目录/复制/移动/权限、能力/重放/保护/旧版本/owner、原文件保留与历史不重做。原33MiB上传下载/7秒停顿、身份/权限、宿主与容器终端、通知/8资产、安装态验证与11核心healthy通过，临时账号/文件/活跃传输0，PKCS10.0.13及sudo保留。详见 [文件变更Agent签收](FILE_MUTATIONS.md#最终安装态agent子范围3f473002026-10-17)。新变更BFF/SQL/Worker/人类审计/UI尚未实现；目录树/编辑HDD备份/回收站/归档、容器文件/隔离/清理与全部76项继续，不签完整文件管理。

- 2026-10-16 后续文件操作执行引擎本地增量：mkdir、普通文件copy/move/chmod、root私有回执、源隔离保留和不重做副作用的恢复已实现；独立验收副本的20项新真实Linux用例与28项原通道合计48通过，含真实SIGKILL/内核锁/不同设备tmpfs与并发外部变更保留。没有注册Agent/HTTP写入口、安装新模块或改变默认根权限，运行时仍15378ec；不把加载函数测试当作安装态/端到端/完整文件管理。详见 [文件操作契约与证据](FILE_MUTATIONS.md)。下一步接专用Agent契约/固定helper/安装权限升级、BFF永久任务/审计/Worker和UI，再继续目录树/编辑HDD备份/回收站/归档等全部76项。

- 2026-10-16 runtime **`15378ec`** 登记目录文件管理BFF/持久意图与Job/Worker/人类审计/WebUI已覆盖部署并签收子范围：710 Unit/115 Integration、189前端单元/47联合浏览器、Release、四作业CI38914558130全绿，同源码冻结PG实际1通过/清理。实际更新unit22:25:41 CST成功结束；真实OIDC→稳定代理→Gateway→API/SQL/Worker→UDS/root驱动验证33MiB+7上传、7秒暂停、完整下载/覆盖保留/幂等/登录隔离/取消tombstone/坏摘要/旧版本/审计；真实网页管理员手机上传发布、独立下载SHA一致，普通与匿名surface0/文件请求0。首版stage提前关闭stdin导致409已实测修复，不抹去失败。原终端/通知/8资产/宿主验证与11核心健康通过，PKCS10.0.13和sudo保留；临时文件与账号清理，回执审计保留。详见 [文件管理签收](qa/FILE_MANAGEMENT.md#最终已部署子范围15378ec2026-10-16)。继续编辑/搜索/复制移动/权限/归档、Unknown人工处置、容器文件、回收站/HDD与隔离/长期清理；P1-11/P2-03和全部76项仍active。下方为历史。

- 2026-10-16 runtime **`6bf4222`** 登记目录文件传输Agent基础已签收：协议1.20，安全路径/流式二进制/版本冲突/私有前序对象/幂等/断线回执；实际systemd的RestrictSUIDSGID拦截openat2及拒绝清理刷新两个实机失败已修复。Release、700 Unit/100 Integration、22真实Linux文件、CI38896550960四作业与同源码冻结PG实际1通过/清理。更新unit19:54:10 CST成功结束，同源码UDS探针和实际安装helper各2MiB+7上传校验通过、前者完整下载/覆盖/重放/权限/断线通过；测试文件和transient清理。身份/宿主与容器终端/通知/8资产/宿主validate通过，11核心healthy、PKCS10.0.13和sudo保留。见 [原始失败、证据和边界](FILE_TRANSPORT.md#最终安装态agent签收6bf4222)。文件API/人类审计/任务/Worker与WebUI、容器文件及回收站/HDD/隔离仍缺，P1-11/P2-03仅部分实现，全部76项继续。下方为历史。

- 2026-10-16 最新已部署runtime **`085acef`** 容器终端BFF/共享WebUI子范围签收：683 Unit/100 Integration、176前端/35浏览器与四作业CI全绿，新增非超级用户CI PostgreSQL实际升级/约束/持久审计1通过；同源码工作站冻结PG实际1通过/清理。更新unit18:07:25 CST成功终态，真实OIDC/BFF/UDS/Exec、核心保护/CSRF/Origin/作用域/幂等/resize/exit/撤销及原Host通过。真实网页临时管理员中文IME/读屏/手机/主题/清屏/exit7/重连断开、普通用户首次/直达/刷新403且surface0通过；独立核实账号/助手/临时容器0、所有会话Closed。通知/8资产/宿主UDS/动态入口及11核心healthy，PKCS10.0.13与sudo保留。初始离线、UI locator及600秒fixture到期失败没有抹去，见 [最终实机记录](qa/CONTAINER_TERMINAL_BFF_UI.md)。继续文件传输、租户隔离/长期清理和更广故障；P1-11及完整76项仍active，下方为历史。

- 2026-10-16 源码 `f81324c`：容器终端BFF目标/启动实例/shell一次性许可、持久字段/SQL约束/人类主体审计、实际字段HMAC复查、共享宿主/容器WebUI及真实容器入口已实现。Release、683 Unit/100 Integration、176前端单元/35本地浏览器、类型/lint/生产构建与全间接依赖审计通过，6原生明确跳过；新真实BFF验收脚本仅准备，尚未执行。工作站两个已知网卡SSH超时，未部署、未推断健康，最后签收runtime仍a0b0001。CI与更细失败边界见 [本轮记录](qa/CONTAINER_TERMINAL_BFF_UI.md)。下一步同源码冻结PG/覆盖发布及真实OIDC/BFF/容器浏览器验收，再继续文件传输/隔离/清理；完整76项仍active，下方为历史。

- 2026-10-16 runtime `a0b0001` 容器Exec Agent基础已覆盖部署并实机签收：独立1.19 RPC、完整ID/启动实例/固定shell，注册表+标签保护、容器隔离和pidfd交互进程清理；676 Unit/92 Integration、15驱动策略、Release/CI 38875103617全绿，冻结原生PG实际通过/清理。同源码原生UDS探针在无网络/无生产卷的非root临时容器验证UID1000/40×120/exit7、重放/重复、断线/仅强杀测试驱动后Exec消失而PID1不变，临时容器精确删除。首次HTTP升级早于实际PID导致的两次失败保留并修复，见 [Agent证据](qa/CONTAINER_TERMINAL_AGENT.md)。更新unit16:45:10 CST终态成功，原身份/宿主终端/通知/8资产与UDS/宿主及11核心健康通过，PKCS10.0.13保持、sudo未改。容器BFF持久授权/人类审计、UI、文件传输、团队隔离与长期清理仍缺；P1-11及76项继续，下方为历史。

- 2026-10-16 runtime `1068b27` 已覆盖发布并签收宿主运行库安全补全与受限宿主终端WebUI子范围：实际Agent/维护包、API共享PKCS与Worker包均核实10.0.13；SDK10.0.402锁定摘要，发布/安装/在位校验，保持NuGetAudit与警告即错误。CI 38868581495全绿、646 Unit/92 Integration、171前端单元及3项生产授权浏览器门禁；冻结原生PG实际1通过/清理。更新unit15:43:57 CST终态成功；真实终端静默6秒正常Close/尺寸/退出持久化/重放、身份/通知/8资产和宿主UDS/权限/动态入口与隔离维护页通过，11核心healthy。实机UI管理员中文/IME/主题/resize/退出及普通用户冷启动403证据见 [终端UI记录](qa/HOST_TERMINAL_UI.md)，原5秒代理EOF及检查器误要求API本地DLL的失败保留并修正；[安全记录](qa/DEPENDENCY_SECURITY_2026_10.md)区分初版容器修复与本次宿主遗漏补全。不作入侵取证结论；P1-11仍缺容器Exec/文件传输/团队隔离/长期清理，全部76项继续，以下runtime为历史。

- 2026-10-16 runtime `8c5ac86` PKCS 安全修复已签收：核实 MailKit→MimeKit→Pkcs 10.0.0，显式锁定10.0.13、保持全间接依赖审计和警告即错误；容器 .NET/ASP.NET 基础镜像同步锁定10.0.13官方摘要。CI 38855112870全部绿色，643 Unit/92 Integration、Release和依赖审计通过；冻结原生PG/TLS SMTP派发实际1通过/清理，未发送外部通知。真实更新unit13:25:20 CST成功结束；API/Worker依赖清单和DLL哈希、Migrator镜像、运行时均核实修复版本，11核心healthy；真实OIDC/权限/通知/宿主终端/8资产与宿主回归通过。见 [安全修复证据](qa/DEPENDENCY_SECURITY_2026_10.md)。仅关闭此依赖问题，不作未遭入侵的取证结论；本地终端WebUI仍在开发、未随此补丁提交部署，全部76项继续。

- 2026-10-16 runtime `5bc2551` 受限宿主终端后端签收：642 Unit/92 Integration、Release无警告错误，冻结原生PG执行1/通过1/清理；真实OIDC/稳定代理/Gateway/BFF/UDS/PTy、一次性/幂等/Origin/尺寸/exit持久化/关闭握手和撤销通过。普通/匿名新终端全部入口403/401、原任务1006事件/通知全链路/8资产/UDS/宿主回归通过，更新unit12:31:12 CST成功结束、11核心healthy、sudo保留。首两次EOF失败与未放宽断言的修正见 [BFF证据](qa/HOST_TERMINAL_BFF.md)。前端尚未生成，本机pnpm新依赖TLS失败，未改全局代理或关闭TLS验证；继续前端、容器Exec/文件传输、隔离/清理与全76项，不能当作完整终端完成。

- 2026-10-16 runtime `c9568e3` 宿主PTY基础实际签收：610 Unit/86 Integration、Release及冻结原生PG通过/清理；同源码原生UDS探针验证固定非特权身份、尺寸/exit、能力/UUID重放、活跃重复不误停、断线/仅强杀测试driver后setsid子进程清理与正文不进入journal/root记录。实际更新unit11:30:52 CST成功结束，任务SSE/通知私有链路/权限/8资产/UDS/宿主回归通过，11核心healthy、sudo保留。见 [证据与首版失败记录](qa/HOST_TERMINAL_PTY.md)。P1-11只部分实现，BFF票据/审计/撤权、前端、容器Exec/文件传输仍待继续，76项不缩减。

- 2026-10-16 最新已部署runtime `1dd3fa5` 修复通用任务SSE终态竞争、500条以上历史尾部、camelCase协议与浏览器续传/去重/清理。591 Unit/86 Integration、146前端单元/27联合浏览器及Release/类型/lint/构建通过；冻结PG两个连接的1006事件实际通过并清理。宿主更新成功结束，真实OIDC/Gateway流中途追加终态、1006完整/续传/尾部/400/404/401与临时清理通过，私有配置/权限/8资产/UDS/宿主回归通过，11核心healthy、sudo保留。见 [本轮证据](qa/JOB_EVENT_STREAM.md)。P0-03与全部76项仍未完整签收，生产身份UI与其他大功能继续。

- 2026-10-16 最新已部署runtime `df8f00a` 签收通知私有配置取消/租约/未知确认增量：591 Unit/73 Integration、Release、133前端单元/23浏览器、类型/lint/构建通过；冻结原生PG实际执行1/通过1并清理。覆盖发布unit成功结束，真实OIDC→API→Valkey→Worker→Agent应用/移除/永久重放、已结束任务SSE终态/Last-Event-ID、权限/CSRF/Origin、8 HTTP资源、UDS及宿主验证通过，11核心healthy，sudo保留；外部发送0。详见 [本轮证据](qa/NOTIFICATION_PRIVATE_BOUNDARIES.md)。生产身份UI、通用SSE实时终态竞争、更多通知/厂商验收及全部76项继续，下方runtime均为历史记录。

- 2026-10-16 最新runtime `9d5775e` 已签收重启后维护状态修复；576 Unit/73 Integration与Release通过，冻结原生dispatch执行/通过并清理、真实安装后二进制独立HTTP验证通过，宿主更新08:44:30成功结束。此前eabac51私有配置真实应用/移除/永久重放、版本及发送阻断、权限/CSRF/Origin/UDS、8资产字节与宿主通过，外部发送0。MariaDB启动挂载故障已恢复，数据库平台a3f734a启动依赖实际安装且未重启Docker，缺失配置bind原生拒绝通过；没有重建数据库、重启主机或卸载HDD。见 [重连与P0修复](qa/BOOT_MAINTENANCE_STATUS.md)。生产身份UI、更多通知故障组合、完整重启演练及76项全量仍继续。

- 2026-10-16 通知私有配置 WebUI 已实现并本地验证：结构化输入/保留替换移除、计划确认/冻结原请求重试、任务状态/未知确认、关闭与降权清空、渠道版本及发送阻断提示；562 Unit/73 Integration、Release/类型/lint/前端构建和20浏览器联合回归通过，6原生本机跳过。两个已知工作站网卡 SSH 当前均超时，本增量未调度部署、未执行新的原生或远端验收，最后已签收 runtime 保持历史 `39c990b`，不推断当前健康。见 [UI恢复点](qa/NOTIFICATION_PRIVATE_UI.md)。P4-06/76项完整目标仍在推进。

- 2026-10-14 最新 runtime `39c990b` 已验收通知私有配置后端流程：管理员签名 Plan/确认202 Job/Status/Unknown确认、Valkey短时输入且PG不存目标密码或票据、独立Worker租约/回执优先、原子入队暂停渠道/Job/Outbox/审计、发送阻断。561 Unit/73 Integration、Release、冻结原生PG新增流程/恢复/清理通过，真实 OIDC→API→Valkey→Worker→Agent应用/移除与永久重放通过，渠道始终停用、外部发送0、临时对象精确清理/审计保留；普通匿名新入口拒绝、migration/UDS/8 HTTP资源/宿主和11核心健康及sudo通过。见 [流程验收](qa/NOTIFICATION_PRIVATE_WORKFLOW.md)。下一步配置 WebUI、更多并发/取消/Unknown人工确认故障组合；P4-06与76项仍未整体完成。

- 2026-10-14 最新 runtime `71b0b43` 签收通知私有配置宿主基础：Agent 1.17 独立契约、固定 root 原子 writer、回执/过期输入恢复/被取代标记、HMAC 不透明版本与受限 transient unit。553 Unit/73 Integration、Release 及 18 条真实 Linux 文件系统/并发/恢复用例、冻结原生依赖回归通过；首次 5b166ab Inspect 只读命名空间失败已实测并修复覆盖。最终真实 UDS 读取/能力重放与危险输入拒绝、生产 registry 不变、OIDC/8 HTTP资源/宿主与11核心健康、sudo 保留。见 [私有配置宿主验收](qa/NOTIFICATION_PRIVATE_CONFIGURATION.md)。下一步仍须 API/永久幂等审计/跨存储一致性/凭据暂存与 WebUI；此模块和76项整体不算完成。

- 2026-10-14 工作站重连后最新 runtime `fe3e034` 已签收通知显式测试与 Unknown 确认重试子项：原记录不可变、关联子记录/单父去重、执行前权限复核、版本/确认/CSRF/幂等/审计与 UI；543 Unit/73 Integration、119 前端、18 联合浏览器回归及 Release 全方案通过。冻结原生 PG/实际本地签名 HTTP 响应丢失/Unknown 不重发/人工子记录、TLS SMTP 与清理实际通过；宿主 update 成功终态、维护 healthy、11 核心健康、实际 OIDC 拒绝契约/OpenAPI及8 HTTP资产一致、动态入口/UDS/权限通过，sudo 保留。详见 [显式意图验收](qa/NOTIFICATION_EXPLICIT_INTENTS.md)。下一步私有目标/凭据 WebUI 与 Agent 安全写入；没有生产群/邮箱发送，P4-06 和 76 项整体仍未完成。

- 2026-10-14 最新runtime6ad5970签收通知Sender增量：私有绑定revision、Email/Webhook/三种Bot适配、独立Worker、尝试历史API/UI、每目标与渠道共享SQL限流；538 Unit/73 Integration（6原生本机跳过）、119前端及36浏览器、Release/类型/lint/构建通过。冻结原生Fact实际执行HTTP签名接收/共享限流/轮换取消/TLS SMTP并清理，实际OIDC元数据回归/8个HTTP资源核对通过。第一次Git TLS失败未创建任务，核实后重试；实际unit成功终态、两个migration落库、11核心healthy、sudo保留。没有启用生产渠道或真实群/邮箱；仍缺WebUI私有值管理/专用Agent写入、测试发送/确认未知重试及厂商真实验收，76项整体未完成。见 [Sender签收](qa/NOTIFICATION_SENDER.md)。

- 2026-10-14 最新运行时e5b2cfc签收通知渠道/路由配置与投递查询API/UI：498 Unit/73 Integration（6原生本机跳过）、119前端、36浏览器及手机空态4复测、Release/类型/lint/构建通过；f7c3f36冻结原生管理/派送基础Fact真正执行1/通过1/清理，实际OIDC权限/CRUD/幂等/版本/CSRF/Origin/OpenAPI通过，8个HTTP资源与Gateway逐字节一致。实际更新unit成功终止、11核心healthy、Agent/维护/timer active、sudo保留、临时测试配置/账户清理。外部发送/签名/Worker等仍待实现，76项目标不缩减；下方runtime是历史。见 [本轮最终签收](qa/NOTIFICATION_MANAGEMENT.md#工作站最终签收)。

- 2026-10-14最新runtime4ec6e86已签收：修复认证前按用户限流实际退化成代理IP共享及默认503，改认证后、主体/匿名IP与会话/操作独立有限预算、429/Retry-After和中文提示、不重复提交写操作。476 Unit/73 Integration（6原生本机跳过）、113前端、30浏览器、Release/类型/lint/构建通过；真实Cookie同入口120/121、第二用户独立、401/403、额满仍CSRF/面板退出、原消息Worker回归通过。更新unit最终成功、7个HTTP分块匹配Gateway、11核心健康，sudo保留。详见 [限流签收](qa/API_RATE_LIMITS.md)。完整通知配置/渠道和76项未完成。
- 2026-10-14重连最新签收runtime `ad0e661`：先核实原bc39f46更新成功，未重发原任务；新源码3个独立原生套件实际各执行1/通过1并清理，真正覆盖100005全101批消费/两次替换/多主体有界清理/存量迁移和消息投递基础兼容。之后覆盖发布，更新unit成功终止、新migration落库、11核心健康、真实身份/API/Worker及HTTP前端资产核对通过，临时测试资源清理，sudo保留。详见 [新快照最终签收](qa/NOTIFICATION_READ_SNAPSHOTS.md#重连后最终签收ad0e661)。以下断线/待原生记录是历史；外部发送/配置页面/完整通知仍缺，不算76项全部完成。
- 2026-10-14 服务器再次断开，本地继续。最后已签收 runtime 为 `4aa036b`；`bc39f46` 更新 unit 曾确认运行中，但最终结果未知，禁止因断线直接重发；`3448a3d` 真实 API 增强脚本尚未执行。冻结 `dac41f0` 的 snapshots/inbox/dispatch 三份原生报告均 Passed 且已清理，不冒称新版本已上线。
- GLM 方案审查后，本地补 `NotificationSnapshotBoundedRetention`：替换原子失效旧头而非级联删除，过期快照全局每轮最多清1000成员/10个空头；新增部分唯一索引、保留旧数据的 migration/安全降级前置检查、离线模型测试及十万条完整消费/多主体清理原生用例。Release 0警告错误、476 Unit/64 Integration真正通过，6原生本机跳过；新增真实路径未执行、没有远程操作或发布。原生待验，不标整个通知或76项完成。见 [快照记录](qa/NOTIFICATION_READ_SNAPSHOTS.md) 与 [方案审查](qa/PLAN_REVIEW_20261014.md)。以下旧“下一步复现/当前运行时”按日期与本条区分。
- 2026-10-14 投递基础 `9d534b3` 已提交：渠道/路由元数据、逐事件回执与同事务去重、SKIP LOCKED 租约、发送前后恢复、保守 Unknown 和最多五次安全重试。476 Unit/62 Integration（5 原生本机跳过）、Release 0 警告/错误及独立 PostgreSQL 真正低序号晚提交/并发抢占/版本/保留通过并清理。未接传输、Worker/API/配置 UI，未生产应用本 migration；线上保持 `4aa036b`，零外部发送。下一步优先原生复现并修复站内全部已读的晚提交快照边界，再继续渠道端到端；不是整个通知模块或 76 项完成。见 [投递实施记录](NOTIFICATION_DELIVERY.md)。
- 2026-10-14：消息中心及实时 IOPS/延迟卡片修复已在 `4aa036b` 覆盖发布并验收，近期有效/Idle/过期/明确不可用分开，汇总不混合不同采样格；98 前端、42 本机 fixture 浏览器、Release 全方案 0 警告/错误与 460 Unit/62 Integration 通过。消息中心隔离原生 `0971930` 实际执行通过并清理；真实 OIDC/API/Worker 权限、读状态、触发恢复及四窗口 78 完整桶独立比对通过，生产前端资源一致。更新 unit 成功结束，11 核心健康，Agent/维护/timer active，sudo 保留，临时账户/SDK/数据库清理；历史保留。外部通知和长期压力仍缺，76 项未完成。详见 [实时卡片证据](qa/TELEMETRY_CURRENT_CARDS.md) 和 [消息中心证据](NOTIFICATIONS.md)。
- 站内消息中心源码已提交 `51067a4`，尚未发布：告警/任务同事务事实、个人与管理员受众、独立已读/快照批次、180 天有界清理及顶部/抽屉前端。460 Unit/62 Integration（4 原生跳过）、89 前端、39 浏览器及 lint/类型/生产构建通过；原生旧告警/容量回归通过。消息原生第一轮 600 秒执行器超时并清理，增加脱敏阶段定位后继续，不当作通过。线上仍 `3c26e13`，外部通知路由/投递仍未实现，P0/P2/P4 整项未签收。详见 [消息中心当前证据](NOTIFICATIONS.md)。
- 主机负载/systemd/Docker 健康告警在 `3c26e13` 发布：Agent 1.16 的 1/5/15 分钟与按核心归一化负载、>100% 规则、登记服务命名属性、运行中 unhealthy 检测；独立评估循环在采集/Agent 失败时继续，失败日志限频。438 Unit/62 Integration（3 原生本机跳过）、74 前端、11 浏览器与真实隔离 PostgreSQL（包含实际 hosted 离线评估器）通过；真实 Authentik/API/Worker 验证 32 核/4 负载/3 单元/11 容器及临时 Info 规则触发禁用恢复。更新服务成功结束、11 核心健康、sudo 保留、测试资源清理；规则/审计历史保留。详见 [健康告警验收](qa/HEALTH_ALERTS.md)。P0-07 仍需通知路由/已读/外部投递、长期压力及更广故障组合；整体 76 项未完成。
- 单位与清理预检修正在 `c671b7e` 覆盖发布：容量/速率统一二进制标签、网卡速度保持十进制 bit/s、时长自适应、未知不伪造 0；悬空镜像估算与实际过滤范围对齐。412 Unit/62 Integration（3 原生本机跳过）、74 前端、50 浏览器回归通过；真实预检为 0 悬空镜像/0 B，镜像/卷/容器清单未变，未执行清理。实际更新服务成功结束，11 核心健康，Agent 仍 1.15。详见 [单位验收](qa/DISPLAY_UNITS.md)。继续 P0-07 负载/systemd/Docker 独立健康状态及通知；76 项整体未完成。
- 告警状态与规则修复已在 `57a94d3` 发布：利用率窗口批量查询、资源/指标设备筛选、覆盖/质量/未知、恢复阈值/严重升级、静默到期、禁用恢复、活动唯一指纹与乐观并发；规则/确认/静默统一审计与状态同事务。402 Unit/62 Integration（3 原生本机明确跳过）、44 前端、47 项独立浏览器回归及可选字段复测通过；真实隔离 PostgreSQL 告警/备份容量与真实 Authentik/API/Worker 权限/流转/审计通过。11 核心健康、Agent 仍 1.15、sudo 保留、测试用户及 SDK/数据库清理，临时规则禁用/事件恢复，审计历史保留。详见 [告警验收](qa/MONITORING_ALERTS.md)。P0-07 仍缺负载/systemd/Docker 独立健康状态、完整通知及长期日志压力；76 项整体不算完成。
- 前端搜索紧凑与长名称省略已在 `b05057b` 覆盖发布：入口/提示简化，标题和描述单行省略并保留全文，结果限高内部滚动；24 布局回归/44 单元、构建/类型/lint 通过，桌面/手机截图及实际 HTTP 资源检查通过。实际更新服务成功结束，11 核心健康，真实 Authentik 管理视图验收通过；详见 [搜索布局验收](qa/SEARCH_LAYOUT.md)。Agent 仍 1.15，未改变权限；76 项总目标仍未完成。
- 同日用户新增开发空间/多语言开发环境、Git/上传导入、远程调试测试和自动报告。已补全 [专用设计](DEVELOPMENT_WORKSPACES.md) 并同步页面/用例/RPC/权限/模型/路线，P3-13–P3-18 共 6 项；完整 P0–P5 清单现在为 **76 项**。本轮只修改范围与契约文档，空间功能/API/表尚未实现，当前 Agent 仍 1.15，未更新运行时或扩大权限。
- P0-07 指标子项已在运行时 `3b5b3d9` 覆盖发布并验收：完整分钟/小时、first/last/质量/完成 IOPS 权重，脏 revision/receipt/游标、14 天回填与迟到并发；336 完成小时、8413 小时 rollup、backfillComplete=true。380 Unit/62 Integration（2 原生本机跳过）、44 前端及 7 浏览器专项通过；独立原生功能/276 万冷查询 12491ms。修复第一轮 7d 503 后生产四窗口/78 完成桶独立比对通过，7d 2.951s，401/403/管理员通过，4133024 冻结原始样本未变，11 核心健康、Agent 1.15、sudo 保留、临时资源清理。详见 [聚合签收与问题历史](qa/METRIC_AGGREGATION.md)。不代表 P0-07 全部负载/服务告警、长期日志压力或整个 76 项已完成，继续其他 P0/P1 缺口。
- 本轮文档校验：76 个任务编号唯一、6 页面/10 用例/7 RPC/10 实体/9 权限共 42 个专用定义无冲突，包含 README/实施状态的 11 处设计链接及章节锚点检查通过，Git diff 无空白错误。未修改运行代码，因此没有冒称重新执行 Release/原生/UI 功能验收。GLM `glm-5.3-flash` 经路由器只读盘点已有文档，GPT 独立复核并修改；路由器记录输入 1,176,384 Tokens（缓存 1,088,704）、输出 18,146、506070ms、1 次成功/0 重试，费用未返回。不采用其未获准读取的文件/未核验专用设计的结论，链接与编号由实际检查证明。

## 当前运行状态

本节保留2026-10-12至14日运行快照，以下“最新”只在当时成立；当前最后记录版本见文首，不把这些旧版本作为今日运行状态。

- 最新已验收runtime `4ec6e86`（2026-10-14）：多人额度与额满会话可用性修复发布，真实身份/Worker/HTTP分块签收；下方ad0e661等版本为历史。源码后续文档/验证提交不代表另一次部署。
- 最新已验收runtime `ad0e661`（2026-10-14）：实体集合快照和成员有界清理已上线；下方4aa036b及其他runtime为历史，不再当作当前版本。源码后续仅文档/验证测试提交不代表再次部署。
- 最新运行时 `4aa036b`（2026-10-14）：站内消息中心与实时卡片修复发布并真实验收，保留 `3c26e13` 健康告警、搜索修复与指标聚合；详见本页上述最新签收。下面版本为历史证据，不能当成仍在运行的版本。

- 新版 P0 历史基线 `ad4f0d8`、Agent 1.15：root journal/清理 schema 安全修复，Linux 38 helper + 8 PID 绑定测试通过；Release 377 Unit/62 Integration（新增原生容量 Fact 本机明确跳过）。独立应用真实共享卷/失效计划拒绝、SIGKILL 后原任务失败并恢复原容器、partial 清理、坏归档拒绝通过，临时资源清理。更新服务成功，11 核心健康，user 全量 sudo 按新授权保留、Agent 规则未扩大。详见 [P0 恢复验收](qa/P0_RECOVERY.md)。不是 P0–P5 整体完成。

- 最终界面细调运行时为 `723ba55`，Agent 协议仍为 `1.15`：手机只读容量规则提示不再藏在横向滚动后。补充容量/布局 34 项通过，部署单元正常结束，真实 HDD 容量、迁移和生产管理员 OpenAPI 复验通过。下方 `a9deae8` 为同一容量后端实现的验收基线。

- 2026-10-13 当前运行时 `a9deae8`、协议 `1.15` 增加实际 HDD 容量只读 RPC/API 与每策略预警/临界告警、去重、升级、静默到期、未知保护、恢复和策略禁用；自动规则不能由通用编辑覆盖。隔离 PostgreSQL 以合成容量样本验证全部状态落库，生产容量另与原生文件系统对照；普通用户 403，策略备份/保留复测通过。Release 377 Unit + 62 Integration，本机新增原生测试明确跳过，远程隔离库另验通过；Linux 隔离 32、前端 43 单元/96 浏览器通过，11 核心健康。手机告警已修为卡片，详见 `docs/qa/BACKUP_CAPACITY.md`。下方保留前阶段证据，不代表当前版本仍是 1.14。

- 2026-10-13 当前运行时更新至 `adf5068`，Agent 协议 `1.14`。应用备份前端、自动策略、每日及备份后保留清理、Expired 历史已上线；真实普通用户读取 403、策略确认/调度/禁用/并发版本、只删除旧私有归档通过。配置/bind/独占卷、取消后恢复及 Agent 重启接续同一任务复测通过，并记录存活服务的实际资源配额与沙箱。Release 363 Unit + 62 Integration，Linux 归档隔离 31 项、前端 43 项单元与 84 项完整浏览器回归通过，11 个核心容器健康。强杀/断电、扩大真实失败验收及策略容量预警仍待补齐，详见 `docs/qa/APPLICATION_BACKUPS.md`。下面保留上一阶段基线，不表示当前版本仍是 1.12。

- 2026-10-13 最新运行时为 `a3a7971`，Agent 协议 `1.12`。应用参数编辑 RPC/API/Worker 与前端已上线并通过独立临时应用原生验收：仅保存不重启，重部署后 Env/Cmd/CPU/内存/挂载一致，旧指纹拒绝，票据执行消费一次，任务/审计测试标记均 0。卸载保留配置与 bind 数据，验收自身保留目录及命名卷已精确清理。Release 326 项 UnitTests + 62 项 IntegrationTests；43 项前端单元测试，67 项完整浏览器回归及最终细调后 12 项配置专项通过。详见 `docs/qa/APPLICATION_CONFIGURATION.md`。

- 工作站仓库：`/data/GitRepos/whale-deck`，分支 `dev`，已快进到当前远端提交。
- `whaledeck-agent` 与 `whaledeck-maintenance` 由 systemd 管理并开机自启；Agent 仅监听 `/run/whaledeck/agent.sock`，不监听 TCP。
- `whaledeck-api`、`whaledeck-worker`、`whaledeck-gateway` 已完成强制重建与切换，均健康。Host Agent/MaintenanceHost 已安装并运行，数据库 migration 已执行。Gateway 只向宿主机回环发布 `18080/18081`，稳定入口由 MaintenanceHost 在 `0.0.0.0:8080/8081` 提供。
- 8 个 `database-platform-*` 依赖容器健康：PostgreSQL、MariaDB、SQL Server、MongoDB、Valkey 9、Valkey 7.2、Authentik Server 和 Authentik Worker。
- 数据库、缓存、Authentik、API 与 Worker 都没有直接向局域网发布容器端口。API/Worker 不挂载 `docker.sock`，所有宿主机与 Docker 操作均通过 Agent。
- Docker 使用有界 `local` 日志驱动（单文件 10 MiB、最多 5 个、压缩）并启用 `live-restore`。
- `database-platform-workstation-update.timer` 已启用，计划每周日 20:00 执行。
- 维护入口按当前启用的非容器 IPv4 网卡动态验收，支持板载、USB 等接口；回环可访问，Docker bridge 接口明确返回 403。

当前可从任意可达工作站网卡访问 `http://<工作站地址>:8080`。`precision-7920-tower.local` 的无端口入口尚未切换到 80 端口，不应在完成网络入口调整前写成已上线地址。

## 已实现并通过真实依赖验收

- Authentik OIDC Code + PKCE、HttpOnly Cookie、CSRF/Origin 防护、管理员稳定组 ID 映射、两小时权限快照和失败关闭。
- Authentik 用户创建/更新/禁用，以及批准类型的 OIDC 应用创建/更新/清理。
- 门户、普通用户/管理员字段裁剪、个人入口配置、概览与 Agent 独立 6 秒指标采集；实时 6 分钟滚动、1 小时/24 小时/7 天历史，物理磁盘读写速率、IOPS 与完成 I/O 加权延迟。
- 容器清单、创建/复制、受保护资源策略、启停/重启/删除/重建计划、受保护容器批量操作、镜像拉取/更新/清理计划、网络/卷/镜像/systemd 资源页、有界日志、单次统计、统一 6 秒 CPU/内存采样缓存与历史入库、事件时间线、更新历史和脱敏诊断包。
- Docker 配置读取、原生校验、原子备份、受控重启、健康等待与失败回滚。
- PostgreSQL、MariaDB、SQL Server、MongoDB、Valkey 9/7.2 的数据库或账号生命周期、授权、凭据轮换及连接管理适配器。
- 六类数据库备份、校验、记录、策略与保留清理；恢复入口未注册。
- 应用模板预检、安装、启停、更新、重装、卸载和原子配置目录。
- Job/Outbox、资源租约、重试、取消、Agent 操作恢复、幂等键、SSE 进度与 `Last-Event-ID` 续传。
- 主机信息、网卡、存储/SMART、journald、更新检查/安装入口、重启入口和受限诊断包。
- 主机进程列表/详情、CPU/内存排序、名称/PID 过滤与分页、每 6 秒采样；2026-10-12 在最终修正版真实工作站读取 655 个进程，核对真实 Agent PID、启动身份、CPU/内存排序和采样推进，匿名 401、普通用户 403，临时用户已清理。无结束进程能力。
- 告警、审计、设置、计划任务、配置仓库快照/敏感扫描/固定参数提交推送、平台自检与更新计划。
- Authentik 稳定组 ID 到本地角色的动态映射、管理权限即时复核，以及按权限裁剪的全局搜索。
- Vue 前端已容器化，包含登录/加载流程、主题切换、响应式导航、仪表盘、个人门户及全部当前管理页面；管理员菜单、搜索结果和危险操作按权限显示。

真实工作站验收脚本覆盖：OIDC、概览、六类数据库、六类备份、应用生命周期、Docker 设置回滚、治理/告警/门户、Authentik 用户与 SSO、主机资源、容器日志/统计、诊断包、Job 幂等和 SSE 续传。临时测试资源均使用独立名称并在脚本结束时清理。

## 自动化验证基线与先前阶段记录

- `.NET 10` Release 构建：0 警告、0 错误。
- 自动化测试：307 项 UnitTests、62 项 IntegrationTests 通过；新增应用参数覆盖、UTF-8 文件边界、环境字面值与预检不消费/不续期票据测试，既有容器关联原生字段投影、数据上限与旧 Agent 兼容检查继续通过。既有迁移、容器、磁盘、历史窗口、Compose、保护、认证、权限与调试文档测试继续通过。当前名为 IntegrationTests 的项目主要是协议/纯方法测试，不连接 PostgreSQL；真实 SQL 行为由隔离工作站库演练和运行时验收另行证明，不能用测试项目名字代替真实依赖证据。
- 前端：Prettier、Oxlint、ESLint、Vue TypeScript 检查、43 项 Vitest 和 Vite 生产构建通过；新增应用响应格式检查、关联入口名称到实际安装 ID 的解析、详情迟到响应隔离、返回键与手机详情布局。既有统计、轮询、历史槽数绘图、缺失断线、跨磁盘延迟、Compose 与应用保护测试继续通过。
- 2026-10-13 最终完整浏览器回归为 55 项，全部通过；新增容器关联跨资源跳转、资源失效、普通项目只读与注册应用入口，包含 320–1920px 和明暗主题。真实工作站另验 11 个容器的 19 个网络端点、24 个挂载及 11 条端口信息，与 Docker 原生字段一致；匿名 401、普通用户 403。详见 `docs/qa/CONTAINER_RELATIONSHIPS.md`。
- 页面水平溢出修复：26 项浏览器回归覆盖 320–1920px、1045px 断点、明暗主题、11 个有数据的管理页面切换及空/错误状态。应用卡片/搜索/关联容器、容器卡片/列表切换、长事件全文弹层均纳入回归；表格只在内部横向滚动，桌面操作列固定右侧。手机详情单列，并单独约束详情标签、顶部操作按钮和弹层的可见边界；数值宽度检查不能替代截图走查。
- Agent 协议仍为 `1.11`；`docker.relationships`、`docker.usage`、`host.disk-io`、`docker.container-summary` 和只读进程能力继续保留。当前运行时源码为 `bcadde9`，已完成工作站构建、Host 服务安装、API/Worker/Gateway 切换；`359a19d` 只增强验收脚本，无运行时修改。11 个核心容器健康，部署单元最终 `inactive/dead`、`Result=success`、`ExecMainStatus=0`。此前 Socket 为 `997:975 / 0660`，普通 user 全量 sudo 仍失效，当前部署未扩大权限。此前日分区运行时为 `18f1531`。
- 应用配置阶段先修复明文 `environment` 进入持久化任务的入口；真实 Authentik Cookie/CSRF 下两种大小写均在入队前拒绝，原生 PostgreSQL 任务/审计标记计数为 0，最近两分钟最多 1000 行的 API/Worker 日志扫描干净。真实 Valkey 的大票据消费一次、再次 404、超长拒绝通过。原生 Compose 对批准的双引号编码验证 10 类字面值，30 个隔离容器仅创建未启动，无端口/挂载，已精确清理；单引号/混合编码作为对照不作为生产实现。完整参数编辑仍未开放，详见 `docs/qa/APPLICATION_CONFIGURATION.md`。
- 历史监控新增 7 项浏览器回归，全套共 33 项通过：320/1045/1440px 明暗主题、时间/设备/指标完整切换、6 秒连续刷新、迟到响应、503 和 403。独立截图检查了工具栏、统计、曲线和相对时间轴，未发现溢出/截断。真实依赖验收返回 30 条网络/物理磁盘序列，四窗口边界与空缺保留正确；最终 64 个已结束时间桶独立对照 PostgreSQL 原生聚合一致，11 条仅有当前桶的新指标检查边界/单位/质量而不进行跨快照数值比较。Worker 可以在 API 响应后补写此前采集的批次，当前桶均值随写入变化属于正常行为。新延迟的历史不足部分保留为空，不伪造部署前数据。
- 容器资源监控阶段新增 10 项浏览器回归，当前全套 43 项通过。卡片/列表、CPU 超过 100%、预热/停止状态、刷新完整切换、慢请求/隐藏/离开取消、503/403 均覆盖；独立 headed 截图修正了原生滚动条下 320px 顶栏裁切与移动卡片标题压缩。真实验收使用隔离、无端口/挂载、0.25 核/32 MiB 临时容器，对照原生 CPU 和内存、确认指标入库、停止空值及启动恢复，最终用规范计划参数停止并删除。匿名 401、普通用户 403，测试用户由包装脚本清理。首次脚本清理参数错误触发计划拒绝，遗留容器已精确核验并单独清理，未删除任何数据卷；修正版完整通过。
- 容器采样阶段真实回归：11 个容器、8 个 Compose 应用，网络/保护一致；历史监控 30 条序列及 75 个已结束桶与 PostgreSQL 聚合一致；OIDC/Origin/CSRF、完整 Authentik 注销与 `/login` 通过。该轮验收脚本提交为 `3423b23`，与当时运行时 `8679e78` 业务源码相同。
- 2026-10-12 指标日分区阶段：迁移前 PostgreSQL 备份和 SHA256 验证通过；原始表约 287 万行/614 MB，聚合表约 28 万行/55 MB。隔离库以应用拥有者和只读角色运行完整 EF SQL 链，覆盖空库、数据升级/降级、精确保留边界、UTC/闰日、真实分区删除、重复执行、并发锁跳过、长停机、无关/伪造分区拒绝及权限保留；测试库和角色已清理。生产两个父表各有 17 个真实 UTC 日分区，拥有者仍为 whaledeck；不可变窗口计数/摘要与迁移前一致，迁移事务内另做全量逐行 EXCEPT ALL 对照。时间 BRIN 父索引及 17 个继承索引有效，时间查询计划只访问所需日分区。
- 分区版运行时回归：MetricsRollup 与 RetentionCleanup 经真实 API → Worker 执行，Job/执行记录均完成且起止时间有效，测试计划已删除；修复了内部任务成功但执行记录停留 Queued 的缺口。四个历史窗口、30 条序列、75 个已结束桶与 SQL 一致；OIDC/Origin/CSRF/完整注销通过。最近五分钟 Worker 循环错误为 0，没有新增逐样本日志。
- 工作站健康入口与 systemd 服务已复验；本轮真实主机回归返回 33 个网络接口、8 条存储记录、20 条系统日志及有效诊断包；OIDC、Origin、CSRF 与容器管理员查询回归通过。

## 安全与恢复入口

- 生产调试文档入口已统一返回匿名 401、普通用户 403 的 Problem Details，含稳定错误码与追踪号，不触发 OIDC 重定向；管理员 OpenAPI/Swagger UI 真实验收通过。进程名称/内存取数已改为固定 `/proc/<pid>/status` 字段，避免框架补全进程名时读取 cmdline；IO 修正已部署并通过工作站复验。
- 2026-10-12 登录/退出与监控修复：浏览器 CSRF POST 顶层导航完整注销 Authentik，最终 `/login`，两侧会话分别返回 401/403；ID token 仅存 Valkey 加密、带 TTL 的服务器票据，浏览器 Cookie 仅含随机引用。匿名 API 在挑战选择阶段直接返回 401，不再由 OIDC 处理器生成 nonce/correlation Cookie。
- Dashboard 两个入口图标上传至 Authentik 媒体，内网绿色网络角标、外网侧局域网蓝色地球角标；入口直接启动 OIDC 登录而非再显示工作台登录页。回调前准备端点按实际 Cookie 路径清理遗留登录数据，网关/维护入口/ API 请求头和代理响应缓冲仍保留有界限制。浏览器模拟 24 枚约 14 KB 遗留 Cookie 后，Dashboard 成功进入主页；匿名连续 3 次请求无 `Set-Cookie`、Cookie 增长为 0。
- 实时监控改为 6 秒无蒙层刷新、固定 6 分钟 61 槽、相对时间轴与缺失断线；首次填充后台历史，后续按时间合并，权限变化清空。历史查询按 `LastSeenAtUtc` 排除旧接口并优先活跃序列，用一条相关投影 SQL 替代 N+1 查询；工作站返回 24 条活跃序列、6 个真实网卡收发方向。浏览器确认连续刷新不清空曲线，390/1440px 明暗主题无重叠或水平滚动，单磁盘筛选正常。
- 2026-10-12 修复 Authentik 已配置管理员/OIDC 但租户 `setup` 标记缺失导致 `/setup` 500 的问题。安装阶段仅在已有可用管理员密码、两个 Whale Deck OIDC 应用和登录流程时补齐官方初始化收尾；不会重置密码。脚本默认只读，显式启用修复，重复运行无变更。
- 2026-10-12 应用与容器页面调整：受保护/未注册 Compose 应用只读；普通受管应用按运行状态显示操作，安装时间、自动更新和版本仅来自有效安装记录，不伪造基础容器安装信息。容器事件动作单行省略，悬停原生提示、点击全文弹层，手机抽屉采用单列键值布局。部署单元最终 `inactive/dead`、`Result=success`，11 个核心容器健康；本轮未修改数据库数据目录，临时验收用户由包装脚本清理。
- Agent capability 最长 60 秒，绑定调用方法并使用一次性 nonce；UDS 同时校验 Unix peer credentials。
- 普通管理操作不能停止或删除 Whale Deck/`database-platform` 受保护资源；删除 Docker 标签不能解除宿主机注册表保护。
- 一次性 Secret 使用 Valkey `GETDEL`，按 Authentik subject 隔离并设置 5 分钟 TTL，不进入 PostgreSQL、URL、审计或日志。
- 迁移安全备份保留在 `/data/DockerData/migration-dcfs-20261009_163559`。最终验收后已删除旧 `dcfs*` 容器对象、空旧网络和 `/data/GitRepos/DCFS` 兼容链接，未删除任何 NVMe/HDD 数据或迁移备份。
- 临时 `/etc/sudoers.d/whaledeck-temporary-user` 已删除，完整 sudoers 已通过 `visudo`；普通 `user` 的非交互式全量 sudo 已失效，长期只保留 `whaledeck-agent` 固定 helper 的最小权限。

## 尚未收尾

以下为早期阶段的待办快照，已被后续记录或范围修订覆盖时不再作为当前缺口；当前待办以目标主表和文首恢复点为准。

1. 任务重试 API、前端确认与预检流程已发布；2026-10-12 临时容器真实验收通过：失败源任务保留，新任务使用当前资源预检，二次确认及幂等成立。治理、门户、告警与身份回归，以及 Docker 配置原值应用后的受控重启已在同日通过。
2. 最终发布前使用正式账号再复核危险操作确认；匿名登录、Authentik 跳转、明暗主题、1045px 导航断点、600px 搜索图标、390px 手机布局和普通用户菜单裁剪已完成浏览器走查。
3. 决定并实施局域网正式入口：将 Whale Deck 切换到 `http://precision-7920-tower.local` 的 80 端口前，先确认与现有服务无冲突；其他服务继续按端口区分。
4. 当前使用 `dev` 开发、测试、修复后覆盖发布流程；部署问题修复后覆盖，不自动回退开发版本。稳定发布和合入 `master` 属于后续发布节点。

## 功能细项缺口

以下为早期源码核对快照，包含后来已实现或已移出的能力；不是当前交付清单。恢复/文件/终端/网络/通知/GitLab仍按59项实施，多节点/企业租户/异地Provider平台不恢复。

以下是对源码与页面的核对结果，仍需逐项实现和验收，不能用上方模块级描述替代：

- 应用手动/策略归档、应用页面入口与历史、只读校验、保留清理已上线并真实验证；`a9deae8` 增加每策略容量预警完整状态机、实际 HDD 采样及隔离 PostgreSQL 落库验证。32 项 Linux 隔离检查补充共享/核心/损坏/预算/部分清理恢复等边界，但不能代替扩大真实失败验收。下一步为强杀/断电与真实共享/损坏/失效计划；不提供恢复/下载，详见 `docs/qa/APPLICATION_BACKUPS.md` 与 `docs/qa/BACKUP_CAPACITY.md`。

- Compose 已完成受控注册项目的启动、停止、重启、拉取、重部署，以及原生配置哈希的服务级差异摘要；完整字段级配置差异、配置编辑仍未完成。独立临时应用通过真实 API → Worker → Agent 生命周期和单次计划验收，并在结束时清理。
- 应用管理已提供紧凑卡片、状态相关操作、搜索排序、关联容器和受保护项目只读展示；热门目录仍为 Top 6，不等同完整应用市场。普通受管应用参数覆盖和专用备份入口已接入 RPC/API/Worker 与前端表单，参数原生验证保存/重部署与敏感值不进入任务/审计，备份范围明确排除可写层/tmpfs/外部服务。高级安装模板和更广泛组合验收仍需补齐，不能把现有按钮数量当作与 1Panel 功能齐平。
- 容器卡片/列表、状态/Compose 项目/关键字筛选、真实端口/IP、持续 CPU/内存与缓存统计、6/30/60 秒或手动刷新策略已实现。本轮已部署 image ID、网络端点、mount 类型/路径/读写、端口映射与 Compose 关联分组，以及资源页精确筛选、反向关联容器和缺失资源提示；11 个容器的真实关联与权限验收记录见 `docs/qa/CONTAINER_RELATIONSHIPS.md`。窄屏列表内部横滚，默认卡片布局不撑宽页面。
- 实时/1 小时/24 小时/7 天历史选择、IOPS 与磁盘延迟已实现。UTC 原始/聚合指标日分区、保留 14 天及整日分区清理已落地，只有跨截止线的唯一边界日做索引局部删除，以保持精确滚动保留。分钟 rollup 有界 upsert 允许迟到修正，不再 N+1 写入；数据模型中规划的持久化 1 小时 rollup 与 first/last 字段仍未实现，不能用历史图表查询替代这两个存储细项。
- 路由、监听端口、防火墙只读视图，大目录扫描与清理建议。
- 镜像更新检查、构建缓存详情、卷容量与依赖图的完整展示。
- 异地备份复制与状态；数据库配置差异；公共门户引用/隐藏和完整可用性探测。
- 通知已读/未读和历史；会话续期提示、最近登录与安全事件展示。
- 所有管理页面的操作、空/错误/过期状态及普通用户拒绝访问，需完成最终浏览器回归。

P2 的镜像安全扫描/SBOM、Compose Git 同步、数据库专用工具入口、受控结束进程、反向代理路由和多 Agent 尚未形成完整实现。macvlan/ipvlan 与 Web Shell 在功能基线中仍明确为设计预留；不因 P2 标签而解除其预留状态。最终交付须以已确认的非预留范围逐项验收。

主机重启、系统更新安装和平台自身回滚属于高风险入口，代码与计划流程已提供，但不会为了验收而无故执行。GitLab、数据库恢复、容器终端、文件管理、防火墙/网络写入、外部通知、多主机、域名/HTTPS 仍为 `HOLD` 或 P2，不属于本阶段缺口。
