# 容器 ACL：永久 SQL、不可变回执与十方共享保护候选

> 后续API/Vue候选（2026-11-03）：062e361已接实际业务的管理员端点/容器文件及Unknown处置页面，4272 Unit/432 Integration（7默认原生跳过）、972前端Unit、43 ACL及156关联生产预览两组通过；尚未部署或真实身份ACL/HDD业务验收。下方72d678e/c9c9265原生PG与“无API/Vue”等属于原批次，正文逐字保留。[页面候选与边界](CONTAINER_ACL_WEB_UI.md)。

2026-11-03。SQL/仓储/测试源码 `72d678ebababbe748b3b34dc164700a8cf11326d`；通用任务入口加固 `c9c9265f900284fb989bb533d10be8111ea2bdc7`。已实现具体持久化、共享保护和专用 Worker 候选，**本批未部署，尚无 ACL 管理员 API、Vue 或真实身份/文件/HDD业务页面整链**。59主项不晋升整项完成，17项移出不算完成，master尚未交付。

前批安装态 Agent/MaintenanceHost `4a58961` / 1.51 与 API/Worker/Gateway `b1fafed` 的运行事实保留；本批不重跑原 ACL RPC/HDD、不重启服务、不修改生产 schema，不自动回滚 dev，不修改开发 user NOPASSWD 或密码。

## 已实现

- 新表 `container_acl_intents`、迁移 `20261102180208_ContainerAclIntents` 和 EF snapshot，17个 CHECK、两个 OperationJob 受限外键，原主体/幂等键、执行ID、租约Token、活动屏障唯一索引及版本并发检查。没有使用 EnsureCreated 或在启动时跳过迁移。
- 原登录/命令、完整原计划、确认执行与 Started 标记不可改；精确原回执 JSON、独立 HMAC、三态确认、原发布版本和已验证历史不可被更新替换。历史可能存在标志只增不减，永久行拒绝删除、终态不可变、非空表拒绝降级；所有旧迁移/触发器及旧降级守卫保留。
- 迁移创建纯 JSON 的 PostgreSQL immutable 校验函数，严格验证至多64个条目、Tag/Id/Permissions三个字段的存在和类型、规范顺序/唯一性、基础条目、named数值ID范围与显式mask；0是有效显式值，缺失/null/bool/4294967295不得当0。default空列表表示清空，access空列表拒绝。mask参与目标模式推导，计划的原ACL/模式/NoChange以及命令/路径在 SQL 中互相绑定，不支持或不一致不放行。
- 全部仓储状态转换使用共享事务锁 `pg_advisory_xact_lock(41307341)`，锁等待3秒、语句10秒有界；计划和执行双Job、Outbox、事件与脱敏审计同事务，审计失败回滚全部新增记录。读取/确认/原幂等重试校验请求、计划、执行和回执HMAC；写入前保留原登录与实时权威身份门槛。
- 90秒自然租约与原Token接管，Expired旧Token不能续租/完成/再次开始。Ready、Started和Unknown不因重试续期；Started后仅查询或Discard原执行，Unknown不自动Claim，屏障保持至当前管理员明确处置。确认原版本和原执行ID，跨管理员处置只记录新处置主体，不冒充原操作者或成功写入。
- 原回执连同独立HMAC及标量事实原子保存，首次落盘后精确保留。后续处置回执另存 AcknowledgementJson；不会把Replayed/当前绑定观察覆盖历史原件，也不能把Unknown改成Aborted/Completed。缺原回执的Unknown可以在原ID可靠fence后保存首次真实Root结果；不根据“未收到回复”推断零修改。
- 十方双向发布保护：主机 `file_path_locks` + 容器upload/directory/copy/move/普通permission/ownership/recursive-permission/recursive-ownership/ACL。既有八类容器仓储都检查新ACL同路径/祖先/后代保留、全局屏障；ACL反向检查所有旧方。活动意图计入共享全局64/每主体4预算；新触发器在所有旧表上阻止原始SQL绕过发布互斥。
- 已注册具体仓储、业务服务、Dispatcher与独立 ContainerAclWorker。通用取消路由到ACL专用消息，通用Worker跳过请求/取消两类专用Outbox，通用重试拒绝ACL规划和执行任务，要求新版本绑定计划与明确确认。Worker仅有有界脱敏警告，未扩大Agent sudo/capability或普通用户权限。

## 自动验证

| 检查 | 实际结果 | 边界 |
| --- | --- | --- |
| Release全解决方案 | 0 Warning / 0 Error | 最后构建含通用Worker/重试修复；未发布 |
| Unit | 4272通过 / 0失败 / 0跳过 | 末轮新增两种ACL通用重试拒绝；此前4270轮不相加 |
| Integration | 364通过 / 0失败 / 7默认原生跳过 | 新增模型/迁移检查；默认跳过不记原生成功 |
| 范围与原生回执状态检查 | 10通过 | 8范围 + 2回执状态，不替代产品验收 |
| 双隔离真实PostgreSQL完整门禁 | 1执行 / 1通过 / 0跳过 | 冻结72d678e Git archive，旧回归与新ACL链完整执行，独立清理核验通过 |

