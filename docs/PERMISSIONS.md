# WhaleDeck 权限矩阵

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

2026-11-03 ACL内部业务更新：41f02fb已接登录绑定/实时授权、原计划明确确认、结构比较/深复制及原任务Inspect优先恢复，回执独立MAC保护原ACL类型/模式/历史及三态；Unknown不重做、不篡改历史，处置使用原执行ID。4270 Unit/363 Integration（7默认原生跳过）与8范围通过；仍无具体EF仓储/共享保护/Worker/API/Vue或真人业务验收，无发布和默认User扩权，运行沿前批4a58961/1.51。[实现与验收边界](qa/CONTAINER_ACL_WORKFLOW.md)。

2026-11-03 ACL1.51安装态权限已真实签收：4a58961固定helper/独立签名四RPC、13预检及4执行绑定拒绝通过，显式ID0与缺ID/权限分开、core/只读根/错域/过期/重放拒绝，真实初始namespace/准确FD非IDMAP通过，无CHOWN/SYS_ADMIN。9操作及8HDD/原inode/非目标ACL/迟到原签名请求不重写、独立清理通过；仅受信机器调用，不是人类Authentik/BFF/永久意图/页面授权验收，默认User不扩权。两次写前失败及真实入口工厂回归保留，开发user NOPASSWD/密码未改。[实际证据与未验边界](qa/CONTAINER_ACL_AGENT_RPC.md)。

2026-11-03后续ACL协议候选6a62027：独立四RPC/完整message与method签名/短时单次capability，ID与permissions presence严格，SDK必须1.51专属能力。fixed unit无CHOWN/SYS_ADMIN，namespace与准确FD mount映射未证明即拒绝；旧chmod/chown不放宽。当前无ACL HTTP/永久意图或默认User扩权，宿主未安装新helper，真人权限/在途撤权及实际签名UDS仍待。测试夹具与实际只读依赖证明不当作人类授权验收。[候选和边界](qa/CONTAINER_ACL_RPC.md)。

2026-11-03 ACL底层候选记录：ed50dc1单次仅替换显式access或default ACL，named条目须显式mask/数值ID，default限目录，保留另一ACL/原inode与HDD旧属性；fsync失败不误签完成。已有普通chmod/chown遇ACL仍拒绝，不借新模块扩大旧入口。当前未安装，无新增RPC/HTTP/Vue/默认User或Agent授权；候选实盘只为本轮自有资源，真实人类权限与namespace映射仍须接入验收。[候选与实际边界](qa/CONTAINER_ACL_ENGINE.md)。

2026-11-02后续实机：递归所有者原目标消失后保留Unknown/18待证明，宿主实际1项修改单独保留；不同当前管理员真实页面五计数、先暂不处置再唯一确认后才释放保护。另一自有容器原目录任务在保护期间至少四次自然延期、无Root写入，确认后同一ID才发布并在Vue可见。原/新上下文正常双端退出与准确清理通过；Unknown汇总0不表示零修改，不以QA改SQL/时钟/缓存放行。仅该跨容器目录组合签收，其他类型/故障、ACL/删除继续，默认User与Agent授权未扩大。[真实授权与保护证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_UNKNOWN_BARRIER.md)。

2026-11-02后续实机：递归所有者首项实际fchown后，只移除本轮新账号管理员组或停用本轮新账号；原正权限快照仍余7161/7162秒，产品3.951/4.255秒结束原helper、保留原Partial与五计数，不续做。旧任务403，移组me200非管理员/forbidden、停用me403/login；取消POST0，不以撤权或身份删除冒充正常退出。另一位新管理员真实页面五类dt/dd、先暂不处置再唯一确认及其正常双端退出通过；原Subject/Root/HDD不改。旧引用只签当前region/sessionStorage提示重载清理，不泛称其他标签页/路由/持久缓存；其他模块实时撤权仍待，默认User/Agent授权不扩大。[实际身份与页面证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_REVOCATION.md)。

下列各候选/实机记录保留当批“尚待”事实；递归所有者已验分支以本条及专项证据为准，目标消失Unknown/跨写入和其他切点仍待。

