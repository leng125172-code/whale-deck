# 递归 ACL 持久化与共享写入保护候选

2026-11-04，冻结源码`224f57cff5f000f5c9e5c9ca6a8963cb7d271259`，承接[内部编排](CONTAINER_RECURSIVE_ACL_BUSINESS.md)及[安装态签名RPC/HDD](CONTAINER_RECURSIVE_ACL_AGENT.md)。本批真实隔离PostgreSQL门禁已通过；**未升级生产库、未部署新Worker/公开API/Vue、未重放Agent文件操作或取得新真人页面证据**。59项主任务及阶段不升完成。

## 实现与实际数据库验证

新增EF映射和`20261103235841_ContainerRecursiveAclIntents`迁移、真实Repository/结果写入、DI及每秒分发Worker注册；通用outbox处理排除本类型，请求取消路由到专用消息。意图与planning/execution两个Job、outbox、事件和脱敏审计共用同一事务。Claim采用SKIP LOCKED、90秒租约与原token校验；Started永久标记、版本/期限/取消复核、先查原执行及Partial/Unknown保持保护等接上原编排。回执JSON、独立ReceiptHash、计数/历史/结果列原子保存，明确处置时先核验原有事实再保存新观察，不能抹除已知结果。

新增表具18个CHECK、双Job外键Restrict、并发版本及唯一执行/租约/屏障索引。复用既有ACL条目函数，新增command/plan/receipt形状校验：两组目标的null/空数组语义、ID0与完整条目、mask/顺序/重复、数值类型/范围、原命令、目录身份/模式/时间/64位精度、manifest及计数、aclType/回执绑定和MAC必备。不可变触发器保留原请求、计划、执行、Started、历史/结果及终态，禁止直接删除；回执正文或MAC单独变化拒绝，同时伪造两者仍须通过应用MAC验证。数据库不持有HMAC密钥，不能把SQL格式约束称为数据库已自行验证密钥签名。

共同advisory事务锁41307341覆盖新仓储与所有旧写入。9类旧容器publisher（upload、directory、copy、move、chmod、chown、递归chmod、递归chown、单项ACL）和宿主FilePathLocks均增加反向保护；新仓储也检查所有旧屏障。11个新fence触发器包含这10个旧入口及新表自身，防止直接SQL绕过应用检查。所有路径预留、祖先/后代冲突与全局/用户活动预算同步纳入递归ACL；copy/move检查source与target。新类型自身用PublishBarrier，不借用宿主路径锁。Down先拒绝非空永久意图，再准确清理挂在旧表上的触发器、新表和独立函数，旧功能函数保持。

真实Native dispatch门禁在两个新建`wdv_dispatch_`隔离数据库执行，源码只读Git archive、固定实际SDK image ID、测试容器无Docker/Agent socket、只读root与有界资源。连接仅留内存/临时0600文件，不打印或入Git；实际业务DB角色非superuser且schema=public。完整旧文件/通知分项回归后，新增递归ACL边界实际执行，并由runner要求专属完成标记，不能以跳过或只迁移成功代替。

| 实际验证 | 结果及边界 |
| --- | --- |
| 空表Down后重新Up、18CHECK与模型无漂移 | 真实隔离库通过；非空表Down明确拒绝 |
| 双Job/规划与确认、幂等、版本、期限/取消、并发确认及claim | 真实Repository/事务通过，原执行ID不变 |
| 同路径/祖先/后代预留与跨类型预算 | 实际SQL及仓储拒绝通过 |
| 9类旧容器publisher+宿主路径锁双向屏障 | 原生SQL拒绝与新Started Busy通过；原记录保留 |
| default写入/清除、named ID0、缺目标/错误null | 正常持久化或CHECK拒绝通过；未执行文件ACL写入 |
| Partial/Unknown、跨管理员明确处置、历史可用性下降 | 原计数/历史保留、回执/MAC一致、原版本释放保护 |
| 单改MAC/正文及同时伪造正文/MAC | 前两种数据库触发器拒绝；后者规范正文可读取但应用MAC校验拒绝 |
| 租约重领、永久Started、Unknown与审计失败 | 真实数据库条件/原子回滚通过；测试时钟推进91秒，不当作真实Worker进程重启 |