本地最终构建/全量日志 `/tmp/wd-acl-persistence-worker-build.log`、`/tmp/wd-acl-persistence-final-{unit,integration}.log`、`/tmp/wd-acl-persistence-scope-status.log`。构建及本地测试在提交前相同内容树执行，随后按提交核对源文件摘要；原生门禁从冻结72d678e归档实际构建。c9c9265只包含通用Worker消息过滤、通用重试拒绝和对应测试，不改数据库模型/仓储/迁移；分列来源，不冒称原生门禁执行了后加的Worker进程。

## 真实 PostgreSQL 门禁与边界

入口为 `deploy/validation/validate_notification_dispatch_postgres.py`。两套随机自有 `wdv_dispatch_<16hex>` 数据库，应用角色非superuser；受限SDK固定镜像、只读Git源码，无Docker/Agent socket。连接串只驻留临时0600 env，测试原始SQL/进程输出不写公开日志。旧主机故障锁留在第一库，第二库执行旧容器回归与新ACL持久化断言，结束校验第一库锁JSON完全不改。首个实际执行回执 `deploy/artifacts/native-status/dispatch-01cf21b00721413798ba907126b62411.json` 为Passed，executedTests=1、passedTests=1、cleanupConfirmed=true；精确SDK镜像 `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。原生主日志 `/tmp/wd-acl-persistence-native-72d678e-second.log`，没有失败后只重跑ACL片段或重复实际文件操作。

新增断言包含：同路径/祖先/后代ACL与普通chmod双向排斥、SQL条目规范与显式0/mask模式、default设置与清空持久化、双连接确认不重复Job、原请求/计划/Started/回执摘要/历史/终态不可改、永久删除拒绝、十方原始SQL双向屏障、Unknown保留/不同管理员处置和原件精确保留、原Token过期后接管与旧完成拒绝、缺回执Unknown可靠fence、取消/计划过期、审计失败整体回滚、主体预算及非空降级拒绝。SQL屏障夹具仅在回滚事务中克隆明确旧种子，只证明数据库互斥，不冒称通过原业务HMAC。

Root回执、身份与时钟为合成输入；租约接管通过测试时钟偏移验证，不声称真实Worker强杀/墙钟90秒或真实用户撤权。即使真实PG门禁通过，也不等于实际ACL修改、HDD、Authentik/BFF、Worker进程或UI验收；不得拼接前批机器RPC证据当完整业务整链。

## 独立只读核验

2026-11-02T18:17:49Z，另一root进程重读原生回执，核对其摘要 `b1380767117dce73a17e4449e9a066a5a7ce0fb85408b59f49b77d6b37dbd5a9`；准确SDK容器不存在，临时dispatch数据库集合恢复原样。11核心（3个WhaleDeck+8个数据库平台）容器的ID/镜像/StartedAt/状态/健康与门禁前完全一致，均healthy，生产ACL表明确不存在。应用角色superuser/createdb/createrole/replication全部false，Agent与维护服务均active；未采样本轮宿主PID，因此不扩写为PID不变证明。

30个SQL/仓储/模型/迁移/原生测试来源文件按字节仍与72d678e一致；c9c9265通用Worker/重试修复由最终本地全量验证，不能把原生72门禁冒称实际Worker进程验收。私有证据在 `deploy/artifacts/acl-persistence-72d678e/`，目录0700、文件0600，含before/after/independent、原生回执、构建/失败/全量日志、来源摘要和SHA清单，不入Git，没有环境或连接串。范围仍59，未新增前端测试/真实PNG/业务页面操作。

## 尝试、失败和下一步

- 已重试上轮未成功的正常Git推送，仍在TLS握手以GnuTLS/exit128失败，保留原回执；不能称远端更新或新CI全绿。
- EF工具不在默认PATH，既有apphost直接运行缺DOTNET_ROOT。定位已安装10.0.4工具并由现有10.0.402的dotnet启动DLL生成迁移，没有安装/升级SDK或依赖。
- 首次迁移SQL拼装器的缩进标记不匹配，Python中止于落盘前；改为迁移partial常量后继续，未执行数据库操作。
- 生成迁移有IDE0161及3个CA1861错误，转换为文件级namespace和集合表达式后零警告错误；没有禁用分析器。
- 首次原生包装器误把“11核心含8数据库平台”当成“11 WhaleDeck + 8数据库平台”，只读数量断言在创建资源前失败；修正为3+8=11后启动冻结门禁，原失败日志保留。

附加只读核验首轮对Docker缺失容器错误使用大写“No such object”匹配，本机返回小写“error: no such object”；准确容器本就已清理。改为大小写无关匹配后完整独立核验通过，未重新执行PG门禁或变更任何资源，不把观察器失败冒称产品故障。

下一步：接严格管理员API/Vue，逐字段区分ID/permissions缺失与显式0、原两类ACL/目标条目/mask影响、预检/确认/取消/原引用恢复与Unknown处置。正常冻结覆盖后，以真实Authentik→Vue→API/SQL/Worker→Agent→实际inode/HDD验证常规与故障整链；之后ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/统一供应商/AI Live2D继续。

文档终检：224份Markdown、1364处相对链接无缺失，目标第1节正文保持不变，旧Workflow原始正文按字节保留，`git diff --check`通过。文档/测试夹具数量不记产品完成率。

项目图谱事件 `whale-deck.container-acl.72d678e.persistence` 已取得projectId/key/eventId完全一致的成功回执，记录移入synced；阶段保持active。GitHub只读连接在收尾恢复，远端仍dc87d1c，随后正常提交并推送，以实际远端SHA回执为准。
