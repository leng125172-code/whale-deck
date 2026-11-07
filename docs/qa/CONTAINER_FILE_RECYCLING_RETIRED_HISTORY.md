# HDD 历史退役终态格式与只读分类候选

2026-11-07，候选 `d5a3c3603917c9a281d5784c7caf20f82fb648a7`。本批增加版本2终态格式校验及 Available/Retired/Unavailable 只读观察，保留版本1预留/取消行为；没有终态生成、unlink 执行、安装/RPC、生产策略或真人页面入口。

## 终态证明与旧数据验证

原 HDD `.json`/historyDigest/source Purged/归档 claim 均不重写。新版本2退役日志保留原 request，并增加 completion：新的 executionPlan、严格 true 的 unlinkConfirmed、与原 archiveIdentity 完全一致的九字段 unlinkedIdentity、到期后的 completedAtUnixMs、独立域摘要 proofDigest。executionPlan 必须已到期且副本有效；除诊断位/其指纹外，其余事实与原预留计划逐字节一致。源/清理/marker 摘要还必须锚定原 archive 计划，不能仅靠重算外层指纹替换源事实。

proofDigest 是完整性校验，不是客户端签名或删除授权；信任边界是受保护的 Root 日志。未来执行器只有在授权、精确 unlink 和目录 fsync 之后才能生成这些证据。本批没有这个写入执行器，不能把格式支持或测试注入的终态当成已经退役。

版本2 Completed 必须有完整证书；Unknown 可保留证书或因孤立 Retired marker 使用 null。Completed 丢失或冲突的 marker 只收敛 Unknown，保留证书且 retirementApplied 为 null；孤立 Retired marker 不推断删除成功。Completed 日志也参与活动保护，防止索引丢失后被重新消费。原版本1回执和预留/取消逻辑保持，`apply/recover/private` 仍拒绝执行。

ContainerRecycleHistory 将元数据验证拆为 `read_metadata`，并收紧 schemaVersion 的整数类型。原 `read/verify/restore` 路径仍必须执行完整数据验证，不会因为存在退役日志而把“没有字节”当成可恢复数据。原 wire 字段不变；即使新观察为 Retired，旧 historyAvailable 也不会变为 true。

## 只读观察规则

- **Available**：原历史元数据/digest 和完整数据校验通过，数据身份前后稳定，没有不确定退役或 partial 冲突。正常 Reserved 和 Released 不会隐去仍存在的完整字节；Available 仅表示字节完整，不授予恢复或删除权。
- **Retired**：唯一版本2 Completed、精确 Retired marker、原 copy/purge/source/marker/claim、原历史摘要和当前存储/目录全部匹配；时间不在未来，`.data` 与 `.partial` 均独立确认缺席。
- **Unavailable/拒绝**：缺数据且无证书、坏数据、孤立/Unknown/冲突证明、数据重新出现、partial 残留、完成时间在未来、历史/原 claim/存储不一致等，绝不退回“已退役”或“完整可用”。

观察只持已有 HDD history.lock，不从 HDD 层反向获取任何 Root 日志/source flock；只验证已有锁形状并读取原子日志，随后重读证据、数据身份和存储配置。没有锁创建、调和、证书生成、claim 修复或负载写入。这是有一致性重核的时点诊断，不是跨文件事务快照；任何后续动作都必须另行授权和重核。

## 自动化验证

18 项新增临时 Linux 文件/日志测试通过：完整旧数据、Reserved/Released、缺失/损坏、不完整/冲突证书、确切终态 join、完成时间在未来、数据仍在/partial 残留、缺失/Released/orphan marker、孤立终态转版本2 Unknown、保留证书与来源排他、false/整数 unlink/年轻计划/错 inode/源摘要欺骗、旧 claim/清理事实变化、布尔 schemaVersion、读中证据变化、配置/目录不安全等。

终态证书和正向到期时钟均为明确注入的测试夹具，仅移除自有临时目录内数据文件，不是实际 HDD 退役证据。完整回收相关 root 最终 **708 项通过、零跳过，56.776 秒**；8 范围、14 前端 CI 合同、7 文件编译及 Git diff 检查通过。原21项预留/取消回归保持。

## 真实 HDD 的现有数据兼容分项

unit `whaledeck-qa-retired-history-d5a3c3600001` / Invocation `d282de9c7c42453bafdc4f223f875fae`，success/0，96.635 秒。冻结19宿主模块及127验证闭包文件，用全新自有惰性容器、新来源/清理/复制/退役 ID 和私有 Root 注册表运行。

实际复制新 inode 后，分别在未预留、A Reserved、B 取消 Released、QA 副本准确移除后的四个观察点执行新分类；均为 Available/VerifiedData、dataAvailable/dataPresent=true，计划元数据、原 claim、HDD、原记录和命名空间前后无改写。仍执行两次到期前预留、竞争拒绝、取消交接和旧操作隔离；存活副本/退役记录先独立核验再 QA 收尾，最终独立和第三次当前 HDD 重读通过。未生成版本2终态、未改变实际时钟、未删除 HDD 备份；不签真实 Retired/自然到期/执行或完整业务整链。

生产四类业务表78/29/65/46全行摘要保持、活动0；Root202/恢复95/目录102/purge90/archive48、2346保护/11核心/21安装模块和产品 `f318d9ab` 保持，HDD187→189仅增本来源两份历史。源与 HDD 同 device，不签跨物理盘灾备。

证据 `container-recycle-archive-copies-608b61618cf9424096c8860694ffac39`：192 文件另加 manifest，SHA256 `42d8b03d56aaaae7bf30ffa585ea109894ba8766042cd4341e6c0424707169f7`；Root:Root/0700目录及0600文件封存。私有路径和资源引用不上项目图谱。

## CI 与续跑

上批候选 `a5829de4` / CI `42337313796` 和文档 `a3b11dcc` / CI `42337980715` 已分别核对12路 success；本批 `d5a3c360` / CI `42340077729` 尚待准确完整终态，不沿用前批绿灯。

继续实现独立授权的实际 HDD unlink/fsync 与持久化证书提交、中断/Unknown恢复、真正自然到期和删除后读取验收，再接专用协议/标准安装/永久持久化/API/Vue/Jobs。stage 安全退役、来源复用、维护连续性、大文件及全部59项仍待；17项移出不算完成。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持。
