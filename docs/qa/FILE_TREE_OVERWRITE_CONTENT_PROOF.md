# 覆盖历史归档内容证明修复候选（2026-10-21，RPC1.35）

状态：active。修复已以2c8d1bb覆盖部署并通过安装态UDS与真实身份BFF子范围；完整网页交互仍未验收。

## 最新冻结实机结果

- **2c8d1bb5fab15f6f79bd07b22de8eea344114da6** 的工作站隔离真实PG：1实际执行/1通过、无跳过，临时库清理完成；状态`dispatch-64b5d6a3c5ba4cdd8dcff0f9b0a35083.json`为Passed/cleanupConfirmed，独立Docker查询确认自有SDK容器不存在。SDK摘要仍为`sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。原c865a75失败保留，不改成成功。
- c865a75→2c8d1bb的src/host/web生产代码差异为空；新冻结修正共用接管测试及BFF元数据检查。最终本地Release零警告/错误、1758 Unit/146 Integration通过（6本机原生跳过）保持；32覆盖引擎+23 BFF隔离+5观察器为真实Linux root通过的证明，不冒充生产文件执行。
- 通过既有固定更新动作完成正常dev覆盖，无回滚请求。安装/迁移/容器更新单元已结束；六组件实际binary/image来源和HTTP API/Gateway均独立核对为2c8d1bb，Agent minor35。两宿主服务active、11核心healthy。
- 新同源码Linux自包含探针构建成功，根所有artifact副本SHA一致：executable `5a5b0ce4a436bb73bd3730b90be1906db06a868531c3814e174f74885912d927`，DLL `5f17523025277b745d8d99bda0a5122cd84442f46cbdd0ecbe1b6798173912cc`；自有构建容器独立确认不存在。
- 同源码已装签名UDS/固定helper：2,097,159与1,073,741,831字节原件/旧目标/归档/新输出、正确独立归档内容域、操作/owner/取消/错指纹核验全部通过，退出0。新夹具精确清理并独立确认不存在。私有baseline SHA `9B5ECEC6A5C1B604323F67ECC25C52BFBE4FAC3DD07A74E6A7E049C5DA873254`，公开RPC结果SHA `C02EBA2383D8D855E9A0196174BEBA3116ED364D5AFD2242FDE96C353AF25362`。此前4ab928625d794320失败现场仍保留。
- **实际Cookie/CSRF/BFF/SQL/Worker/已装UDS/完整磁盘通过**：专用源目录经产品回收生成Trash捕获，换登录绑定消费历史并覆盖；生成的Overwrite捕获实际用于旧不存在目标恢复及再次覆盖。每树2MiB+7，全量字节/元数据/旧目标原inode/新输出及两组新归档内容域一致；计划/执行分离、同key/确认重放不重做、取消、陈旧版本拒绝、另一登录404/403、匿名401、Origin/CSRF及真实普通有效CSRF403均通过。人类审计、永久Job/SQL/Outbox及零活动意图/零锁核验通过。
- BFF只清理精确自有载荷，永久捕获/归档/root回执/Job/审计保留，不在目录背后删归档。夹具独立确认不存在；私有baseline SHA `3FA5FF14C2A0550E2D0AF686E31EF77506D25DE2694D04B9CB63ACFC6601E4B2`，结果SHA `04BB846474E5FC4BB921E0D2AFB1D8E8784E9A2344E214A0E699E8C63FE6C7EA`。字段不含凭据。
- CI39460444206当前backend/native-postgres/installer成功、frontend仍在原作业执行；1ea9de6及57877a9四CI已全成功。没有把旧CI冒认为新版本全绿。
- 所有新实盘源/备份不同设备观察仍false，不能宣称物理异盘灾备或完整文件管理。真实网页只完成匿名1440登录页截图/边界检查，主题操作工具超时、未发生覆盖写入，详见[网页清单与未完成边界](FILE_TREE_OVERWRITE_REAL_UI.md)。

## 问题与改动

准备实际复用 `Overwrite` 捕获时，源码核对发现：SQL捕获签名原先把 `TargetTreeSha256` 写进 `ContentSha256`。前者包含原inode/元数据并使用覆盖树证明域；后者应使用 `whaledeck-tree-archive-content-v1` 域和归档规范化成员。后续真实恢复计划返回后者，严格的 `RequireCapture` 因二者不相等而拒绝。旧安装态2MiB+7/1GiB+7验收明确未覆盖新捕获复用，因此不能用旧绿色结果证明本功能完整。

- 保留原 `TargetTreeSha256` 及原件inode/完整树核验，不削弱 `RequireCapture`。
- 新增独立计划 `TargetContentSha256`：源自同一完整目标扫描的归档规范化成员，并进入原计划指纹、Agent严格JSON、protobuf字段16、Application计划签名及SQL捕获签名。
- root helper在备份Verified后、交换前及再次观察时核对实际归档内容摘要；保存捕获改用新内容域，不再使用inode树摘要。
- Agent minor35增加 `files.tree-overwrite-restore-content-proof`；Gateway要求35及旧/新两项能力，不能静默向旧Agent降级。公开HTTP请求/24字段响应不变。
- 旧root私有回执只读可查，不改旧指纹或内容，也不能把缺少新证明的旧计划重新Apply/Discard。公开试验前只读查询实际SQL：覆盖意图0、Overwrite捕获0；没有手改或重签历史业务数据。

## 当前验证

- 最终本地Release构建：0警告、0错误。1758 Unit及146 Integration通过；本机6项原生依赖测试明确跳过，真实冻结PostgreSQL另验。
- 新BFF验证器公开DTO纯契约15项通过；8项清理隔离测试在Windows明确跳过，等待真实Linux root执行。
- Linux引擎新增独立内容域计算、错摘要指纹拒绝、实际新捕获用于不存在目标恢复和再次覆盖；另补旧root回执可查但不能重做。仅编译检查，不先宣称Linux执行通过。
- 冻结c865a75现已在工作站实际Linux root执行：32项覆盖恢复引擎、23项BFF契约/清理隔离（无跳过）、5项观察器全部通过。源码只在隔离测试夹具运行，未安装1.35 helper。
- 首轮冻结c865a75真实PG实际执行失败，状态`dispatch-33f0bbaed37b44878619d918cb887b22.json`与CI原生PG均记录失败；独立临时资源清理完成。白名单诊断定位共用现任管理员回收测试`NativeNotificationDispatchTests.FileRecoveryWorkflow.cs:181`仍把旧`TargetTreeSha256`作为capture.Content的预期。改为正确`TargetContentSha256`并增加必须不等于tree的断言；不修改产品代码或削弱签名/归档验证，须在新冻结源码完整重验。
- 新真实BFF脚本仅已实现/编译，尚未执行：通过产品回收生成输入、换登录绑定、计划/取消/陈旧目标拒绝、真实覆盖、两类恢复实际复用、CSRF/Origin/普通用户拒绝、SQL/Outbox/审计/零锁和完整字节/元数据。只在成功且精确路径/inode/全SHA核验后清理自有载荷；保留永久归档、目录、Job、审计和root回执，不在目录背后删除归档。

## 保留的失败与实际部署边界

- 第一轮本地build使用此前Linux探针交叉还原的旧assets，报告NU1301错误源路径。使用默认来源重新restore探针项目成功（4.7秒），未改全局源/代理、未关闭审计，随后全量Release构建通过。
- 第一轮完整测试：1757 Unit通过、1项旧维护快照并发测试的同步Barrier超时；146 Integration通过、6原生跳过。该测试把同步参与者安排在线程池且finally会掩盖先发生的失败。改用两个独立LongRunning线程并WhenAll观察，保留100轮、5秒每屏障和完整快照断言，不改OperationStore生产代码；随后1758 Unit及146 Integration全量通过。
- 本轮先完成了 `1ea9de6` 的正常dev覆盖发布，独立检查六组件和HTTP来源一致、两宿主active、11核心healthy。这是上一候选已部署，不代表新1.35摘要修复已安装。没有请求自动回滚，也没有在已知问题版本执行新的覆盖验收写入。
- 文件根和归档仍同HDD。数据库运行目录独立核验为NVMe，SQL Server备份在HDD；未迁移或改写挂载。

## 协作与后续

GLM/glm-5.3-flash仅整理公开DTO字段，未用工具/读仓库：1次成功、23183ms，input11869/cached0/output269，费用未返回。其“command包含Job”表述不正确，已由GPT按真实DTO纠正；不采纳为实现依据。GPT代理仅补限定测试，主代理负责全部协议/权限/生产源码、发布与实际复核。

下一步观察原CI句柄至完成，补独立覆盖网页夹具/观察器、实际管理员桌面/手机操作及普通权限/正常双端退出；继续目录清理/容器文件及完整76项P0–P5。上方冻结PG、1.35覆盖部署、UDS和BFF已实测，不再重复安排；下方“尚未执行”是对应早期证据边界。
