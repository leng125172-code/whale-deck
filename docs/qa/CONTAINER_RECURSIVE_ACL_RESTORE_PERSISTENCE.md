# 递归 ACL 历史恢复持久化与共享写入保护

2026-11-05，候选源码 `f7d6a6810c83ea60f1464056a327ea4b9b1e9f78`，承接[内部业务](CONTAINER_RECURSIVE_ACL_RESTORE_BUSINESS.md)与[安装态签名RPC分项](CONTAINER_RECURSIVE_ACL_RESTORE_AGENT.md)。本批补永久仓储、EF迁移、共享保护和Worker接线；**未发布生产迁移或新Worker，未新增公开API/Vue或真实身份页面整链，不提升59项完整完成率**。

## 实现

新增 `ContainerRecursiveAclRestoreRepository`、EF映射与 `20261104182233_ContainerRecursiveAclRestoreIntents`。规划意图、planning/execution双Job、事件、outbox和脱敏审计在同一事务提交；确定性执行ID、并发确认、原确认幂等、版本/取消/期限复核、SKIP LOCKED与90秒租约、永久Started、回执正文/独立MAC和五计数/历史事实保存接上原编排。Partial/Unknown保持屏障和原事实，只能由当前管理员明确处置，不自动重做；数据库不持有业务HMAC密钥，格式约束不冒称数据库自行验签。

18个CHECK、双Job Restrict外键、Version并发字段、唯一执行/租约/屏障索引；SQL分别约束4字段恢复请求、18字段计划、10字段来源摘要、9字段Root来源文件快照、恢复目标摘要和19字段回执。回执类型绑定 `RestoreSource.ChangedAclType`，恢复请求没有普通ACL的目标条目字段。来源capture/digest、计数、access/default、PID/合法日期和Root文件0600/uid0/gid0/单硬链/大小/64位时间均做独立校验。不可变触发器保留请求、计划、原执行、Started、历史/结果与终态，禁止删除永久意图或单改回执/MAC；正文和MAC同时伪造仍由应用MAC拒绝。

共享事务锁41307341下，旧十类容器写入（上传、新目录、复制、移动、普通权限、所有者、递归权限、递归所有者、单项ACL、递归ACL）和宿主路径锁都纳入双向保护。所有旧仓储的路径预留及Started检查增加恢复意图，新仓储也检查所有旧家族；copy/move同时检查source/target，活动预算同步计入。新增12个SQL发布围栏触发器覆盖旧十表、宿主锁及新表自身，另有1个不可变保护触发器；Down先拒绝非空永久表，再准确移除新增触发器/表/函数，保留旧围栏。

DI注册真实仓储、protector、workflow和dispatcher，Worker每秒分发、异常警告限频；通用outbox排除专用消息，取消路由到本类型，通用重试拒绝原恢复写入并要求重新规划和明确确认。构建/仓储测试不等于新生产Worker循环已验收。

## 本批验证与证据边界

最终Release构建0警告/0错误；5048单元测试（含两类恢复Job通用重试拒绝）与613默认集成测试通过，7项原生默认跳过。8范围基线及3原生状态工具测试通过。冻结后Agent、MaintenanceHost、Worker三个实际CLI的 `--build-info` 均退出0且返回f7d6a68；38个变更源码/测试文件SHA已保存并逐一与冻结提交/工作树核对；720个关联Markdown本地链接存在，既有目标/状态/业务报告正文保留校验通过。

真实PostgreSQL native dispatch首次执行通过：executed=passed=1，无跳过，新增专属分项完成标记实际出现，全部旧分项同批回归。状态原件 `deploy/artifacts/native-status/dispatch-a3e310f89c0645c788a789db5facf2b4.json` 为Passed/cleanupConfirmed=true；两个自有隔离库、SDK容器和scratch清理成功，外部投递0。宏门禁内部断言不另冒充独立测试数量，临时TRX已随scratch清理，不声称仍有该临时原件。

门禁设计使用两个新建 `wdv_dispatch_` 隔离库、非superuser数据库角色、public schema、只读Git archive和固定实际SDK image ID。SDK只有公开源码/自有scratch挂载、无Docker/Agent socket、只读root、drop ALL和资源上限；本轮工具以root运行，SDK的显式UID继承runner为0，不将此测试容器当作产品Agent最小授权证明。连接只留内存/临时0600文件，不入仓库或日志，未调用外部通知目标。