2026-11-02后续实机：递归所有者原Worker SIGKILL后，原SQL租约/保护保持至自然到期，仅观察恢复原Partial；固定Agent重启后也不续做。两套当前另一管理员须实际页面先暂不处置再确认，原Root/五计数/历史/主体不改。宿主强杀/重启是本轮预检后QA权限操作，不授予普通面板用户停止受保护核心的能力。其余撤权/Unknown等继续。[进程与授权证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_PROCESS_RESTARTS.md)。

2026-11-02后续实机：递归所有者首项实际fchown后，页面取消及原身份/原实例保留的正常双端退出均通过；退出时原账号/组仍有效且权限快照未到期，取消POST0、旧任务401/引用清空，不以账号删除冒充正常退出。两个新管理员分别经真实页面先取消处置再明确确认，原五计数/历史与主体不改，原执行Failed/Acknowledged。其他实时撤权/故障仍待，不扩普通User权限。[实际证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL.md)。

2026-11-02后续实机：b1fafed管理员真实登录及递归所有者常规6场景、原key/原登录计划与执行事实已签收。退出观察器时序失败保留，另身份纯UI退出补证BFF401/Authentik403，未重复fchown；普通8路403/匿名4路401在原CID移除后验证原永久任务/路由，不能写成原CID存活时的写入实测。默认User不扩权，进行中撤权及同上下文完整退出仍待。[证据与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_REAL_UI.md)。

2026-11-02后续a806f56新增管理员专用递归所有者HTTP与Vue候选：显式UID/GID（0为root）、严格字段/确认/版本/幂等键，Cookie/CSRF/Origin/实时权威身份与原登录授权，原引用查询和跨登录拒绝。部分/未知仅当前管理员明确处置后请求原任务释放，实际计数/历史不可改；新引用随降权/退出清理，不存私有计划或路径。54项新传输/真实回环MVC、77项前端合同与44项生产预览浏览器通过；身份/仓储/Agent为测试夹具，**未部署/未真实Authentik或文件/HDD页面验收，不增加普通User授权**。[候选与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_WEB_UI.md)。

2026-11-02后续候选a19f5c4将原登录/幂等请求、当前身份编排接至真实EF仓储及专用Worker，保留90秒租约、Started后不再Apply、Partial/Unknown需明确处置和原Root事实不可变；新任务与主机锁/七类旧容器写入构成九方双向SQL/路径/预算保护。双隔离真实PG验证不等于人类授权或真实文件验收；**本批没有HTTP/Vue入口、默认User扩权或宿主授权变化，未发布**。[持久化与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_PERSISTENCE.md)。

2026-11-02内部业务候选7a94733已接当前权威管理员与原BFF登录：读写返回前复核，取消前再复核，执行期间每6秒检查授权/取消/租约；其他当前管理员只能经明确确认、原执行ID的永久Discard fence与返回后的再次授权申请释放Partial/Unknown保护。目标UID/GID必须显式（0表示root），不自动沿用原属主或执行身份。可信最终回执的内部归档不依赖操作者仍获授权，不会据此再次Apply或释放Partial/Unknown。**无HTTP/Vue入口或新增默认User权限，未接具体永久仓储/Worker，也未做本批真人权限验收**。[候选与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_WORKFLOW.md)。

2026-11-02后续实机：递归所有者1.50已在22906f5安装，独立方法/内容签名、显式UID/GID（含0:0）及core/只读/缺失/篡改等16项拒绝边界已真实通过，新受限unit仍独立于旧chmod；[安装态证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)。这不是人类Authentik/BFF授权、永久业务共享保护或页面签收；下方候选记录保留，默认User未获得新管理能力。

2026-11-02递归所有者内部授权候选：1.50独立四RPC仅接受完整请求/方法绑定的短时单次capability，UID/GID必须显式提供；新受限unit单独具备CAP_CHOWN，旧递归chmod不扩大。没有新增浏览器授权入口或默认User能力。永久业务接入时仍须Authentik当前管理员/原登录与资源/核心保护、确认及在途撤权；本批仅候选协议/SDK/传输检查，不等于真人权限验收。见[签名与安装候选](qa/CONTAINER_RECURSIVE_OWNERSHIP_RPC.md)。

