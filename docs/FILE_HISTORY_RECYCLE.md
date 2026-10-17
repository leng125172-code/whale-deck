# 文件历史、回收站与恢复

2026-10-17，接续运行时931a24c/Agent1.25的完整HDD覆盖前备份。本文是正在实现的下一阶段，不把独立执行模块/测试称为API、页面或安装态完成，完整P0–P5的76项保持。

## 交付结构与当前状态

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
