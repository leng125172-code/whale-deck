# 文件历史、回收站与恢复

2026-10-17，最新运行时7ece209/Agent1.26已覆盖发布，普通文件历史/回收/恢复/单HDD版本清理已通过真实身份、后台及网页验证；小屏长目录指针遮挡修复另有源码，尚待发布。下方基础阶段“尚未注册/未部署”为历史，最终证据见文末。目录/归档、容器文件、隔离及长期清理和完整P0–P5的76项继续，不签完整文件管理。

## 交付结构与基础阶段状态（历史）

- 独立FileArchive契约、Application用例、永久意图/Job/Outbox/共享路径锁/人类审计、Agent/root执行与WebUI；不修改旧上传/变更的签名JSON结构，不让历史永久任务失去校验能力。
- SQL关系化`file_versions`及`file_archive_operations`：版本来源操作、根/相对路径、旧源版本、字节/完整SHA、捕获原因/生命周期/可用性、创建与观测时间；操作存人类主体/登录绑定、不可逆owner、HMAC意图、类型化计划、期限、租约、阶段、结果和版本。无文件正文、浏览器Token或私有宿主路径；每次schema变更配EF migration。
- 根执行模块已新增，尚未注册helper枚举、Agent能力/服务或HTTP写入口，未部署。后续还须接真实依赖和页面，不能据源码存在或Linux测试签成回收站完成。

## 根执行规则

1. `history`列表只接受登记根及可选相对路径；50返回/500扫描/48KiB转义JSON条目预算，稳定UUID游标和目录版本，显式complete/invalidRecords，不漏续页。原1.25旧元数据默认为Overwrite/Available，无需伪造或改写旧回执。Present只表示当前元数据/类型/权限/大小一致，不等于每次列表重读全部SHA；恢复和清理计划必须独立完整校验。
2. `trash`仅当前普通单链接文件；预检盘身份/容量、源版本/完整SHA及父目录身份。先完成HDD完整旧版本/fsync/私有证明，再NOREPLACE把原inode移入其私有目录，保留两份；不对用户名称直接unlink。最终确认后才将捕获状态从Pending改Trashed。
3. `restore`绑定原版本UUID、原根/路径、元数据指纹、完整旧字节/原普通模式与owner/mtime、当前目标版本或ABSENT以及父目录。先完整私有暂存与SHA校验；覆盖目标先共用1.25完整HDD备份，再EXCHANGE保留当前对象；不存在用NOREPLACE。剥离SUID/SGID，输入不能自选UID/任意源路径。恢复回收站版本成功后标记Restored，但不删除其历史字节。
4. `purge`独立管理员计划/确认，绑定一个完整可校验版本；只先私有隔离该版本的数据对象，核对原inode/完整SHA，再删除数据与元数据。永久操作回执/人类审计保留；重放只读结果。中途不确定留Unknown和隔离对象，不在恢复查询中补做未完成删除。
5. 新增显式`files-history`、`files-archive`目录授权，仍要求相应list/download/upload/mutate；不把已允许copy/chmod的自定义根悄悄增加删除/恢复权限。默认根升级另做严格旧默认匹配；目前生产默认未增加新权限。
6. 状态机与上传/旧变更共用非阻塞namespace锁。每次应用永久绑定UUID/owner/计划指纹，取消先写持久Aborted栅栏；即使原路径已消失或根权限已收紧，也不得让迟到Apply产生副作用。源/目标和版本ID在SQL再共用逻辑锁；长复制续租/撤权/取消及精确invocation停止必须接入后才能开放BFF写入口。
7. 源并发变化、父目录替换、计划过期、缺盘/只读/容量、损坏数据/非法元数据/链接/保护路径均拒绝。SIGKILL后的恢复只根据保存的身份、阶段、私有对象和完整摘要判断；不重新执行用户文件rename、发布或unlink。Unknown保留数据，不能伪装Canceled或Succeeded。

## 继续实现，不作为缩减

当前模块只处理普通文件。目录回收/恢复、目录复制移动/归档需完整子树保护/清单；损坏或缺失副本的受控清理、Unknown人工处置、保留策略/调度/容量和永久回执长期清理、所有SQL事务/权限/故障/实机/UI必须继续。不能用当前完整文件SHA路径为损坏副本/目录恢复背书。

## 开发证据

