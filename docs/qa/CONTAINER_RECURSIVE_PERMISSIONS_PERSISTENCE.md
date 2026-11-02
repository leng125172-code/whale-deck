# 递归普通权限：永久SQL、八方写入保护与专用Worker候选

> 2026-11-02后续：管理员API/Vue候选2056191及验收器f706c86已接并通过49项接口、832前端Unit及44递归+104关联生产预览分批验证，未部署/未真实业务整链。见[页面候选、失败与边界](CONTAINER_RECURSIVE_PERMISSIONS_WEB_UI.md)。下方未接HTTP/Vue等保留为原批次事实。

2026-11-02。源码候选`a91dcb2`实现真实EF仓储、迁移和专用Worker；`36c2710`补无Root证明拒绝成功的原生断言，`5419f2b`尝试schema隔离，`8ca0679`改为双隔离数据库并同步CI；最终`cca8ec9`修正JSONB断言并全套通过。**尚未部署，尚无递归管理员HTTP/Vue或真实身份业务页面整链验收。** 当前应用仍d1fcca8，宿主e668dbe/Agent1.49保持。59个主项继续部分实现，17项移出不算完成；master尚未交付，不自动回滚dev。

## 实现范围

- `container_recursive_permission_intents`及迁移`20261102044324_ContainerRecursivePermissionIntents`/snapshot：15个CHECK、两个受限OperationJob外键、乐观版本、唯一租约/执行ID/发布屏障和有界索引。原请求、计划、执行绑定、Root开始、计数/历史/结果事实和终态不可改，永久意图不可删除；有历史行拒绝Down，保留旧迁移/触发器及旧降级守卫。
- 真实仓储在共享`pg_advisory_xact_lock(41307341)`事务内处理原登录/幂等、预检与执行双Job、Outbox/事件/审计、90秒租约、取消/过期/处置。锁等待3秒、SQL语句10秒有界；所有事务复核原三层HMAC和回执兼容性。
- 八方双向发布保护涵盖主机file_path_locks及容器upload/directory/copy/move/普通permission/ownership/recursive-permission。旧六类容器仓储反向纳入递归同路径/祖先/后代保留，全局64/每Actor4预算包含递归活动意图。旧保护函数没有被替换或停用。
- 独立保存Root状态、Completed/Changed/Aborted/NotStarted/Unknown计数、outcomeDigest、HDD验证证明和原回执JSON；计数与计划、JSON与列须一致。Completed按当前绑定区分Succeeded/Superseded；Partial/Unknown保留保护、无完成时间且不自动Claim，不自动续做/回滚。明确处置才释放屏障，执行Job保持失败而非假成功，当前确认人与原执行人分别审计。
- 历史验证/摘要/实际变更计数不能因观测变差消失；BindingCurrent/HistoryAvailable/Replayed允许记录新观测，不能把旧true冒充当前HDD可用。保存的SQL结果不等于实时Root/HDD检查。
- DI及专用Worker已接候选；通用Worker明确排除两类Outbox，通用重试拒绝两类任务，通用任务取消路由到专用取消消息。未增加公开占位接口或按钮。

## 验证与证据边界

本地全量3794 Unit通过、259 Integration通过（7项默认原生依赖跳过），Release全方案0警告/0错误；新增迁移/模型检查和两项通用重试拒绝包含其中。最后复核补齐普通chmod路径检查里递归集合的计数/冲突使用，随后Release与新模型专项通过；此修复已包含a91dcb2。

隔离真实PostgreSQL门禁最终已签收：冻结`cca8ec93fa13ef36a4814b55d756ac813727581b`，回执`deploy/artifacts/native-status/dispatch-4e9a58c940be4859b806dc21e686ba55.json`为Passed，实际1执行/1通过/0跳过、cleanupConfirmed=true，外部投递0。不是只重跑失败片段；全套旧通知/文件回归和新递归门禁均执行。执行入口`deploy/validation/validate_notification_dispatch_postgres.py`，固定SDK镜像`sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`，只读git归档、受限SDK容器，不挂Docker/Agent socket或生产私有数据。最终方案由root验收器创建两个随机自有DB，应用角色始终非superuser/NOCREATEDB；旧host回归及其Unknown锁在原DB保留，递归使用另一DB从全部真实迁移建表、重跑旧容器四类门禁提供永久种子再验新流程。两个连接仅经0600临时env文件进入测试容器，finally精确清理两个DB/本次SDK；CI也创建两个自有库并保持只允许1次实际执行/通过的TRX门禁。

前三轮记录保留：a91dcb2888cd8e0791e92ccf2ca822d77dd7fac5（dispatch-6ff88d3126e34c048dc7d32d8892ebd9.json）因错误假设原host锁为空在新测试起始失败；5419f2b94c95ab5ee66ef746d6980efdd65c0ef7（dispatch-9a1145729bd2490cbb185c59e920ebe1.json）改独立schema后Migrate遇42P07；1895dbd9c7076c984490ae17de95b09eb4d75cf1（dispatch-20f48f6e1f3c439a821bebef9f015e49.json）诊断轮在确认旧指标SQL明确引用public后准确中断，记Failed/KeyboardInterrupt。三轮cleanupConfirmed均true，不改记通过。最小隔离迁移SQL复现亦证实public限定，精确临时DB已删除；没有修改旧指标迁移、释放Unknown锁或放宽产品保护来过门禁。

