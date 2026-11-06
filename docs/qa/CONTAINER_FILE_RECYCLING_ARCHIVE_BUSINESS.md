# 归档复制内部业务编排候选

2026-11-07，`f7c05f79ec822970b00a94a41067679806f5d501` 接续[归档1.59安装态分项](CONTAINER_FILE_RECYCLING_ARCHIVE_AGENT.md)，实现独立归档意图、来源解析、业务MAC、确认及Reserve→Apply分派。本批未接EF/migration/真实仓储、Worker/DI或API/Vue/Jobs，未调用新的生产归档RPC；测试是内部候选验收，不是永久化、真实身份页面或归档业务整链验收。

## 来源与独立确认

入口只接持久化清理意图ID，不接受客户端owner、计划或物理路径。清理记录必须MAC完整有效、Succeeded且已完成、无业务保护，并有独立Root预留、Reserved证明及Apply入口，回执必须Completed/PurgeApplied/SourceFenced/OwnedFence。再读取准确原回收记录、验证原请求/计划/执行/回执以及历史Trashed/HDD事实，重新计算原回收→清理的来源proof，并比对清理记录保存的proof和完整命令。原历史记录不被覆写；当前Root的Purged状态及实际HDD继续由归档RPC复核。

新来源MAC涵盖清理请求/计划/执行/回执hash、原来源proof和完整ArchiveCopy命令。新请求/计划/执行/回执/确认各自分域，幂等ID、执行ID和owner与回收/清理域分开；执行ID不能复用旧清理或回收操作。规范序列化拒绝未知字段、非规范表示及过大数据，完整嵌套字节在await前冻结。

查询前后、确认与活动RPC反复复核当前管理员和原登录。已成功清理后晚到的取消不会抹除Completed历史事实。确认绑定subject/login/计划/版本/期限，警示从HDD以新inode复制、不覆盖已有文件、原清理和备份不改变、断线查询同一任务以及Unknown处置不代表复制成功。

## 预留、复制与未知结果

分开持久记录RootStartedAt、Reserved证明/MAC与ApplyStartedAt。只有新执行、当前租约/来源/权限/取消/期限均有效且三阶段仓储入口成功，才允许Apply；每6秒和RPC前后检查，失权或丢租约取消活动令牌。重领先Inspect原ID，已有Reserved只Discard原ID，不重新预留或复制。

历史Completed允许BindingCurrent=false或当前claim不再属于原操作，仍保留ArchiveCopied=true与原PublishedIdentity/ctime。回执必须满足新inode、设备/UID/GID/mode/大小/mtime等原件约束，不能把原已删inode或HDD inode冒充新副本；已有成功事实不得换成另一inode。丢成功响应后即使退出或来源变化，也仅查询同一ID完成，不重复复制。

缺少业务预留/Apply标记的意外Completed、无法消解的Reserved或不合规回执保持Unknown和保护。明确处置由当前管理员以原执行上下文Discard/观察；仓储必须原子提交版本、审计、完成时间和保护释放。无回执Unknown可记录晚到终态，已有明确Root Unknown事实不被改写为Completed。

仓储契约要求事务中锁定原回收与成功清理来源、复核MAC、同所有写入器及跨容器宿主目录共享保护，原子提交Job/Outbox/审计及两阶段入口。成功复制的来源在独立留存/claim释放策略明确允许前不可再次提供为新来源。这些是后续真实实现的约束，本批接口和内存测试替身不提供SQL围栏或来源排他保护。

## 验证与原始问题

74项新业务检查通过：完整复制流、双来源MAC变化与原记录变化、幂等/会话/分域、预留后取消/撤权/到期/来源变化/丢租约、入口保存失败、重领与丢回执、Unknown跨管理员处置、两项长调用令牌取消、新inode及历史绑定失效。初轮54项通过后补20项归档专属检查，最终74项通过。

解决方案Release构建零警告/零错误；5875项单元零跳过，782项集成通过、7项既有默认原生跳过；585容器文件与453回收Linux root检查零跳过、8范围及14前端CI合同通过。跳过项不计通过；未把网关/身份/登录/仓储替身当成真实Authentik、PG或Root/HDD业务证据。

准备期两个DispatchProxy覆盖参数名触发CA1725，首次测试未执行；修正为基类targetMethod后通过，未关闭分析器。封存首次因两个既有csproj的工作树CRLF与Git LF差异停止，无成功清单；保留失败事实，仅对精确CRLF差异分别保存实际/Git字节，其余逐字节匹配，未修改原文件或宽放任意差异。最初未保存的失败源码/原始输出只记事实，不补造。

## 运行边界和封存

只读复核Agent/Maintenance仍为上一批PID1244695/1244692、active/running，运行checkout仍9694af1c；新业务未在DI/Worker/EF注册。此检查不替代完整业务行、Root/HDD或维护连续性验收；本批未重新部署宿主或应用，也未重放安装态写入。

私有证据 `deploy/artifacts/container-recycle-archive-business-4157359a8e2942e28698123a7c2d34ee` 共404文件（另加清单），含388份Domain/Application/保护器/回收相关测试及注册源码，2份CRLF差异Git原件、原测试/构建日志、失败事实和只读运行检查。清单SHA256 `68674b1a74832ab65fd4921ff3019439e987adafc3105cb65963e4e7df415f58`；root:root目录0700、文件0600。源码按准确提交保存并核对；私有证据不上传展示站。

下一步实现EF映射/migration、真实PG持久化与双来源锁/共享写入保护、Job/Outbox/审计及Worker接线，用真实隔离PG验证事务、重领、取消和Unknown，再接管理员API/Vue/Jobs与真实身份整链。归档适用故障矩阵、claim/暂存清理、长期留存与维护连续性及其余59项继续；整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