- 首轮独立Linux17项中16通过；purge强杀测试把整个os.unlink都拦截，误阻止安全的回执临时文件清理。已将故障注入严格限定为本次数据/元数据/隔离负载名称，保留“不得补做删除”的原断言。修正后117项通过；新增策略收紧后取消、元数据类型/状态一致性及HDD已fsync但根回执未保存proof的真实SIGKILL用例，最终120项通过（原100+新20）。这是独立Linux目录夹具，设备身份由测试显式合成，不冒称真实物理HDD/安装态/API/UI证据；未执行生产删除或改默认注册表。
- 独立签名域、严格类型/来源/摘要/大小/状态和永久关系字段绑定测试已加入；`FileHistoryAndRecycle`增量migration含不可变事实及来源人类Job匹配触发器。迁移生成器的命名空间/重复数组分析器失败及模型测试缺少metadata using均记录并修正；完整Release/本机与真实PG结果待后续记录，不把源码生成当作通过。
- 最终本机Release测试：822 Unit、116 Integration通过，6项依赖原生PostgreSQL在Windows明确跳过；新独立模型检查证明迁移只有新增表、外键/索引/约束与guard trigger，没有生产数据删除，snapshot与模型一致。新增23项回收/历史Linux用例与原100项合计123项通过，覆盖外部源修改/父目录替换和purge数据/元数据unlink后的真实SIGKILL截断。CI installer已接入新套件。真实PostgreSQL新增断言已编写（不可变事实、真实Job主体匹配、绑定后禁止改人、状态约束、恢复版本FK保留），尚待冻结源码执行，不冒称通过。
- `78b1b10`首轮工作站隔离PG及CI39005871272/native-postgres同在新测试61行失败：原生PostgreSQL18.6的显式RESTRICT删除返回23001，不是测试误写的通用外键23503。数据库已正确拒绝删除，并非保护失效；更正为精确RestrictViolation、同时要求确切FK名称与原行仍存在，不放宽断言或修改数据库保护。工作站失败隔离库/容器已确认清理，保留报告。修正后需重新执行。
- `validate_file_archive_engine.py`真实物理HDD执行通过：2,097,167字节二进制原文，源与HDD实际不同设备；生产Python模块在独立root-only目录及瞬态systemd沙箱中执行，实际udev/UUID/sysfs挂载身份核对、完整校验、回收/原mode-owner-mtime恢复、覆盖先备份当前原文、两条历史、只清所选HDD版本及重放、只改临时策略UUID后拒绝均通过。探针明确不覆盖已安装Agent/API/Worker/UI，不称为产品入口已开放。私有本地保留对象与HDD副本均在精确白名单/目录inode/无链接/原文摘要检查后清理，`ownedPhysicalArchiveFixturesRemoved=true`；生产注册表、策略及业务文件未修改。
- 实盘模块字节SHA256：files `1059130508e28764cf3d1d7f668158e9d3dcc0ff0508f52367b627c57c2d788e`；backups `a30a3c7b220fed969034391d4270992c2bc85493d1d418ae61117addecfa601b`；archives `a953a4265cfc55570fba1ae55ef50cfc19705a33d89efb5ad2ee5dad7f21f751`。实盘清理仅表示探针对象清理，不代表产品长期隔离对象清理策略已实现；HDD版本purge不会顺带删除仍被永久操作回执引用的本地保留对象，后续有界清理必须单独接入。
- GLM glm-5.3-flash只读定位四个实体/任务配置文件，实际Controller为FileMutationsController而非请求中不存在的FileOperationsController；GPT复核。1次成功/0重试，input81234/cached57024/output1642 Tokens，64662ms，费用未返回。安全与架构、实现、权限和实际操作由GPT负责。
- 后续GLM glm-5.3-flash只读整理四个现有测试文件的夹具/清理/入口；GPT实际阅读复核后编写测试。1次成功/0重试，input287502/cached243648/output4189 Tokens，159553ms，费用未返回。其过滤命令是建议，原生实际执行与清理由冻结源码验证器负责，不因只读分析计作测试通过。

## 基础源码签收（241e5bc，尚未部署）

`78b1b10`独立执行引擎/契约/模型与`241e5bc`精确RESTRICT断言/实盘探针已推送dev。修正后822 Unit、116 Integration通过，6项原生在Windows明确跳过；123真实Linux用例通过。同源码工作站冻结PostgreSQL验证实际执行1项通过（非超级角色、旧schema升级、不可变事实/Job主体绑定、状态及FK保护），报告`dispatch-ff22e835fd174749acbc76ebc2507448.json`为Passed、executed=passed=1、cleanupConfirmed=true；这是隔离库，不给生产应用此migration。首轮失败报告`dispatch-b8e1144424714dddb9391b90dd560057.json`保留，不抹去或作为成功。

