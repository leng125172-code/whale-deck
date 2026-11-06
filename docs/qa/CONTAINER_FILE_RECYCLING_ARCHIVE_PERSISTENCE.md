# 归档复制持久化与共享保护候选

2026-11-07，接续[内部编排候选](CONTAINER_FILE_RECYCLING_ARCHIVE_BUSINESS.md)。初版f5a54c88、修正343d68b2新增EF迁移20261106175807、真实仓储、共享写入保护与Worker接线。修正后的第二批真实隔离PG已通过且临时资源清理确认；这只签持久化分项。生产未迁移或部署，管理员API/Vue/Jobs结果摘要和真实身份页面整链尚未完成。

## 已实现的持久化边界

21项CHECK约束、不可改写请求/计划/执行/Reserved/复制事实和终态触发器，保留nullable ArchiveCopied、BindingCurrent及完整新inode回执。来源外键指向成功清理意图；事务内先锁清理记录、再锁准确原回收记录，重验两层MAC和来源proof。成功复制、未解决Unknown或活动来源保持唯一消费保护，不将旧回收/清理记录覆写成恢复结果。

共享advisory锁41307341下，归档和所有旧文件写入器均有双向围栏。新增16个全局写入触发入口，包含宿主file_path_locks、容器上传/目录/复制/移动/权限/所有者/递归权限/递归所有者/ACL/递归ACL/递归ACL恢复/回收/恢复/清理/归档；14类旧路径集合及全局/单actor预算增加归档。当前发布围栏全局串行保护，覆盖跨容器宿主路径别名。

意图、规划/执行Job、Outbox、事件和审计同事务。RootStarted、Reserved证明和ApplyStarted分别持久化，原租约续期/提交均被校验；重领只沿原ID观察/取消，不复做复制。Unknown屏障保留，版本化ACK同时写当前管理员审计、原结果、完成时间并释放业务保护。降级在非空归档表时拒绝，历史数据不静默删除。

专用Worker每秒尝试领取，异常仅每分钟一次固定告警；通用Worker排除归档消息，Jobs取消路由到归档取消消息。公开任务结果投影尚未实现，本批只写既有安全元信息/事件/审计，不用空摘要冒充完成。

## 当前检查与原失败

模型/迁移一致性检查通过；Release构建零警告错误。修正后5875单元零跳过，783默认集成通过、7默认原生跳过，8范围/14前端CI合同通过。真实PG门禁使用独立新数据库和受限SDK，只使用合成身份/计划/回执，不调用Agent或Root，不证明真实身份页面或HDD业务链。

首批f5a54c88实际运行失败于合成清理竞争者克隆：已清空receipt/root_state，但保留Completed种子的SourceFenced/OwnedFence，被原ck_container_recycle_purge_receipt拒绝。SQLSTATE23514/源码位置和准确提交已保存，原约束不放宽；临时数据库及容器清理确认成功。修正清空两字段，并补通用Worker排除和Jobs取消路由，第二批使用全新资源通过，未重用首批数据库或容器。

准备期重复if、移除未实现结果摘要时漏删紧凑调用、迁移namespace风格IDE0161及固定表名拼接EF1003失败均保留为事实；修正调用和固定SQL字符串，不禁用分析器。原始未保存输出不补造。

## 真实PG结果与封存

第二批unit `whaledeck-qa-archive-pg-343d68b2`，Invocation `9aa9cfae3188475cb2a0b96001dd8561`，准确源码 `343d68b273c8b4008c5397de66bb8a274d6400a3`，exit0/success，445.867秒。标准原生状态明确1执行/1通过、103个阶段标记、新归档专属标记及cleanupConfirmed=true，外部投递0；两个临时数据库和SDK容器已清理。已验证确认/Apply前清理与原回收两种行锁均真实阻塞并按3秒lock_timeout拒绝，14类旧容器写入peer双向围栏、宿主锁拒绝；迁移亦安装归档表自身唯一索引/触发器。同时通过Reserved/Apply两入口、旧租约拒绝、nullable Unknown/跨管理员ACK、历史Completed绑定false、来源MAC失配、成功来源不可重复消费、Jobs取消类型及Outbox处理、Enqueue/Finish审计注入失败时事务回滚、非空降级拒绝和原历史不改写等检查。

首批unit `whaledeck-qa-archive-pg-f5a54c88` / Invocation `433b132ac82343019353eea2338f335d`，失败及cleanupConfirmed=true原样保留，不记通过。标准runner不保存原始私有SQL输出；保留实际清洗日志、SQLSTATE/约束/源码位置和准确失败源码，不补造原始输出。

第二批运行中与结束后两次重新查询生产53回收/29恢复/17清理完整业务行SHA256一致，活动0；归档生产表仍不存在。两个宿主PID1244695/1244692和运行checkout9694af1c保持；此证据不冒充完整Root/HDD或维护连续性复验。上批e4744ec6准确CI42264843870已completed/success、12路全成功；本批远端CI另查，不沿用旧绿灯。

私有证据 `deploy/artifacts/container-recycle-archive-persistence-e22ed3ac2b5147d7865bad98e23915a0` 共122文件（另加清单），含首批45/最终47源码、默认测试/构建/模型检查、真实原生终态/清理、失败事实与生产保持。清单SHA256 `1f5bc8eb8c61f1321c935cca7cb8320ba35e8429f2ea6cdf05b93813769eb1cb`；root:root目录0700、文件0600，最终源码逐字节等于工作树/Git。未把私有证据、凭据或内部端点上传展示站。

## 续跑点

继续来源目录与管理员API/Vue/Jobs、标准部署、真实身份和文件/HDD整链及适用故障矩阵。claim/暂存清理、留存策略、维护连续性和其余59项继续；整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
