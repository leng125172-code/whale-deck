# 容器移动：永久SQL、双任务与五方发布保护

2026-10-29。持久化实现30cfcb8，原生门禁最终冻结73fa4dd。仍为未部署业务候选；生产数据库未执行新迁移，安装态Agent/MaintenanceHost仍53701f8/minor46，API/Worker/Gateway仍7b3f44e。59项继续active，17项移出不计完成。

## 实现

- 独立 `container_move_intents` 和EF migration `20261029080510_ContainerMoveIntents`，两个Restrict Job外键、幂等/执行ID/租约唯一索引、全局发布唯一索引、并发版本与13项数据库约束。
- 请求/计划/执行/RootStarted/终态不可改写，历史不可DELETE；已知的发布、源移除、原件留存事实不能反转或清空，包括false。成功必须三项 `IS TRUE`，避免SQL CHECK的NULL语义绕过。
- 登记、确认、计划结果、执行结果、取消和管理员Unknown处置均在同一事务写入相应Job/Event/Outbox及审计。独立MAC继续校验全部原绑定。
- 移动/复制/上传/新建目录/宿主路径锁共享advisory事务门41307341；同时修改所有旧仓储的反向检查、路径冲突和全局/每人预算。数据库触发器使不认识移动表的旧写入者也无法同时取得发布保护。
- 源路径与目标路径均保留层级冲突；复制读取的源也不能被同时移走。不同容器可能别名同一挂载，发布阶段仍以全局屏障保护。
- 90秒租约、精确token、只认当前租约的完成、RootStarted后只观察/封闭，以及Unknown保护保留。缺回执不抹掉已有事实；当前管理员确认使用原操作回执并另记当前身份审计，不删除原件或把Unknown改报成功。
- 已补管理员HTTP、专属Worker、通用取消分流和禁止通用重试；专属Outbox不会被普通Worker误消费。HTTP只做有界业务映射，未在请求内扫描/移动源数据。

## 已执行检查

- 最终组合Release零警告错误；3189 Unit及209 Integration通过，7项原生依赖用例默认跳过。
- 冻结73fa4dd在真实PostgreSQL临时库从空库迁移，原生dispatch门禁 **1执行/1通过/0跳过**；该单用例实际执行新的ContainerMoveBoundaries及全部既有通知/文件回归。
- 移动专项包含：双连接幂等登记和确认、路径源/目标冲突、文件/目录完整计划持久化、未知事实true/null与false/null、永久历史及事实反改拒绝、缺原件事实不能成功、失效计划、计划中取消、RootStarted保留、90秒失租与旧租约拒绝、真实数据库锁等待55P03后无启动标记、审计失败后四类残留均0、并发预算和降级保护。
- 五方保护分别实测仓储返回Busy及原生SQL触发器拒绝，覆盖移动与宿主锁/上传/目录/复制双向关系。较早复制和其他恢复保护仍单独执行，未为新migration删除旧断言。
- 独立第二进程确认4轮准确测试容器均不存在、所有本次dispatch临时库为0，11核心容器ID/镜像/状态/StartedAt与安装前基线一致。没有生产外部邮件/Webhook投递。

此原生门禁操作真实SQL，但使用合成Root回执，不等同实际移动Agent、当前人类身份或页面整链；它们需要另行部署验收。

## 首轮失败与修正

1. EF生成器输出旧namespace/数组形式被仓库分析器拒绝，按现有规范修正；另修复测试局部变量重名。审阅时发现新移动触发器默认分支仍使用COPY错误码，已改为MOVE，保留复制分支的COPY错误码。
2. 30cfcb8原生运行在审计回滚断言处42883：测试对JSONB使用字符串Contains。改为参数化精确 `payload_json->>'jobId'` 查询，零残留断言保留；2528194重跑。
3. 2528194原生运行实际得到55P03，但EF执行策略包裹在InvalidOperationException中，测试错误要求顶层PostgresException。改为检查真实最内层PostgresException/55P03，并增加RootStarted为null/屏障false断言，未改产品的事务或超时；4734672重跑。
4. 4734672已通过移动并进入后续目录清理回归，降级先被最新移动永久历史阻止，旧测试仍期待复制保护。更新最新保护预期，另独立执行复制Down guard，保留既有上传恢复与目录清理恢复guard；73fa4dd完整重跑通过。

四轮均由原句柄确认终态且清理完成后才开始下一轮；未因观察超时重开测试，也没有自动回滚线上系统。

## 本机私有证据

- `deploy/artifacts/native-status/dispatch-8d863fd1c6c14289a8b8524114dd2ec7.json`：首轮失败/准确清理。
- `dispatch-fda1d5f443ab4dc982f32e22e2f1a853.json`：第二轮失败/准确清理。
- `dispatch-243830e2f4e74098a488e06b33567a53.json`：第三轮失败/准确清理。
- `dispatch-48cff6895de64bb39fc3a926a7237a12.json`：最终Passed、executedTests=passedTests=1、cleanupConfirmed=true。
- `deploy/artifacts/container-move-persistence-73fa4dd/independent-cleanup.json`：独立准确资源、临时库0与核心不变核验。
- 本机日志 `/tmp/wd-container-move-persistence-native{,-second,-third,-fourth}.log`，数据库凭据与原始SQL失败输出不公开。

## 下一步

完成Vue候选及真实浏览器检查，冻结提交按正常dev覆盖发布，再以真实身份完成HTTP/SQL/Worker/UDS/Root/完整文件与页面整链、普通拒绝、取消/重启/Unknown实际故障验收。当前SQL通过不能替代后续门禁或整个P1/P2完成。
