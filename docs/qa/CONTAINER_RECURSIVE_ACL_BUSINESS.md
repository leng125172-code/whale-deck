# 递归 ACL 内部业务编排候选

后续进展：224f57c已补真实Repository/迁移/共享fence及Worker注册，并通过真实隔离PG门禁，见[持久化分项验收与生产未升级边界](CONTAINER_RECURSIVE_ACL_PERSISTENCE.md)。以下未持久化措辞保留为本批当时事实，不倒填本页测试或真人整链证据。

2026-11-04，内部编排源码`0168d5b48e5696a4ed89444dc3a8f369630c2cce`，输入快照边界补强`4b01148c6d9f4bef5fb437500f481d66e99c2bea`。上批[安装态签名RPC/HDD](CONTAINER_RECURSIVE_ACL_AGENT.md)已通过，本批没有重放真实操作、发布或重启服务。**本批仅实现内部业务与持久化接口契约；真实仓储、迁移、共享路径互斥、Worker/DI、公开API/Vue和真实身份整链尚未接通，59项主任务及阶段不升完成。**

## 已实现的内部行为

- 新Domain意图、请求/计划/执行/确认/回执绑定、工作流服务、分发器和独立业务保护器。规划与执行使用不同Job ID；相同原Subject/登录/幂等键重取原意图，不续期限、重扫或产生新执行，目标变化明确冲突。
- request/plan/execution/receipt使用四个独立HMAC域，确认token另有独立域，Intent/owner/执行ID亦与普通ACL、递归chmod/chown隔离。原容器/实例/路径、完整file/directory ACL、aclType、身份/登录、trace/owner、Job/幂等键、时间窗、清单/目标证明及原版本均参与绑定。
- canonical JSON限制64KiB/深度16，拒绝多余/重复/非规范表示。比较两组ACL列表的实际有序值，保留default文件null、目录空列表清除语义和named ID0/权限0。命令在第一次await前深拷贝；Agent计划在RPC返回后、续租/重新授权await前深拷贝。后续读、确认和执行均从原JSON重新核验。
- 先验证每组ACL不超过64项，再序列化快照，避免无界列表先被遍历/分配后才拒绝；复制后再次验证，防止可变调用方列表影响已绑定请求。6项上限/拒绝顺序测试使用禁止枚举的65项列表，不分配实际超大数据或触碰生产内存上限。
- 当前权威管理员和原登录必须有效；队列/读取/确认/取消/处置及返回私有结果前复核，长RPC每6秒检查撤权/取消/期限/续租，335秒生命周期与10秒检查预算。规划15分钟、Ready至多5分钟窗口，明确确认后的执行15分钟；到期不重签或自动续做。
- 执行先Inspect原ID；已有结果只对账。已取消/撤权/过期或原Started标记存在时只Discard原ID；共享屏障Busy/能力不足不得Apply。丢回执不能断言无变化，超期Unknown保持保护；不会自动重放Apply或回滚。Completed的当前绑定已变记Superseded，Partial/Unknown保留实际计数、outcome和历史事实。
- 新ReceiptHash将原operation和完整合法回执单独HMAC绑定，并核对持久化可见列；同时篡改canonical回执与计数/可用性列仍不能绕过MAC。管理员明确处置时允许新的当前绑定/历史可用性/重放观察，不能改写原状态/计数/outcome/历史证明或抹除可能存在历史。另一当前管理员仍只封存原执行ID，原Actor/登录绑定不被替换。
- DTO包含原root mode/UID/GID、执行UID/GID、树计数/manifest、两组目标与实际回执；警告区分ACL条目UID/GID与inode所有者，说明access mask会影响模式、default只作用目录、取消可能部分变化。敏感绑定/确认token的ToString不展开正文。

## 实际验证与准确边界

最终4b01148冻结提交完整Release构建0警告/0错误；两个实际候选CLI --build-info返回4b01148并退出0，未启动新服务。11个实现/测试源码的SHA与Git冻结内容核对并保存。最终全量Unit **4649通过/0跳过**，其中新增递归ACL业务专项**149项全部通过**；默认Integration **490通过/7跳过**，范围基线8项通过。七项默认原生测试没有执行，不记新业务真实PG/Valkey通过。本批没有改动Linux helper或前端，不冒称重跑506个root文件测试或前端全量。

专项覆盖：两种ACL/default清除、零值与大整数、列表别名在仓储await/续租期间被改写、等值不同数组幂等、目标/清单/身份/期限篡改、跨域/正确MAC但非规范token、能力/屏障Busy、取消/退出/实时身份与租约失效、回执丢失后只对账、写后撤权、身份目录故障下保留可信事实、Partial/Unknown跨管理员封存及历史可用性下降、回执/列同时篡改与缺失MAC、快照前有界拒绝。

这些是显式内存Repository、typed Gateway、身份和临时随机密钥夹具下的内部测试，不是实际SQL/outbox、真实共享路径互斥、实际撤销Authentik组或新的Root/HDD写入。仓储接口已经要求所有文件写入共享排他，测试验证Started=Busy不调用Apply；真实跨容器/跨目录屏障仍须下一批PG事务、查询与故障验证，不能用该接口和夹具补签。

首轮构建5个CS0103来自复制测试夹具缺少Entries静态导入，修正后通过；首批143项专项及4643/490通过，随后因快照前上限补强新增6项，最终重新执行全量4649/490并通过。证据清单首次glob遗漏I开头接口导致11文件计数断言失败，改显式列入接口后核对通过，未重复实机操作；初始与最终源码清单/测试原件分列保留。上轮待同步的c05bae1文档本轮已正常推送并核对远端。

私有证据`deploy/artifacts/recursive-acl-business-8155a571fd61428a9e756c7b4144b8c7/`保留原始TRX/日志、初始及补强源码SHA、冻结记录、候选CLI来源和只读安装态观测，不入Git。观测比较前一已验安装基线，并非本轮开始前另做的快照：两宿主仍7cb3935/1.53（Agent2799620/维护2799617），四容器及账本仍28062da；11核心ID/镜像/StartedAt保持且healthy，7配置/完整账本保持，旧145普通和23递归候选元数据九字段/字节保持，总数仍153/58。原递归RPC的Root字节及HDD计划/散列/计数再次只读核对相符，没有覆盖原验收结论。活动为0，root只读Git未改变索引且所有者1000:1000，NOPASSWD策略未改。

## 后续接入要求

1. 新EF映射/迁移与真实Repository，将意图、planning/execution Job、outbox、原子claim/lease/version/取消/到期/准确结果和审计真正持久化；保存或处置回执时原子写ReceiptJson、ReceiptHash与匹配列，再按原版本释放保护。
2. 将新业务纳入全部文件写入的共同事务排他、directory路径冲突及原Started/Unknown/Partial保护；真实PG验证跨容器/跨目录、并发claim/确认、租约/崩溃、直接非法写入CHECK拒绝及恢复，不降低旧操作保护。
3. 完成DI/Worker及通用outbox分派排除、管理员公开API/严格输入、Vue预检/确认/任务/恢复页面，再标准部署并以真实Authentik身份、实际文件/HDD及页面证据签收。
4. 继续安装态完整故障矩阵、递归ACL历史恢复、可恢复删除/长期清理、维护入口零中断及其它59项。[当前目标](../GOAL_P0_P5.md)和[实施状态](../IMPLEMENTATION_STATUS.md)保持为准，不自动回滚dev。