新增专属分项覆盖空表Down/Up、18CHECK/模型一致、并发确认、双Job/审计原子事务、同路径及祖先后代预留、跨类型预算、十类旧publisher及宿主锁双向SQL围栏、来源/frame/目标摘要异常、access/default回执、Partial/Unknown保留与跨管理员处置、租约重领、原Started不可清除、取消专用消息、伪造回执MAC和非空Down拒绝。原生runner必须看到新增专属完成标记，不能静默跳过此分项。

Agent回执、来源内容、身份与时间推进是显式测试夹具；真实的是数据库、迁移、仓储、事务和SQL拒绝，不是新Root/HDD写入、Authentik撤权或Vue页面。原生SQLpeer克隆只验证围栏，均在自有事务回滚；永久意图留至runner删除其两个隔离库。

## 失败及修复记录

既有dotnet-ef 10.0.4首次未设置DOTNET_ROOT导致apphost找不到.NET；设置现有10.0.402根目录后迁移生成成功，未安装新工具。EF生成迁移触发IDE0161及3个CA1861，改为文件范围namespace与集合表达式后构建通过。只读复核发现来源实例SQL原规则接受PID1、超长PID及无效日期，已收紧并补真实SQL反例。真实PG首次执行成功，无失败后删除永久数据重跑。首次从仓库根执行原生状态Python单测缺少模块搜索路径（ModuleNotFoundError），设置PYTHONPATH后11项通过；不记为产品故障。最终证据汇总首次误将TRX的notExecuted字段当作xUnit跳过数，改用total-executed并核对结果后，5048/613及7默认跳过一致；测试本身未失败。

私有证据目录 `deploy/artifacts/recursive-acl-restore-persistence-20261105/` 保存TRX、冻结构建日志、38源码SHA、候选CLI与运行前快照，不入Git。真实门禁unit为 `whaledeck-qa-recaclrestore-pg-f7d6a68`，InvocationID `1f7aeb6d04db484c8a3442dd75a65d33`；原unit已成功结束，journal中InvocationID与原启动回执一致，Passed状态和专属阶段标记已归档。瞬时unit卸载后systemctl无原InvocationID，不拿空值补造ExecMainCode；以原journal及状态回执核验。

## 运行与下一步

运行前后直接比较：1434受保护配置/元数据、来源Root1052、恢复Root32/HDD46和目录Root94的字节hash及九字段均保持，11核心ID/镜像/StartedAt/健康和两宿主PID/版本保持；宿主仍e066ec1（Agent1080518、Maintenance1080514）。生产SQL只读确认新恢复表仍不存在，旧递归ACL38意图/35执行/73Job、活动0保持。本批没有部署、宿主重启或重复真实恢复操作。

下一批接管理员公开API、严格请求输入、Vue来源/异质目标预检、确认/取消、任务结果及Partial/Unknown处置，再准确CI、标准覆盖与真实Authentik→Vue→API/Worker→Agent→实际文件/HDD整链。完整恢复故障矩阵、可恢复删除/长期清理、维护入口连续性及其余59项目标仍需推进；前批67维护样本4次非200及来源/HDD同device2049的限制保留，不能由本批数据库通过消除。

远端同步：功能候选f7d6a68已推送origin/dev；首次GnuTLS中断、直连低速超时，单次HTTP/1.1连接重试成功，未改全局代理/TLS或强推。项目图谱首次传输失败后，以同一事件 `whale-deck.container-acl.f7d6a68.recursive-restore-persistence` 重试获匹配成功回执，pending移入synced，阶段仍active。CI终态、生产部署及真人页面不由推送成功代签。


后续接续：管理员API/Vue候选0034671及范围元数据修正7906e45已完成接口/页面候选验证，见[API与Vue批次](CONTAINER_RECURSIVE_ACL_RESTORE_API_UI.md)。本报告f7d6a68真实PG和当时未部署事实保持，不据后续候选补签生产迁移或真人恢复整链。