从工作站已提交的241e5bc再次执行实盘探针，三个生产Python模块字节SHA与上面完全一致，原文/身份/元数据/回收/恢复覆盖/历史/精确HDD版本purge及错误UUID拒绝再次通过，两个专用root-only目录已清理。独立Linux代码副本也在确切目录身份、所有权、无链接/文件类型白名单与无活跃Python验证进程检查后清理；永久原生状态报告保留。

CI39006685063已最终completed/success，安装、后端、原生PostgreSQL及前端四作业全部成功。宿主已安装运行时与Pkcs10.0.13、稳定动态入口、systemd/权限/受限helper/UDS及11核心healthy再次复核，新增archive helper确认不存在。线上仍为931a24c/Agent1.25，没有增加helper枚举/Agent服务能力/HTTP写入口或默认目录权限；不据此签收产品回收站、完整文件管理或全部76项。下一步接独立Agent RPC/固定helper、BFF持久任务/共享锁/Worker/人类审计及页面，再继续目录、容器文件、Unknown和长期清理等完整范围。

## Agent/BFF/任务工作流源码推进（尚未部署）

新增Agent1.26专用List/Plan/Apply/Inspect/Discard、方法独占能力验证、固定helper枚举和受信任模块加载；类型化UInt64/纳秒/完整摘要/备份证明与有界历史消息逐层验证。注册表只对精确原始或1.25默认整体升级，新备份名不与旧pre-mutations冲突；任意自定义/收紧策略保持原样。生产尚未安装新helper或改变默认权限。

新增FileArchiveService/Repository/Dispatcher、只含传输映射的管理员HTTP接口与Worker。计划短时、意图永久签名/原登录绑定，Job/Outbox/同一41307341事务门/跨上传变更共享锁、合并预算和人类审计闭环；元数据导入不伪造旧来源，只有匹配已验证操作HDD证明才绑定创建者。已Purged目录行不会被旧列表重新激活；SQL事实不可变。Apply及长读取/取消RPC均续租，写入每6秒复核取消/意图期限/相同主体/有效两小时权限快照，10秒单次guard与10分钟RPC预算，停止确切helper调用；历史结果只读，不因失联重做。通用Outbox消费者排除专用消息。

本机增量曾遇新异常/同名契约类型及权限到期属性引用错误，均按实际类型修正；既有5ms真实定时器测试偶发续租4而非3，改为显式手动tick且保持恰好三次原断言，没有修改生产续租策略。已通过127真实Linux文件用例（含新增模块加载/元数据EOF及旧默认升级），新Agent策略/能力/长任务撤销用例已加入。原生SQL新增工作流断言已编写，使用显式传输stub只证明SQL/Application，**尚未执行新原生工作流，不算实际OIDC/API/UDS/安装态或UI通过**。

本轮GLM glm-5.3-flash只读整理三个既有策略/升级测试文件，GPT独立读取复核。1次成功/0重试，input42476/cached14144/output1424 Tokens，55354ms，费用未返回；权限、架构及实现由GPT负责。来源定位不作为测试证据。

`34f7114`上述Agent/BFF/Job源码已通过CI39010008595四作业，856 Unit/116 Integration及127 Linux通过；工作站冻结原生报告`dispatch-35deeb27c49540b098b607b54df53d06.json`实际执行通过并确认清理，新增真实SQL事务/索引/共享锁/身份绑定/取消/结果恢复/目录不复活断言已执行。传输为明确stub，仍不是已安装Agent或真实OIDC/BFF/UI证据，线上保持931a24c/1.25。

## 页面刷新恢复的服务端计划引用（8da9f90源码已原生验收，尚未部署）

按`playwright-interactive`交互清单发现只在前端内存保留登记请求不足以覆盖刷新/崩溃，因此新增五分钟`file_archive_registrations`引用：页面只需保存非秘密计划ID于路由，签名token及原幂等键由原登录重新获取；Issued不代表确认或执行，已登记只返回原Job。计划与幂等键绑定、与永久意图使用不同HMAC域；到期和任务缺失在同一登记事务门内采样，拒绝迟到提交，不把两个HTTP读取的竞态当作“未登记”。取消未确认计划写单向栅栏，已登记则返回原任务而非悄悄取消。基础事实不可修改，取消时间一旦写入不能清除/改变。

