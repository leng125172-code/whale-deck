# 递归所有者：永久SQL、九方写入保护与专用Worker候选

> 2026-11-02后续：a806f56已接管理员API/Vue候选，4036 Unit/363 Integration（7默认原生跳过）、909前端Unit及44项递归所有者生产预览浏览器通过。未部署、未真实身份/文件HDD整链；下方“无管理员API/Vue”等为a19f5c4原批次事实，双隔离PG原始正文保留。[最新页面候选与边界](CONTAINER_RECURSIVE_OWNERSHIP_WEB_UI.md)。

2026-11-02，冻结源码`a19f5c40d14a20b79854dbccb9bb20b92b8f8d6c`。具体EF仓储、migration、双向共享保护与专用Worker候选已实现，双隔离真实PostgreSQL全套首轮通过并独立核验清理。**尚未部署，尚无管理员API/Vue或真实身份业务页面整链**。本批没有重跑前批安装态RPC/HDD、重启服务或修改生产schema；59主项继续部分实现，17项移出不计完成，master未交付，不自动回滚dev。

## 实现与安全约束

- `container_recursive_ownership_intents`、`20261102131222_ContainerRecursiveOwnershipIntents`及snapshot：16个CHECK，双OperationJob受限外键、原Actor/幂等唯一键、唯一执行ID/租约/发布屏障、有界索引及并发Version。显式UID/GID必须存在、为整数且在0至4294967294范围；0可保存为root，缺失不能变成root。原根属主/目标UID-GID/执行身份保持独立。
- 原请求、密封整树计划、确认执行与Root开始标记不可改；Root状态、五计数、outcomeDigest、已验证历史与摘要、可能已有历史事实不可被回写抹除。永久意图拒绝删除，终态拒绝变化，非空拒绝Down；已有迁移、旧保护函数与旧降级守卫不替换或停用。
- 仓储全部状态转换使用共享`pg_advisory_xact_lock(41307341)`事务，锁等待3秒/语句10秒有界；原登录/幂等/请求计划执行HMAC、版本、租约、Root回执兼容性重新检查。规划/执行双Job、Outbox、事件和脱敏审计同事务，审计失败整体回滚。
- 90秒租约按原token续租/接管，过期旧owner不能再续租或开始；Started后只能观察/封闭原执行，不重新Apply。Partial/Unknown保留原事实与屏障，无完成时间且不自动Claim；明确处置后执行Job为Failed/Acknowledged，不冒称整树成功。确认人与原执行人分开记录。
- 九方双向保护包括主机`file_path_locks`，及容器upload/directory/copy/move/普通permission/单对象ownership/recursive-permission/recursive-ownership。新旧仓储都识别同路径/祖先/后代保留及全局发布屏障；活动意图加入全局64/每Actor4预算，SQL触发器阻止绕过应用直接取得冲突发布屏障。
- 历史验证、摘要和五计数是永久事实；BindingCurrent/HistoryAvailable/Replayed为最后一次观察，当前可用性变差不抹掉原事实，也不把旧SQL快照当作实时HDD检查。
- 注册具体仓储、业务服务、Dispatcher及独立`ContainerRecursiveOwnershipWorker`。通用Worker排除两种专用Outbox，通用重试拒绝规划/执行任务，通用取消转入独立取消消息。没有新增占位HTTP、默认User权限或前端按钮，未扩大宿主Agent授权。

## 冻结源码验证

| 检查 | 实际结果 | 边界 |
| --- | --- | --- |
| Release全方案构建 | 0 Warning / 0 Error | 不发布 |
| Unit，Release无构建 | 4036通过 / 0失败 / 0跳过 | 比前批新增两种通用重试拒绝 |
| Integration，Release无构建 | 309通过 / 0失败 / 7跳过 | 含新模型检查；默认原生PG/Valkey测试跳过 |
| 双隔离真实PostgreSQL dispatch全套 | 1执行 / 1通过 / 0跳过 | 同一Fact包含旧通知/文件回归与新递归所有者断言链，不按子断言重复计数 |
| 范围及原生回执状态门禁 | 10通过 | 8范围+2回执；不代表产品功能签收 |

本地构建/Unit/Integration最终日志为`/tmp/wd-recursive-owner-persistence-{build-frozen,unit,integration}.log`。此前两项迁移模型专项也通过，不与全量结果相加。