2026-10-30后续权限设计：`PERM-AI-PROVIDER-EDIT-OWN`须额外显式授予，本人可聊天不等于可配置连接/读密钥；实例连接沿用管理员`PERM-AI-CONFIGURE-SERVICE`，共享只授予使用权。`PERM-PET-ACTION-OWN`同时检查原生成与当前pet/会话/模型包/期限，AI只选择已登记动作表情，不取得URL/任意代码/SDK/宿主能力，不覆盖用户睡眠/隐藏/直接操作。[供应商/Live2D授权及拒绝门槛](AI_PROVIDERS_LIVE2D.md)尚待实现，包含真实匿名/跨用户/撤权/CSRF/Origin/Secret隔离验收。

2026-10-30酒馆/看板娘新增权限**设计待实现**：`PERM-AI-USE`须显式授予；`PERM-AI-READ-OWN/EDIT-OWN/GENERATE/IMPORT-EXPORT`逐资源/模型授权，`PERM-AI-CONFIGURE-SERVICE/EXTENSION-MANAGE/SHARE`独立管理，`PERM-PET-CONFIGURE-OWN/STATE-OWN`和`PERM-AI-BALANCE-READ-OWN`仅本人。完整[权限/资源及工具安全契约](AI_TAVERN_MASCOT.md)与[验收账本](AI_TAVERN_ACCEPTANCE.md)覆盖匿名/普通/管理员/跨用户、CSRF/Origin、退出/两小时复核、Worker发出请求前及在途撤权。管理员不默认读全部聊天/余额，AI不继承操作者宿主管理能力；模型输出/角色卡/扩展脚本不构成运维授权。ST原生密码、登录和资料映射Authentik/BFF，不开第二身份源；下方既有User默认权限不因此自动扩大。

2026-10-21范围覆盖：当前为[单工作站59项](SCOPE_SINGLE_WORKSTATION.md)，不建立企业tenant、节点角色、订阅或邀请体系。保留Authentik管理员/普通用户、本人门户，以及按Subject/稳定组ID授予的开发空间动作；Worker最小授权、二次确认、资源归属和跨用户拒绝不变。下面P5租户描述是旧范围，不作为新权限实施要求；开发期sudo不传播到产品。

正文搜索 **5e131e9/RPC1.29已安装签收**：真实管理员Cookie/CSRF/Origin与实际UDS读取、独立非管理员403、匿名401、缺CSRF400/外源403和保护项403通过；源码双根授权/返回前管理员复核测试保持。320/1440明暗真实网页通过且无管理写请求，见[最终证据及测试修正](qa/FILE_CONTENT_SEARCH.md)。以下“源码候选/未部署”为历史，不据此放宽其他文件功能或宣告全部76项完成。

正文搜索1.29源码候选（尚未部署）：匿名与普通用户拒绝；当前管理员通过BFF读前/返回前两次授权，身份过期或降权不返回片段。`POST /api/v1/files/content-search`是仅查询的POST，需要Cookie、CSRF和同源Origin，不生成写任务/幂等记录或自由命令。根目录必须同时声明`files-list`与`files-download`，不能靠仅列表/仅下载获得正文遍历。前端按登记动作显示正文页签，普通用户不挂载管理页；两类搜索切换/变更根目录/取消时清除迟到私有结果。源码能力不等于实机签收，见[证据](qa/FILE_CONTENT_SEARCH.md)。

范围修订：2026-10-13 [P0–P5 基线](GOAL_P0_P5.md) 解除了旧 HOLD，但没有解除核心资源/默认拒绝策略。旧矩阵继续保护当前实现；新增恢复/文件/网络/终端等动作使用专用权限，P5 团队/租户委派显式授权，普通用户不会自动获得管理员能力。开发期 user 的 NOPASSWD:ALL 属于用户授权的宿主机临时环境，不授予 API/Worker/System 主体全量 sudo，也不取消产品权限校验。其撤销与密码更换由用户手动负责。

