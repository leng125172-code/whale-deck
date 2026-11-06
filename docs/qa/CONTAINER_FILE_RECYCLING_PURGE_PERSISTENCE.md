# 清理持久化与共享写保护候选

2026-11-06，持久化候选 `b2c4b9c0abb5d4a669ab84d2502e0f3a4462afad`、完整围栏验证 `a6e45e8c8ece1a32847e734171054cceebed93da` 接续[内部编排](CONTAINER_FILE_RECYCLING_PURGE_BUSINESS.md)。新增EF映射/迁移、真实PG仓储、两阶段事务与双向共享保护、Worker及安全Job/Outbox/审计接线，真实隔离PG门禁通过。生产未迁移/部署，管理员API/Vue及真人业务整链仍待。

## 模型、来源与永久约束

EF迁移 `20261106060735_ContainerRecyclePurgeIntents` 新建container_recycle_purge_intents，FK关联原回收意图及规划/执行Job。21个CHECK核验完整Originals命令、20属性类型化计划、七属性回执三态、阶段/期限/租约、请求与回执绑定、Reserved证明及独立Apply入口。source/current inode、归档root:root/0600/单链接/大小/SHA、HDD配置/目录身份等形状保留；SQL不存签名密钥，不用SECURITY DEFINER，业务MAC仍在应用层验证。

原来源行在41307341事务advisory lock下SELECT FOR UPDATE；Enqueue、Confirm、FinishPlanning、Started、Reserved及Applying重新检查来源完整MAC/资格、source证明hash与规范命令。原来源记录不覆写。已被恢复、尚有活动恢复或Root Unknown的来源不能清理；已有未完成、Root Unknown或已成功清理的来源由排他查询及条件唯一索引阻止再次取得清理资格。清理/恢复表间还有来源双向触发器，ACK释放全局业务保护不把未知来源重新当作可用原件。

请求/计划/执行/已保存预留/Apply入口及已知结果不可改写，永久意图不可删除；非空表拒绝降级。Unknown保持publish_barrier、null结果与未完成Job，排除正常Claim，只能明确管理员处置原执行ID。Canceled/Failed需要Aborted证明，Succeeded需要预留、不可逆入口和Completed证明；Root Reserved不能混入终态或解除保护。

## 两阶段事务与全部写入器

Started仅进入Reserving并保存RootStarted/全局保护；Reserved保存匹配回执及MAC；Applying再次检查原租约、取消/到期、锁内来源和预留证明后，才保存不可逆入口。90秒租约以原token围栏旧持有者；重新领取或Defer保留已有阶段和保护，原Dispatcher只观察/取消，不能再次执行删除。规划/执行Job、Outbox、事件、审计及状态/回执在同一事务提交。

15个SQL触发入口共用事务锁：宿主file_path_locks，容器上传、目录、复制、移动、普通权限/所有者、递归权限/所有者、ACL、递归ACL、递归ACL恢复、原回收、独立恢复和本清理。跨容器宿主别名无法从SQL可靠判断时，采用保守全局发布保护。所有既有仓储加入清理barrier、同容器重叠路径及全局/actor预算；恢复来源目录排除已占用/消费的清理来源。

候选DI已注册保护器/仓储/来源解析/Workflow/Dispatcher，Worker有独立轮询，通用Outbox排除两个清理消息；通用Job取消转专用消息，通用retry拒绝重做清理。Job结果目前只落最小安全状态，不声称管理员任务页已适配。现有生产Worker仍旧版，没有运行本批后台任务。

## 真实隔离PG验收

标准原生dispatch宏门禁使用两个自有临时库、非superuser应用角色、真实EF迁移/SQL/事务/行锁。原回收来源由真实仓储产生，身份/计划/Root回执和时钟为明确夹具；不调用Agent/Authentik或读取实际文件/HDD，不算真人页面证据。

首轮b2c4b9c0已执行并通过，扩展a6e45e8c另跑完整双向peer测试和Finish审计失败回滚，两个修订均单独保存。实际检查21约束/15围栏；确认前错version拒绝，另一连接持原来源行锁时Confirm/Applying超时且无新入口提交；Applying不得先于Reserved证明；逐表peer双方保护、宿主锁拒绝，原源/取消/Unknown/成功消费资格与不同管理员ACK、原租约失效后旧Finish拒绝通过。完成后原来源全部字段保持。

审计注入失败分别使Enqueue和Finish回滚：无新意图/Job落地，删除结果/终态未提交、既有Applying/保护保持；撤掉故障后同原执行可提交已验证结果。永久意图删除/来源证明篡改被拒，Unknown保持null事实与保护，非空迁移降级拒绝。两轮各有1个宏测试实际执行/通过、零跳过，要求包含本清理phase marker；临时数据库及SDK容器清理确认，外部通知投递0。旧恢复降级检查改为执行其实际迁移的首个Down guard，避免引入新后继迁移后先降掉空表再测旧guard。

本地5616 Unit通过，742 Integration通过（7原生依赖默认跳过，749总）；两项模型/迁移一致性检查、8范围、14前端CI合同通过。默认跳过不冒充上述实际PG门禁。已有10.0.4 EF工具指定本机10.0.402运行环境后使用，未安装新工具；不支持的--no-connect参数已去掉，生成索引数组CA1861改为static readonly，无分析器抑制。状态保存脚本一度误把工具Passed当Succeeded，按真实状态契约修正，原生回执未改写；未保存原错误日志没有补造。

## 保持事实与封存

生产前/中/后实际读取完全保持：两宿主857e018d、运行四容器/checkout093f44a9、2346保护文件、11核心、36回收22恢复完整业务行及活动0，Root75/HDD83、恢复Root59、目录Root98/purges Root4保持。真实生产PG仍没有container_recycle_purge_intents表，无新migration、身份变更、文件RPC或发布。维护连续性原发现继续保留，本批没有做宿主重启。

私有证据 `deploy/artifacts/container-recycle-purge-persistence-7410162bf78a45eea2b7420edac05a72` 共62文件（另有清单），45份变更源码按最终准确提交在测试后保存并核对，包含两轮原生状态/源码/清理回执及生产保持。清单SHA256 `6e5623964ae10666a117addaff72eb752e3f53302a7c812b71aaa41fd498b067`；目录root:root/0700、文件root:root/0600，临时连接环境和凭据未封存。

继续管理员API/来源选择/明确确认、Vue与Jobs完整投影、准确CI/部署和真人文件/HDD整链，再适用故障矩阵、维护入口连续性、长期留存策略/归档恢复及其余59项。本批后续提交CI终态另查；整项0/59、阶段0/6、AI子计划0%保持，17项移出不算完成。