真实PG入口：`deploy/validation/validate_notification_dispatch_postgres.py`，冻结Git archive、受限SDK镜像`sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。两个随机自有`wdv_dispatch_<16hex>`数据库，应用角色非superuser/NOCREATEDB；连接串仅通过临时0600 env进入无Docker/Agent socket、只读源码的SDK容器。原host回归及其Unknown锁留在第一个库，第二个库执行真实migration/旧容器门禁/递归chmod/新递归所有者链；原库全部路径锁完整JSON前后相等。不释放旧锁、不修改生产时钟/SQL/缓存来取得通过，最后由执行器准确清理本次资源。

首轮回执`deploy/artifacts/native-status/dispatch-94b628a94db344b1b5463e9c22688b49.json`为Passed，executedTests=1、passedTests=1、cleanupConfirmed=true；日志`/tmp/wd-recursive-owner-persistence-native-first.log`。没有失败后只重跑片段或重复已完成实机操作。

新断言涵盖：双连接确认只创建一个原执行及双Job；原请求/计划/Started/结果/终态不可改、永久删除拒绝、无Root证明不可成功；普通chmod与递归chmod同路径/祖先/后代双向保留；九方原始SQL双向屏障；Unknown/Partial不Claim、不同管理员处置/五计数与历史保留/当前观察可变；原token自然测试时钟到期接管；取消/过期、通用取消路由、审计失败回滚、每Actor及跨旧仓储预算、锁等待超时、非空降级拒绝。SQL直接插入的UID/GID各自缺失/4294967295四种输入被CHECK拒绝，显式0:0持久化通过。原始SQL屏障夹具只在明确回滚事务中克隆种子，不删除永久意图。

**Root回执、身份和时钟为合成输入**。真实PG通过不代表真实Worker进程、原生Root/HDD、Authentik/BFF、浏览器权限与实际所有者变更已验收；不能把[前批安装RPC/HDD](CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)拼接成本批真人业务整链。

## 独立核验与失败保留

2026-11-02T13:22:03Z，另一root进程重读首轮原回执，准确SDK容器不存在、临时dispatch数据库计数0；11核心容器与门禁前基线的ID/镜像/StartedAt/状态/健康完全一致，均healthy。生产`container_recursive_ownership_intents`明确不存在，Agent与MaintenanceHost均active，应用角色superuser/createdb/createrole/replication均false。API/Worker/Gateway仍0dede08，前批宿主22906f5/1.50安装事实保留，不据本批active推断未保存的宿主PID对比。

私有`deploy/artifacts/recursive-ownership-persistence-a19f5c4/{before,independent}.json`保存基线和独立结果，目录0700/文件0600，不入Git；没有保存环境变量或连接串。user的`sudo -n true`实际通过且授权保留，未改sudoers或密码。

候选编译阶段曾有两处签名方法名适配遗漏（CS1061），修正为独立ownership HMAC；工具首次缺DOTNET_ROOT，仅设置固定已安装工具链后生成migration，没有安装新SDK。生成迁移三处常量数组触发CA1861，改集合表达式后构建通过，未关闭分析器。一次Python组合命令缺PYTHONPATH导致回执测试ImportError，8范围已执行；用明确`PYTHONPATH=deploy/validation`重跑完整10项通过，原失败不记通过。这些失败均不触及生产写入，原日志/回执保留。

前批f0ce4d3的CI运行41450148088首次查询EOF，重查为backend/native-postgres/native-valkey/installer成功、frontend仍运行；不冒称全绿，也不把旧CI计作本批候选CI。

## 接续门槛

接管理员API/Vue：严格显式目标UID/GID/重复字段拒绝，原树异步预检、明确确认、原引用恢复/取消、原/目标/执行身份区分、五类结果计数与历史/当前观察、当前管理员明确处置；覆盖真实登录撤权与小屏明暗。正常冻结覆盖后验证真实Authentik→Vue→API/SQL/Worker→Agent→实际树/HDD常规与故障整链，并验证旧写入兼容；不能据模型或真实PG签收完整文件模块。ACL、可恢复删除、长期清理、P0-05与其余59项/UI/C#酒馆/统一供应商/AI Live2D继续。

文档检查：212份Markdown/1285处相对链接无缺失，`git diff --check`通过；前批Workflow原始正文逐字保留，只前置后续说明。文档检查不替代功能验收。
