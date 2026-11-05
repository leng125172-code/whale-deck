# 独立回收恢复持久化与共享保护候选

2026-11-06，产品候选a222316f8807223bfef1759664aa9298bc958009，最终原生验证工具0714a7b706acd9b9e6b9766c4dc3e4c42edc9685。承接[内部编排](CONTAINER_FILE_RECYCLING_RESTORE_BUSINESS.md)，新增EF Repository/迁移、原来源事务锁、双向共享保护及Worker接线；真实隔离PG验收已通过，生产未迁移/部署，管理员API/Vue和真实身份文件/HDD整链仍待。

## 永久记录与锁内来源证明

新增container_recycle_restore_intents，EF迁移20261105184338。以FK保留原ContainerRecycleIntent、新规划Job与执行Job，SourceIntentId/SourceProofHash和完整命令、request/plan/execution/receipt哈希不可变；nullable源状态/保留/恢复/本操作归属事实独立保存。19个CHECK、索引及更新/删除guard保护完整形状、阶段/时间、执行ID、新旧来源、历史摘要、回执presence/绑定和Unknown处置；不允许删除永久意图或清空既有事实，非空表禁止降级。

Enqueue、Confirm、FinishPlanning及Started均在既有41307341事务advisory lock内SELECT原来源FOR UPDATE，调用同一FromRow验证原业务MAC/资格，重算source证明并比较完整规范命令；确认和开始执行不只依赖事务前的读取。规划/执行Job、Outbox、审计、事件、回执与状态同事务提交，租约token及90秒期限围栏旧持有者。Defer保留Started与发布保护，不重做Apply；Unknown保持保护/null事实，只有明确管理员处置可解业务保护，原来源意图不改写。

数据库MAC仍由Application/Infrastructure验证，CHECK只负责格式及交叉事实，不在SQL放置签名密钥或SECURITY DEFINER函数。新计划及回执形状函数复用旧回收的精确整数/原计划校验，完整来源、留存身份、HDD摘要与回执归属均绑定。

## 双向写保护与运行时接线

新增SQL围栏覆盖14个入口：宿主file_path_locks、上传、目录、复制、移动、普通权限/所有者、递归权限/所有者、ACL、递归ACL、递归ACL恢复、原回收和新独立恢复。所有围栏共用同一事务锁；其他容器即使路径无法在SQL解析为同一宿主位置，也受保守的全局发布保护。既有各Repository同步加入恢复barrier、同容器路径重叠与全局/actor活动预算检查，恢复侧对全部既有writer同样排他。

DI已接新保护器/Repository/来源解析/Workflow/Dispatcher，Worker有专用循环并排除通用Outbox消费，通用Job重试拒绝这两个新Job类型，通用取消路由到专属消息。持久Job只存最小安全结果；完整Jobs投影/UI适配仍属后续，不把摘要落库当页面验收。接线是源码候选，现有生产Worker仍旧版，没有后台新写入。

## 真实隔离PG检查

使用现有冻结Git archive/固定SDK身份的标准dispatch原生宏门禁：两份独立临时数据库、非superuser应用角色、真实EF迁移/SQL/事务/行锁/MAC/触发器执行。来源、身份、计划、Root回执和时钟偏移是明确夹具；不调用实际Agent或Authentik，不读取真实文件内容，不算Root/HDD/真人UI验收。

实际检查19约束/14围栏；来源由真实永久原回收意图产生且最后各字段保持原值；确认前旧version拒绝，另一连接持有原来源行锁时Confirm与Started均超时拒绝且无新执行/保护提交；双方writer围栏、宿主锁与busy不Started通过。Unknown保存null事实和保护，直接篡改source证明、归属及删除被拒，另一管理员ACK保留原事实；规划取消和执行前Aborted取消、伪造但新request-MAC自洽的source证明拒绝、租约重新取得后旧回执拒绝、成功归属/原来源不变及非空降级拒绝通过。注入审计失败时，Enqueue和Finish各自的Job/意图/Outbox/回执状态全部回滚；无新未处理专属Outbox或活动恢复意图残留。

最终unit whaledeck-qa-recycle-restore-pg-executed-f9f333c8，Invocationfbe6b65370c746918a245525e63d7df2，准确exited/Result=success/ExecMainStatus=0；原生宏1执行1通过零跳过，并明确包含新恢复阶段marker。两临时数据库/SDK容器清理确认，外部投递0。源码0714a7b7不与前序失败轮混用。

## 失败及修正保留

首个编译因复制的配置方法名重复CS0111失败；生成迁移IDE0161/CA1861改为文件作用域namespace与静态索引列数组；固定表种子读取EF1003改为明确SQL switch，无分析器抑制。迁移模型及HasPendingModelChanges检查通过。

前三轮实际PG均保留failed及cleanupConfirmed=true：a222316f阶段复用上传/目录种子但它们已被前阶段清理，Single无元素；3a60e2fd自建种子后使用了无效Publishing/Creating状态，被23514拒绝；dcb4ad7c改为合法Ready/Pending后选中了“仅预检取消、无执行”的所有者种子，被ck_container_ownership_execution拒绝。最终统一为有执行ID/计划的意图种子，并只对本阶段自有上传/目录使用其合法未执行种子。未放宽任何生产约束；诊断只增加约束名/固定表名，不输出SQL参数/连接凭据。旧失败不补签通过。

本地5430 Unit零失败零跳过、706 Integration通过/7原生依赖默认跳过（713总）、8范围通过；Release零警告错误。上述默认跳过不冒充真实PG，新原生宏单独给出通过回执。现有10.0.4 EF工具按现有10.0.402根目录使用，未安装新工具。

## 生产保持与证据

实际生产前、中、后只读快照逐值保持：2346保护文件、11核心healthy、回收Root32/HDD35、新恢复Root5、目录Root96、既有数据库与宿主PID/build不变。生产恢复业务表查询仍为空；两宿主f333a69a/Agent1.57，四容器与运行checkout仍58b8174，无生产迁移、覆盖发布、身份变更或新文件RPC。

证据deploy/artifacts/container-recycle-restore-persistence-f9f333c81fd94dfd9e001bd2c4b8f810，共73文件/43变更源码，EVIDENCE_SHA256.json摘要6742339a792c0ebb6e8b7edd3176dee0289343048c22eaaa7814691b7d5c343e，所有文件逐一重算通过。包含三轮失败、成功状态/源码身份与清理回执、本地检查和生产保持记录；连接凭据与临时环境文件未封存。

继续管理员API/安全确认与恢复列表、Vue/Jobs投影、准确CI和标准部署，再真实身份/原文件/HDD整链及完整故障矩阵、长期清理和其余59项。当前完整主项0/59、阶段0/6、25%–35%工作量粗估及AI子计划0%保持，17项移出不算完成。

项目图谱whale-deck/container-recycle已按本批稳定eventId回执同步active，pending移入synced；整项及阶段未标完成。

2026-11-06后续：[管理员API/Vue候选](CONTAINER_FILE_RECYCLING_RESTORE_API_UI.md)已形成并通过协议/预览及真实PG目录查询检查；本记录原文按当时边界保留，不转签生产部署或真实身份文件整链。