新增迁移保留旧archive任务并回填只读关联记录，再加入RESTRICT；旧意图seal不能作为重发计划的权限。有记录时拒绝向下迁移，不执行开发自动回滚。未登记计划最多128活跃/每主体16、总未登记5万硬上限；过期超过1天的每轮最多1000条有界清理，已登记引用受FK保护不清。离线模型检查及856 Unit/117 Integration通过，6原生本机明确跳过。8da9f90冻结工作站原生报告`dispatch-cfebc6184ee349fe97778b233ca3801b.json`实际executed=passed=1、cleanupConfirmed=true；跨DbContext刷新恢复、异登录拒绝、取消栅栏、到期提交、旧schema回填和有界清理已执行。CI39011917539安装/后端/原生PostgreSQL/前端四作业completed/success。本节仍只证明实际SQL与应用工作流，Agent传输为明确stub，不签安装态或真实身份网页。

## WebUI接入与实际链路验收准备（新增源码，尚未部署）

- 文件页增加根目录/所选文件历史、刷新/续页、空/错误/无效元数据、只读目录边界；普通用户不加载新文件界面或请求。普通文件更多菜单新增回收；恢复区别目标不存在/覆盖前备份，清理只针对所选HDD副本。Present明确不代表刚做过全SHA校验，同设备留存不称异盘容灾。
- 确认前计划ID写路由，不写签名/幂等键/正文到URL或浏览器storage。刷新只取原计划或原Job，Issued不自动确认；丢失响应按原seal/key重试或只读找回。计划取消与任务取消分离，Unknown必须人工核实，不能冒充成功。新面板与上传/编辑/文件变更互锁，晚回复/撤权不复活界面。
- 218前端单元、类型/lint/格式/生产构建通过。原文件/变更/搜索/编辑联合71浏览器通过；新增17项历史回收站及7项生产首次授权，共24项在最终构建预览通过，新增17项连续两轮34通过。明确为浏览器协议fixture，不代替真实OIDC/SQL/Worker/UDS或磁盘证明。
- 保留失败：初轮15项因缺少默认Chromium无法启动，改用本机已安装Chrome；模板多语句事件表达式曾构建失败，改成函数。首轮可运行浏览器11通过/4失败，发现Element Plus程序关闭后的迟到update事件会被误判用户取消，导致原任务查询中断或冻结登记被取消；增加dialog可见状态保护后完整重跑通过，没有增加重试/放宽成功断言。只读根和直接丢失响应刷新两项随后补入。
- 持久headed Chrome独立目测桌面及320px明暗历史、恢复/清理计划，正常点击取消往返及主题可见外壳；辅助说明改为regular主题色提高对比。320×760实际dialog x12/w296/bottom691、两个主要按钮bottom663，历史正文width/scrollWidth280、文档width320，无横向溢出。截图与更细边界见[UI证据](qa/FILE_ARCHIVE_UI.md)。首次手工主题操作误点隐藏input超时并引发REPL重置，原owned浏览器已随pipe关闭；改点可见core，不用force click。
- 新增`validate_file_archives.py`，准备真实身份→API/SQL/Worker/已安装Agent/HDD的取消栅栏、登录绑定、回收/恢复不存在/恢复覆盖前备份/精确purge与人类审计证明，精确清理自身测试文件/副本、保留永久Job/目录/根回执。语法已检查，尚未执行，脚本存在不算实机通过。
- GLM glm-5.3-flash仅只读UI样式草案，GPT审阅，实际状态/权限/安全/实现/验证由GPT负责。1次成功/0重试，input25710/cached11008/output3347 Tokens，100947ms，费用未返回；草案明确占位，未将其按钮当实现或验收。

线上仍931a24c/Agent1.25，未安装1.26新服务、应用新migration或覆盖本次WebUI。下一步同源码门禁与实际发布、真实BFF/HDD及管理员/普通用户网页验收；目录、压缩解压、容器文件、隔离和长期清理及全部76项继续。

## 实际覆盖与已验证子范围：7ece209 / Agent1.26

