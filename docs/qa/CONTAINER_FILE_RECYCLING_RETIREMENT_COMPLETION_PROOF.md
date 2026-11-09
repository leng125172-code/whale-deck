# 实际退役之后的完成证据关联候选

2026-11-09，提交 `1fd747897e490fd823028d0a5f0c548514bf660e`。新增纯函数 `completion_acceptance_proof`，不执行、不修复、不删除。它为之后的真实退役验收补齐证据关联，不能据本批测试宣称自然到期或生产 HDD 已退役。

## 校验契约

调用者必须另行提供独立取得的执行前完整快照，分别绑定原 request、Root树、完整HDD树、保护树和副本命名空间；函数不证明这些快照的来源或授权本身。它拒绝只把前后树一起截短来凑“保持”的报告。真正的物理采集器与执行放行仍须独立完成。

必须具备同一原请求的新鲜 ReadyForExplicitAcceptance 诊断及当前完整计划；时钟是严格整数，已到期且未跨越模糊边界，执行时间窗包含证书提交时间。就绪诊断仍不授予删除权。随后要求：

- 原 v1 Reserved 日志精确成为同请求 v2 Completed；原 Reserved marker 精确成为 Retired，owner/操作ID/原归档/计划指纹一致。
- 证书 executionPlan 与就绪计划相同，unlinkConfirmed 严格 true，九字段 unlinkedIdentity 对应原HDD数据对象，完成时间在窗口内，独立域摘要正确。
- 完整HDD差异恰好只有指定 `.data` 缺席；原 `.json`、所有其他条目的完整身份/内容/属性均保持，无额外数据/partial。原数据的 inode、大小、SHA及无意外属性与计划一致。
- Root条目集合保持，仅本退役日志和本source marker发生允许的变更，原始记录字节与快照哈希/文件身份关联；其他Root、原claim、保护树、副本命名空间均保持。
- 执行回执为 Completed/retirementApplied=true，读取观察明确 Retired/CommittedRetirement、dataAvailable/dataPresent=false，均绑定同来源/操作。

这仅验证给定证据的一致性；哈希不是签名，不替代独立采集、授权、存储/实例重核和安全执行。

## 本轮验证

8项新专项通过，正向调用实际内部执行器在自有临时目录完成真实 unlink/fsync/证书提交，再关联其原始记录、完整HDD差异与 Retired 读取。年龄与硬件/namespace证明为明确替身。反例包括未到期/布尔或反序时钟、错inode/未确认unlink、原元数据或其他备份变化、数据重现/外来条目、原claim或命名空间变化、Unknown marker、错误回执/读取状态及前后同时截短。

完整回收root **786项通过、零跳过，85.341秒**；14项前端CI合同、2文件编译和Git diff检查通过；文档/元数据更新后8项范围检查单独重跑。独立只读代码复核未发现阻断项；复核没有运行实机，不替代测试或实际到期证据。

原保留的自然老化来源另以干净环境独立观察，仍为 `captureAgeElapsed=false / retirementExecuted=false / automaticExecution=false`。最早到期仍为 **2026-11-10 10:00:02.252 +08:00**；本轮没有重建来源、修改其策略/冻结闭包/证据、调用其退役或新增HDD历史。原证据严格基线继续保留。

测试与源码核验摘要 `retirement-completion-proof-983dad57b836440192329480499078b9`：3文件另加manifest，SHA256 `8771d53fc60d9646868eb9875b18d5252523a8524d2b7349af01a554fce37a77`，Root:Root/0700目录与0600文件。本记录不是正向自然到期的物理证据包，没有重建未保存的原始测试输出。

## 续跑

到期后先重核原来源的冻结代码、实例、保护、HDD与新鲜原生计划；另行获取独立执行前快照并单独放行自有来源的实际退役。执行后用本契约核对完整差异、证书、原claim和读取结果，再补实际失败/Unknown及精确收尾。本批新CI仍待准确完整终态。专用协议/标准安装/永久业务/API/Vue/Jobs、维护与其余59项继续；17项移出不算完成，整项0/59、阶段0/6保持。