本文定义身份来源、权限动作、字段可见性、资源保护和确认等级。Authentik 是身份与用户组唯一事实来源，WhaleDeck 不维护本地密码或重复用户资料。

新增开发空间的 `PERM-DEV-*` 见 [资源级开发权限](DEVELOPMENT_WORKSPACES.md#7-权限与隔离)。管理员可配置资源和委派权限，显式授权的开发者只管理本人/被授权空间；默认 `User` 不自动变为 Developer。本人空间终端不等于宿主终端，访问个人报告也必须验证空间与 run 归属。开发期宿主 `user` 的 sudo 授权不会传播到工作负载。

## 1. 主体类型

| 主体 | 来源 | 能力 |
| --- | --- | --- |
| `Anonymous` | 无有效会话 | 登录、健康检查、Docker 维护加载页 |
| `User` | 已登录且不属于管理员组 | 普通首页、公共门户、本人门户 CRUD、本人会话与退出 |
| `Administrator` | 属于配置的 Authentik 管理员组 | 全部已启用管理功能，但仍受资源保护策略限制 |
| `System` | Worker/计划任务专用主体 | 只能执行任务类型声明的最小能力，不继承管理员全部权限 |

管理员组通过稳定组 ID 或名称配置。部署必须验证该组存在，不能把任意第一个组当作管理员组。

## 2. 权限解析与失效

1. 登录时读取 Authentik Subject、账户状态和用户组。
2. BFF 建立 HttpOnly Cookie，只保存最小会话标识和权限版本，不向浏览器保存 OIDC Token。
3. 权限快照最长缓存 2 小时；到期后重新向 Authentik 校验。
4. 用户被禁用时会话失效；移出管理员组时降为 `User`。
5. 权限下降后拒绝后续管理请求，并取消该用户尚未开始的管理任务；已进入不可取消阶段的任务按安全策略完成或回滚。
6. Authentik 不可用且缓存仍有效时可沿用旧快照；超过 2 小时后默认拒绝管理能力。
7. 每个管理 API、后台任务入队和 Agent 能力签发都必须服务端授权，不能依赖前端菜单隐藏。

2 小时窗口是已确认的可用性/即时撤权折中。需要紧急撤权时，管理员必须同时撤销 WhaleDeck 会话或停止对应用户的会话记录。

## 3. 权限动作

### 3.1 首页、门户与会话

| 权限 ID | User | Administrator | 说明 |
| --- | --- | --- | --- |
| `PERM-SESSION-READ-OWN` | 是 | 是 | 查看本人最小会话信息 |
| `PERM-SESSION-LOGOUT-OWN` | 是 | 是 | 退出本人会话 |
| `PERM-OVERVIEW-PUBLIC` | 是 | 是 | 平台聚合状态，不含敏感详情 |
| `PERM-OVERVIEW-ADMIN` | 否 | 是 | 完整主机、容器、数据库和监控概览 |
| `PERM-PORTAL-READ-OWN` | 是 | 是 | 本人和公共门户 |
| `PERM-PORTAL-WRITE-OWN` | 是 | 是 | 本人门户 CRUD/排序 |
| `PERM-PORTAL-MANAGE-PUBLIC` | 否 | 是 | 发布、修改、撤下公共门户 |

### 3.2 容器、镜像与 Docker

| 权限 ID | User | Administrator | 保护策略 |
| --- | --- | --- | --- |
| `PERM-CONTAINER-READ` | 否 | 是 | 敏感环境变量始终脱敏 |
| `PERM-CONTAINER-CREATE` | 否 | 是 | 禁止高危 runtime 参数 |
| `PERM-CONTAINER-START` | 否 | 是 | 控制面核心容器只允许维护流程 |
| `PERM-CONTAINER-STOP` | 否 | 是 | 二次确认；核心容器拒绝 |
| `PERM-CONTAINER-RESTART` | 否 | 是 | 二次确认；核心容器拒绝 |
| `PERM-CONTAINER-DELETE` | 否 | 是 | 二次确认；核心容器拒绝 |
| `PERM-CONTAINER-LOGS` | 否 | 是 | 行数、时间和大小受限 |
| `PERM-IMAGE-PULL` | 否 | 是 | 只允许批准 Registry/镜像加速 |
| `PERM-IMAGE-PRUNE` | 否 | 是 | 计划哈希、引用检查、二次确认 |
| `PERM-COMPOSE-MANAGE` | 否 | 是 | 批准目录和现有模板 |
| `PERM-DOCKER-SETTINGS` | 否 | 是 | 白名单字段、配置备份和回滚 |
| `PERM-DOCKER-RESTART` | 否 | 是 | 二次确认、Agent 持久任务、维护加载页 |

### 3.3 数据库、账号与备份

| 权限 ID | User | Administrator | 保护策略 |
| --- | --- | --- | --- |
| `PERM-DATABASE-READ` | 否 | 是 | 不返回完整凭据 |
| `PERM-DATABASE-LIFECYCLE` | 否 | 是 | 停止/重启二次确认 |
| `PERM-DATABASE-CREATE` | 否 | 是 | 参数化引擎适配器 |
| `PERM-DATABASE-DELETE` | 否 | 是 | 二次确认；系统数据库拒绝 |
| `PERM-DB-PRINCIPAL-MANAGE` | 否 | 是 | 禁止普通界面授予服务器超级管理员 |
| `PERM-DB-PERMISSION-MANAGE` | 否 | 是 | 仅引擎声明的权限模板 |
| `PERM-DB-CREDENTIAL-ROTATE` | 否 | 是 | Secret 只展示一次，不进审计 |
| `PERM-DB-CONNECTION-TERMINATE` | 否 | 是 | 系统和维护连接拒绝 |
| `PERM-BACKUP-READ` | 否 | 是 | 备份路径仅显示批准目录内相对信息 |
| `PERM-BACKUP-RUN` | 否 | 是 | 资源锁、容量检查、异步任务 |
| `PERM-BACKUP-POLICY-MANAGE` | 否 | 是 | 目标目录来自批准列表 |

数据库恢复已经纳入 P2-08，不再受旧 `HOLD` 排除；专用权限/计划/目标覆盖确认与安全实现完成前仍不得注册可调用恢复入口，不能借通用备份权限绕过恢复边界。

### 3.4 应用、主机与平台

| 权限 ID | User | Administrator | 保护策略 |
| --- | --- | --- | --- |
| `PERM-APPLICATION-READ` | 仅公共入口 | 是 | 普通用户不见安装配置 |
| `PERM-APPLICATION-INSTALL` | 否 | 是 | 现有模板、计划哈希、异步任务 |
| `PERM-APPLICATION-LIFECYCLE` | 否 | 是 | 停止/重启二次确认 |
| `PERM-APPLICATION-UPDATE` | 否 | 是 | 备份、维护窗口、失败回滚 |
| `PERM-APPLICATION-UNINSTALL` | 否 | 是 | 二次确认，明确卷保留策略 |
| `PERM-SERVICE-READ` | 否 | 是 | 普通用户只通过首页门户访问已发布入口 |
| `PERM-HOST-READ` | 否 | 是 | IP、设备、日志仅管理员 |
| `PERM-HOST-PROCESS-READ` | 否 | 是 | 列表/详情只读；不暴露命令行、环境变量或可执行路径；无结束进程动作 |
| `PERM-SYSTEMD-MANAGE` | 否 | 是 | 仅白名单 unit |
| `PERM-SYSTEM-UPDATE` | 否 | 是 | 普通更新，不自动重启 |
| `PERM-HOST-REBOOT` | 否 | 是 | 二次确认、任务排空、无关机能力 |
| `PERM-PLATFORM-DIAGNOSE` | 否 | 是 | 脱敏输出 |
| `PERM-PLATFORM-UPDATE` | 否 | 是 | 核心维护流程和回滚 |
| `PERM-CONFIG-REPOSITORY` | 否 | 是 | 批准仓库/路径、敏感检查、固定 Git 参数 |

### 3.5 任务、计划、告警与身份

| 权限 ID | User | Administrator | 说明 |
| --- | --- | --- | --- |
| `PERM-JOB-READ` | 否 | 是 | 普通用户门户操作不产生可见后台任务 |
| `PERM-JOB-CANCEL` | 否 | 是 | 仅可取消阶段 |
| `PERM-SCHEDULE-MANAGE` | 否 | 是 | 仅预定义任务类型 |
| `PERM-AUDIT-READ` | 否 | 是 | P1 支持导出 |
| `PERM-ALERT-MANAGE` | 否 | 是 | 查询、确认、静默和历史 |
| `PERM-IDENTITY-MANAGE` | 否 | 是 | 通过 Authentik API，不保存本地密码 |
| `PERM-SSO-MANAGE` | 否 | 是 | 管理批准的 Provider/应用 |
| `PERM-SETTINGS-MANAGE` | 否 | 是 | Secret 只显示配置状态 |

## 4. 字段级可见性

| 数据 | User | Administrator | 日志/审计 |
| --- | --- | --- | --- |
| 平台在线/异常/维护状态 | 可见 | 可见 | 可记录状态变化 |
| 资源数量 | 不返回 | 可见 | 聚合记录 |
| CPU/内存/磁盘/网络原始指标 | 不返回 | 可见 | 不记录逐样本日志 |
| 主机 IP、内核、设备和挂载 | 不返回 | 可见 | 错误中脱敏路径 |
| 容器名、镜像、网络、日志 | 不返回 | 可见 | 环境变量值脱敏 |
| 数据库名、账号和连接 | 不返回 | 可见 | 密码永不记录 |
| 个人门户 | 仅本人 | 本人；公共项可管理 | 记录变更，不记录访问历史默认值 |
| Authentik 姓名/邮箱 | 当前用户可见本人 | 按身份页面需要显示 | 不复制成本地资料表 |
| Secret、Token、私钥、产品密钥 | 不返回 | 仅配置状态或一次性交付 | 禁止记录 |

DTO 必须按权限构建，不得先序列化完整对象再让前端隐藏字段。

## 5. 资源保护级别

| 级别 | 示例 | 普通管理 API | 允许流程 |
| --- | --- | --- | --- |
| `ControlPlane` | WhaleDeck Gateway/API/Worker、Agent、宿主机稳定入口 | 禁止停止、删除、重建、普通自动更新 | 平台维护流程 |
| `IdentityCore` | Authentik Server/Worker 及其关键依赖 | 禁止从普通容器页破坏 | 专用应用维护流程 |
| `CriticalData` | PostgreSQL、Valkey、SQL Server、MariaDB、MongoDB 数据实例 | 禁止删除卷/绕过备份；允许数据库页面受控生命周期 | 数据库管理流程 |
| `Managed` | 普通应用和容器 | 按管理员权限操作 | 容器/应用流程 |

保护状态来自宿主机资源注册表和平台标签双重判断。建议标签为 `io.whaledeck.protected=true`，但管理员删除标签不能绕过注册表保护。`autoupdate=true` 不能覆盖保护级别。

## 6. 确认与执行等级

| 等级 | 示例 | UI | 服务端 |
| --- | --- | --- | --- |
| `L0` | 查询 | 无确认 | 权限和字段裁剪 |
| `L1` | 创建门户、启动容器、立即备份 | 明确成功/失败 | 幂等、审计 |
| `L2` | 停止/重启、应用更新、密码轮换 | 二次确认并展示影响 | 资源锁、审计、可回滚信息 |
| `L3` | 删除、Docker 设置、主机重启、平台更新、配置推送 | 二次确认、计划差异和回退方案 | 短时计划哈希、Agent 能力、持久任务、完整审计 |

二次确认不等于多人审批。首期不实现审批流或单独审批角色。

## 7. 历史 `HOLD` 权限门禁（新版已解除）

2026-10-13 之前以上能力不注册处理器；新版按完整目标实现专用权限。新增开发空间按 `PERM-DEV-*` 显式委派，不把普通用户放宽为管理员。仅保留命名空间/设计注释仍不能使 API、菜单或 Agent RPC 可访问，须待对应服务端授权与安全契约实现。

## 8. 消息中心（站内已签收，新快照与外部投递待签收）

| 操作 | 匿名 | 普通用户 | 当前管理员 |
| --- | --- | --- | --- |
| 消息列表/摘要 | 拒绝 | 本人 Subject 消息 | 本人 Subject 消息 + 管理员广播 |
| 单条/快照已读 | 拒绝 | 仅本人可见消息的本人读状态 | 仅本人读状态，不影响其他管理员 |
| 管理资源详情 | 拒绝 | 无管理跳转 | 仍由目标端点再次授权 |
| 任意发送/编辑事实 | 无入口 | 无入口 | 无入口 |
| 渠道/路由元数据及投递记录 | 拒绝 | 拒绝 | `/notifications/manage` 服务层再次校验 |

配置写入必须 CSRF、Origin、UUID 幂等键、二次确认和预期版本；凭据值及其引用不返回客户端。路由变化提升父渠道版本并作明确影响提示；历史投递阻止渠道删除。见 [通知配置契约](qa/NOTIFICATION_MANAGEMENT.md)。这些端点不能任意发送消息。

身份由服务端 Authentik 目录解析，不使用请求体角色。越权或过期 ID 与不存在 ID 统一 404；退出/降权清空前端，失效摘要不显示假零，未知/晚到响应不补回私有数据。详见 [消息中心](NOTIFICATIONS.md)。

## 2026-10-27 容器历史内容恢复权限

历史和恢复五路API仅当前Authentik管理员，服务额外检查原BFF登录存活；普通/匿名拒绝由HTTP集成覆盖，真实正常管理员全链已通过，更多真人拒绝/中断门禁继续。计划与确认绑定原subject/login、当前CID/原始纳秒instance和目标版本、完整来源捕获摘要。恢复专用状态/取消仅原登录；另一登录不可借原计划重放。现任管理员只能走已有任务取消或Unknown接管流程，不伪造原登录。

API/Worker只访问UDS和短时签名，minor42及history/source能力必需；Worker接收前、接收中、Ready前、发布前复核权限/取消/租约。来源必须经正常RPC EOF全验证，浏览器不能提供恢复内容，通用任务重试拒绝。核心/敏感路径/别名与实例保护继续由Agent/Root复核。

2026-10-27：允许同容器其他路径内容恢复，来源读取权限不扩大至其他CID。新目标仍执行原路径/当前实例/核心/敏感/链接与目标版本保护。实际普通OIDC登录对历史、恢复计划/登记/状态/取消均403，匿名历史401；原登录归属和进行中撤权/重启更广组合继续。

## 容器复制权限（2026-10-28 候选，实际身份验收待完成）

| 入口 | 当前身份要求 | 原身份/其他约束 |
| --- | --- | --- |
| 登记预检、按 ID/key 查询、确认、取消 | 当前 Authentik 管理员且登录有效 | 原 subject + login；容器/命令/计划不变；确认必须显式、未过期且版本匹配 |
| 后台扫描/Apply | 原用户仍为管理员，原登录有效 | 每6秒及最终等待后复核，期限与租约有效；发布保护先持久化，再签原执行 |
| 后台 Inspect/Discard 对账 | 可信 Worker 的原绑定 | 只观察/封闭原 ID；原用户撤权不抹去历史结果，也不授予新 Apply |
| Unknown 元数据/接管 | 当前管理员且登录有效，可与原用户不同 | 显式二次确认 + 并发版本 + 原 Root 永久栅栏；保留原 actor/owner/意图及可能存在的私有内容 |

普通用户不能调用上述管理员 API；匿名请求遵循现有 API 401 规则。Cookie 写请求保留全局 CSRF/Origin 及唯一幂等键。
当前已验证业务/模型/SQL，尚未把这些检查声明为本批真实身份网页验收；见 [边界及证据](qa/CONTAINER_COPY_PERSISTENCE.md)。