第四轮冻结8ca0679c774244010fe086de294a563abf8527a9，回执dispatch-58158887915846b799eb0f5d54a0fbd5.json为Failed且cleanupConfirmed=true：双库及真实迁移、前段新断言已执行，测试在JSONB PayloadJson上用Contains翻译成不存在的字符串函数，SQLSTATE42883。改为参数化JSONB jobId字段精确匹配，不改产品取消路由或数据库类型；最终cca8ec9第五轮完整重跑通过，第四轮失败回执原样保留。Root回执、Actor和测试时钟是明确合成输入，不是实际Root/HDD/Authentik证明。

新原生断言覆盖无Root证明不得成功、双连接确认幂等、双Job、请求/计划/开始/结果/终态不可改、同路径及祖先后代双向拒绝、八方原始SQL双向屏障、Unknown/Partial不Claim及独立确认/计数保留、通用取消路由、自然测试时钟租约到期、取消/过期、审计失败事务回滚、每Actor预算和跨旧仓储预算、锁等待超时、拒绝非空降级。新测试位于原dispatch全套回归最后；旧降级守卫继续实际验证，不用新守卫掩盖旧检查。原DB全部路径锁完整JSON在新递归门禁前后比较；保留两库永久历史至执行器清理整个自有DB，不能据夹具清理当作产品Unknown解锁。

迁移工具初次缺DOTNET_ROOT、EF生成代码触发IDE0161/CA1861、原生测试初次引用不存在CancelAsync及独立schema尝试初次拼接标识符触发EF1003均在候选编译阶段修正（schema改为隔离DB内固定常量，不压制分析器）；未关闭分析器或执行生产写入。模型专项本身通过后，同命令普通用户读取root私有native-status被拒绝；后续使用sudo只读核对，不放宽证据权限。失败日志保留在`/tmp/wd-recursive-persistence-*`，私有回执位于忽略的deploy/artifacts，不入Git。

## 接续门槛

双隔离PG全套及独立资源清理核验已通过；下一步接管理员API/Vue的原树计划、确认、接续/取消、明确Partial/Unknown处置与实时身份清理；完善任务/通知投影。API/Vue衔接对应现有`ContainerOwnershipsController`、`containerOwnerships.ts`及容器文件/文件恢复面板；递归入口必须分别设置FileMode/DirectoryMode，展示Entry/File/Directory/Change计划计数和五类实际结果计数，同时独立呈现Partial与Unknown，不能复用单对象成功样式掩盖部分变更。正常覆盖发布后需真实Authentik→页面→SQL/Worker→Agent→实际树/HDD、明暗/320px和故障组合验收。现有Agent原生签名/HDD证据独立保留于[上一批记录](CONTAINER_RECURSIVE_PERMISSIONS_WORKFLOW.md)，不能拼接成这轮真实业务页面已通过。

递归所有者、ACL、可恢复删除/长期清理及其余59主项、UI风格、C#酒馆/统一供应商/AI Live2D继续，不能据本候选签完整递归权限或文件模块。

## 独立清理核验与当前运行边界

2026-11-02T05:22:28Z，另一个root进程重读全部五份回执：前四Failed及其cleanupConfirmed=true保持，第五Passed/1执行/1通过/0跳过；五个准确SDK容器均不存在，随机dispatch临时数据库为0。11核心容器与门禁运行期间保存的基线ID/镜像/StartedAt/状态一致并均healthy；没有据此冒称对比了首轮启动前未保存的基线。Agent与维护入口active，应用角色superuser/createdb/createrole/replication均false。生产候选表明确不存在，API/Worker/Gateway镜像revision仍d1fcca891830b7a43da0b253c3abcad67a66c18e，本轮没有应用/宿主发布或生产schema变更。user NOPASSWD:ALL的sudo -n/visudo仍通过且保留，产品Agent授权未扩大。

独立私有报告`deploy/artifacts/recursive-permission-persistence-cca8ec9/independent.json`为root私有0600，包含准确非敏感核验结果，不入Git。新库原生计数为一个实际Fact内的完整断言链，不重复累计各子场景为测试/功能完成数。Root/身份/时钟合成边界保持；该通过结果不代替真实Worker进程、Authentik会话、目录/文件/HDD或UI。

最终日志：`/tmp/wd-recursive-persistence-{unit-final,integration-final-db,build-final-all,gates-final,native-fifth,independent}.log`；前四native-first/second/third/fourth及编译失败日志均保留。Python语法、10项范围/回执门禁（含8项范围）、本轮MD链接及git diff检查通过。未修改前端，未声称新增浏览器验收或远程CI全绿。