- 源码7ece209d08aa86d0317d99890f212b0427439f5d的CI39016011071四作业最终completed/success，856 Unit/117 Integration、218前端单元、135 CI联合浏览器及127 Linux通过。工作站冻结原生报告`dispatch-c18a3716597747bf88332f293b86f321.json`实际executed=passed=1、cleanupConfirmed=true，仍明确其Agent传输为stub。
- 发布前活跃上传/变更/路径锁/终端全0；第一次预检错误地逐字节比较JSON排版，未执行调度。严格JSON无重复键/所有权/单链接检查后确认与1.25默认语义全等，才调用既有固定发布helper。没有改自定义策略、sudo范围或网络/KDE。实际unit16:23:47开始、16:25:27 CST Deactivated successfully，inactive/dead/MainPID0/Resultsuccess/ExecMain0。宿主两个在位程序集包含完整7ece209来源且SHA与实际发布包一致；HDD策略/systemd/权限/UDS/helper/动态入口校验通过，宿主运行库和PKCS仍10.0.13。
- 实际Cookie→维护入口/Gateway→API/SQL→Worker→UDS/已安装helper：五分钟原登录计划找回、取消栅栏/迟到提交拒绝、不同登录404、CSRF/Origin/匿名/保护/路径拒绝，普通文件回收、恢复不存在目标及完整uid/gid/mode/mtime、覆盖前另做完整HDD备份、只purge所选版本、原文件与本地私有保留/永久证据保持，真实Job/人类审计/关系目录及共享锁释放通过。`validate_file_archives.py`初轮辅助函数`path`与查询参数重名，在首个读取前TypeError；未执行回收，测试文件已清理。QA-only e37a4fd修正后实际执行通过且cleanup确认；没有为QA脚本重新发布相同应用二进制。
- 真正浏览器无route fixture：全新临时管理员经Authentik用户名/密码/授权进入文件页，桌面确认原42字节文件；刷新Issued只找回同一计划且无POST，随后确认回收，320px深色历史/恢复，独立下载完整42字节SHA相同，再按选定版本清理HDD副本。等待列表刷新确认原文件仍在、该文件历史为空。永久任务依次d018f9fa-b1bb-c69e-083e-84c1aa138f2a、9086a460-1a5a-fe88-701d-e31558c6c2a2、e2c272f1-e2e1-b63c-c779-3805ed091052，实际SQL三终态/三成功审计/锁0、root回执与完整SHA独立复核后，只清本次源文件/私有测试保留；Job/审计/目录/根回执不删。
- 实际共享源与HDD版本在同设备，明确只为版本留存，不冒称异盘容灾。物理不同设备→HDD及强杀故障仍见独立基础证据，不冒称在生产BFF注入缺盘/满盘/断电等全部组合。
- 新建独立普通用户、首次/刷新isAdministrator=false，没有文件界面或文件API请求；历史GET403，持有效CSRF及正确Origin的计划POST403。两个真实浏览器账号均精确删除，独立目录查询剩余0、凭据/私钥内存引用已清，所有浏览器已正常关闭；首次私有SSH子进程exit255，改用现成RSA加密封装通道，不输出明文凭据。Authentik页面语言重绘曾清空密码框，补填后正常，未关闭安全校验。
- 新旧流程原生回归全部通过：33MiB+7/7秒暂停代理上传下载/覆盖/幂等、65543字节目录/普通文件复制移动/权限、人类审计、目录树/递归名称搜索/游标/保护/转义预算、UTF-8/BOM/CRLF/空/边界与完整HDD旧版本；各自精确清理完成。通知8个在位静态资产HTTP/Gateway一致。11核心healthy、宿主服务active、sudo保留，活跃上传/变更/归档/锁/终端全0。
- 本地已测7ece209 FilesView-BoddqrdN.js SHA256 `e55bab01dd1db4f53ce5681b294657a77ca76b83a48375f2374628614953bff1`、FilesView-DR26AcoL.css `bfa5e76a41ba077bcc6fc8b524b56f666bed3227204e4df434ae356c047bb154`，与实际HTTP及Gateway字节三方相同。后续小屏修复是新产物，不能沿用此SHA声明已部署。

### 实机探索发现的小屏长目录点击问题（修复源码，待部署）

320px固定操作列遮住长目录button的中心，真实指针点击被拦截；管理员流程暂以正常键盘Enter继续，不将它算作指针通过。新源码只在600px以下限长目录button命中区，独立文本span真正ellipsis/完整title、图标不压扁，并使归档互锁时目录按钮显示禁用。新增用例先检查elementFromPoint中心确实命中链接，再普通click进入，不用force或删固定列规避。

218单元、类型/lint/格式/生产构建通过，新18归档用例+7首次授权生产预览共25通过。独立headed候选320窗口/305内容区：button x43/w80/right123、中心未遮挡、ellipsis、正常点击及无页面横向溢出已目测。源码尚待提交后CI与覆盖发布及实机再验；截图`archive-long-directory-320-light-candidate.jpg`不冒称线上修复。

未签收：目录回收/复制移动/压缩解压、正文搜索、容器文件、完整租户隔离和长期隔离对象/过期清理；生产BFF故障全组合尚缺。P2-03/P2-04与全部76项保持active。