旧peer的原生SQL克隆仅用于检查数据库fence，不作为通过业务MAC的正常意图；所有克隆在自有显式事务回滚。Agent回执、身份、ACL目标和时间推进为明确测试夹具，因此不是新真实Authentik、Root/HDD捕获或页面链路。真实DB内的永久记录留到runner最终删除其两个自有测试库，不删除生产意图来通过门禁。SDK容器/两库清理命令及cleanupConfirmed均成功，外部通知投递0。

## 测试、来源与失败记录

完整Release构建0警告/0错误；4649单元、491默认集成通过，7项默认原生跳过；新模型检查包含在491内。另执行真实PostgreSQL native dispatch：实际1个宏门禁executed=passed=1、未跳过，包含全部旧分项和新增递归ACL分项，不能将内部断言数冒充新增独立测试项。8范围基线、3原生状态工具检查、Python语法及diff检查通过。没有改动Linux helper或前端，本批不冒称重跑506个root文件或全量前端检查。

候选34个改变源码/测试文件SHA与冻结提交核对；随后干净提交再构建，Agent/MaintenanceHost/Worker三个实际CLI --build-info均退出0并返回224f57c，未启动新候选服务。原生门禁同样记录准确224f57c来源及SDK image ID。

初始dotnet-ef不在PATH，找到既有10.0.4工具并设置既有.NET10.0.402根目录，无安装新工具。首次迁移插入脚本对生成代码缩进的断言失败，尚未改写文件；修正定位后补齐SQL。生成迁移触发IDE0161及3个CA1861，改文件范围namespace/集合表达式；后加测试与复制夹具重名触发CS0128，去重复声明并只针对新迁移验证Down。上述修正后完整构建/回归通过，真实PG门禁首次执行即通过；不把前置编译失败后的旧--no-build模型结果当作最终证据。

私有证据`deploy/artifacts/recursive-acl-persistence-32693e84de7d4cf9beedd760625c4b57/`保留源码SHA、TRX/构建日志、候选CLI、原生unit实际InvocationID/日志/状态副本、运行前后快照。原生状态原件`deploy/artifacts/native-status/dispatch-37d37468793a4e81984e6bef36ec4e72.json`为Passed/cleanupConfirmed，含专属阶段标记；runner已清理临时TRX及隔离数据库，不能声称这些临时原件仍在。证据均私有，不入Git。

## 运行环境与后续门槛

Native运行前后直接比较：11核心ID/镜像/StartedAt不变且healthy，Agent2799620/维护2799617实际仍7cb3935，四产品容器/完整发布账本仍28062da。7受保护配置与账本hash/权限/所有者保持，全部153普通ACL及58递归ACL元数据字节和九字段保持，活动0。生产库实际检查新表不存在；没有生产迁移/发布、宿主重启或自动回滚。root只读Git未改变索引，所有者仍1000:1000；NOPASSWD策略保持。

下一批完成管理员公开API/严格输入与Vue预检、确认、任务结果/恢复处置，再准确CI、标准覆盖及真实Authentik→Vue→API/Worker→Agent→文件/HDD整链。Worker/DI虽已接代码，本批只验证仓储/分发契约和构建，不签收新生产Worker循环。安装态完整故障矩阵、递归ACL历史恢复、可恢复删除/长期清理、维护入口零中断和其它59项继续。[当前目标](../GOAL_P0_P5.md)。

后续接续：管理员API/Vue与通用重试保护已在c020c24完成候选验证，见[新页面批次与未验边界](CONTAINER_RECURSIVE_ACL_API_UI.md)。本报告224f57c的数据库/运行证据和当时缺口保持原样，不能据后续候选补签生产迁移或真人整链。
