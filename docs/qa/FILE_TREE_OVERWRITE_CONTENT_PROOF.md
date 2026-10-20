# 覆盖历史归档内容证明修复候选（2026-10-21，RPC1.35）

状态：active，尚未部署本修复或执行新真实身份BFF/网页验收。

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
- 新真实BFF脚本仅已实现/编译，尚未执行：通过产品回收生成输入、换登录绑定、计划/取消/陈旧目标拒绝、真实覆盖、两类恢复实际复用、CSRF/Origin/普通用户拒绝、SQL/Outbox/审计/零锁和完整字节/元数据。只在成功且精确路径/inode/全SHA核验后清理自有载荷；保留永久归档、目录、Job、审计和root回执，不在目录背后删除归档。

## 保留的失败与实际部署边界

- 第一轮本地build使用此前Linux探针交叉还原的旧assets，报告NU1301错误源路径。使用默认来源重新restore探针项目成功（4.7秒），未改全局源/代理、未关闭审计，随后全量Release构建通过。
- 第一轮完整测试：1757 Unit通过、1项旧维护快照并发测试的同步Barrier超时；146 Integration通过、6原生跳过。该测试把同步参与者安排在线程池且finally会掩盖先发生的失败。改用两个独立LongRunning线程并WhenAll观察，保留100轮、5秒每屏障和完整快照断言，不改OperationStore生产代码；随后1758 Unit及146 Integration全量通过。
- 本轮先完成了 `1ea9de6` 的正常dev覆盖发布，独立检查六组件和HTTP来源一致、两宿主active、11核心healthy。这是上一候选已部署，不代表新1.35摘要修复已安装。没有请求自动回滚，也没有在已知问题版本执行新的覆盖验收写入。
- 文件根和归档仍同HDD。数据库运行目录独立核验为NVMe，SQL Server备份在HDD；未迁移或改写挂载。

## 协作与后续

GLM/glm-5.3-flash仅整理公开DTO字段，未用工具/读仓库：1次成功、23183ms，input11869/cached0/output269，费用未返回。其“command包含Job”表述不正确，已由GPT按真实DTO纠正；不采纳为实现依据。GPT代理仅补限定测试，主代理负责全部协议/权限/生产源码、发布与实际复核。

下一步冻结候选执行真实Linux与隔离PG、CI，再覆盖发布1.35，重新执行专用签名UDS、真实身份BFF/Worker及网页；完整76项P0–P5仍active。
