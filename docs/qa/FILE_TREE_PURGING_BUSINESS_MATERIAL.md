# 目录清理业务材料候选（2026-10-21）

状态：active、未部署。已装 Agent/MaintenanceHost 仍为 56101c8/1.36，API/Worker/Gateway 为 2c8d1bb；[前序安装态子门禁](FILE_TREE_PURGING_INSTALLED.md)不等于本批业务验收。完整 P0–P5 的 76 项及文件管理仍未完成。

## 本批实现

- 独立 Domain 操作材料、Application 模型/校验/服务/调度器和 Infrastructure AgentGateway/签名器。不是新增一个直接删文件的 Controller。HTTP 命令只含 `CaptureId` 与 `Scope`；历史路径、版本、owner、类型、清单和目录登记证明由后端验证 immutable catalogue 后解析，不使用当前用户替换历史 owner，也不依赖原账号仍存在。
- Trash 沿用原签名载荷；Overwrite 验证独立类型/内容证明。计划与目录登记的 entry/bytes/archive SHA/内容域再次绑定，catalogue proof 只进入业务签名，不伪装为 Agent 已证明的 SQL 字段。原捕获不被本候选修改或删除。
- Archive/Originals/All 严格范围、18 字段计划、uint64 身份、16 GiB 原件/17 GiB 归档及 10,000 项边界；命令与计划 JSON 拒绝重复/未知字段。Archive 的四个原件证明必须为空，不放宽普通文件 1 GiB 限制。
- 请求、Ready 计划、观察回执及确认使用独立 HMAC purpose。当前 actor/login/ID/时间、捕获/范围和完整证明均受绑定；现有捕获锁 token 直接复用恢复流程，计划/执行 ID 则采用新的独立用途，不能借旧恢复确认或跨组件确认。
- 服务每个入口先查当前管理员。确认前检查本人登录、capture/scope/fingerprint/expiry、取消和版本；Repository 的确认事务仍必须重验 TTL/版本并获取捕获及层级锁，本批没有用服务层预检替代该事务。
- 调度算法在计划和真正 Apply 前重验管理员/时限/取消，每 6 秒续租并重验；关闭或丢租只取消本次调用，不写假终态。RootStarted 后丢回执先 Inspect/Discard，绝不直接重新 Apply。已观察的 Root 终态回执不可增加、丢失或替换任何事实，业务状态不能把 Completed 标 Canceled、把 Unknown 标 Failed，或把丢回执签成终态。
- Gateway 要求 major1/minor≥36 和清理专用能力；Plan 不接受替换命令，Apply/Discard/Inspect 分别签名，仅 NotFound 映射“无回执”。独立异常不伪装成缺失。
- 面向后续页面的 DTO 不暴露历史 owner/内部证明；三种范围有不可撤销/可能最后副本警告。Unknown 中已选组件的 false 标记只是“尚未建立移除证明”，DTO 返回 null 而不是“未删除”；已有正面移除证明保留 true，未选组件才可明确 false。

## 验证与已知失败

- 最终 Release 零警告/错误，2124 Unit / 146 Integration 通过；6 项本机原生依赖测试明确跳过。相比前序 1856 Unit 新增 268 项纯用例，包括 protobuf/内存 HTTP 帧签名、材料/catalogue/确认篡改、权限/租约/取消、假时钟和 Unknown 三态。这不是实际 PostgreSQL/Worker/真人权限或本批实际 UDS 证据。
- 首轮独立 Gateway 测试桩有 `AsyncClientStreamingCall` 泛型参数编译错误，已修正测试桩，未放宽断言。早期 318 项清理筛选通过与后续 2100/2114/2120 全套结果只代表各自源码批次，不冒充最终版本。
- 最后四个 Unknown 三态测试加入后，全套复现旧 `OperationStoreTests.ConcurrentMaintenanceReadsObserveOnlyCompletePublishedSnapshots` 的 Windows FileNotFound（2123 通过/1 失败）。同实例的 `File.Exists` 与打开文件未和快照发布使用同一锁；候选中令该读取与该实例的写入协调，保留 FileShare.Delete 和原测试的 100 次双线程 barrier/完整快照断言，没有捕获异常并伪造 Ready，也没有删测试。
- 修复后原并发测试独立运行 10 次全通过（共 1,000 轮发布/读取），随后完整 2124/146 再通过。该 Agent 读取修复尚未安装，不宣称多进程 Windows 或实际 Linux 运行态的同一故障已验证。
- 前序 56101c8、6c60ea3、257c0f9 对应 CI `39486648857`、`39489338136`、`39491660546` 已分别四路 success；本批新提交 CI 必须独立观察，不继承这些结果。
- 当前 11 核心 healthy、两宿主服务 active、工作台 HTTP 200；本批没有安装/覆盖服务或镜像、创建生产清理任务或清理生产数据。
- GLM 5.3 Flash 只读提取旧接口命名；协作代理只补独立纯测试。实现、高权限判断、失败诊断及最后验证由 GPT 负责。

## 尚未实现与下一步

本批 Domain 类型**未注册到 EF 模型**，没有 schema 修改或 migration，不声称已创建永久任务或完成数据库墓碑。下一步实现 PostgreSQL 仓储、迁移、单次确认/Job/Outbox/捕获与层级锁、事务审计及按组件的永久清理事实/不确定状态，保留原 immutable catalogue，并让历史及两种恢复消费者尊重清理状态。再接 Worker/DI/API、第11类当前管理员跨账号 Unknown 处置、原生 PostgreSQL 验收和明暗窄屏 WebUI。

当前同登录 Acknowledge 服务不替代上述跨账号接管。孤儿/Unknown 私有对象、长期元数据策略、容器文件、异盘及更广安装态中断，以及完整76项继续，不因本候选纯测试通过签收整项。
