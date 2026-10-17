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
- GLM glm-5.3-flash只读定位四个实体/任务配置文件，实际Controller为FileMutationsController而非请求中不存在的FileOperationsController；GPT复核。1次成功/0重试，input81234/cached57024/output1642 Tokens，64662ms，费用未返回。安全与架构、实现、权限和实际操作由GPT负责。
- 后续GLM glm-5.3-flash只读整理四个现有测试文件的夹具/清理/入口；GPT实际阅读复核后编写测试。1次成功/0重试，input287502/cached243648/output4189 Tokens，159553ms，费用未返回。其过滤命令是建议，原生实际执行与清理由冻结源码验证器负责，不因只读分析计作测试通过。
