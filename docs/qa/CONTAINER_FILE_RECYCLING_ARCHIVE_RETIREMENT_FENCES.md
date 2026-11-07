# HDD 退役独立预留与取消候选

2026-11-07，候选 `a5829de45dd54149f93e9cee7602471c24d9f1ca`。本批实现独立 Root 日志、退役来源 marker、取消及中断观察，已分项验证；没有安装、RPC、永久业务或 HDD 删除入口。

## 预留只登记来源，不授予删除权

预留允许早于到期日创建，用来排他登记将来的退役意图；要求当前完整副本仍有效、完整留存计划与当前事实逐字节相符。即使 `retentionElapsed=false/retirementEligible=false`，也可登记 Reserved，但所有回执固定 `retirementApplied=false`。`apply/recover/private` 均无条件拒绝 `RESERVATION_ONLY`，不继承目录创建或清理行为。真正执行仍须独立的授权/确认、到期与副本保护重核、持久化退役日志和中断恢复，不能把预留当作这些门禁完成。

记录位于与 archive/purge/source/restore 均独立的 Root:Root/0700 专用目录；其 `claims/<原source ID>.json` 是新的退役 marker，绝不替换原归档的 restore claim。marker 精确绑定 archive 请求、退役 ID、owner 和完整计划指纹，保留 Reserved/Unknown/Released 状态；Released 是墓碑，不 unlink。该目录只容纳本类型日志、空锁与 claims 子目录，未知 JSON/不安全权限/超过边界都拒绝。

沿退役日志→archive→purge→source→HDD 的固定顺序持锁。原只读留存预检拆出内部 `plan_locked`，在原记录锁仍持有时重核；没有释放后再嵌套取锁。所有同来源 marker 和日志变更均在原 source 锁内；扫描活动退役日志时只读、不取兄弟日志锁，避免反序。marker 丢失时，Preparing/Reserved/Unknown 日志仍阻止新预留，不能因为索引缺席就宣告来源空闲。

正常取消仅限 Reserved，要求当前 session/source guard；它先提交并 fsync Aborted，再将本 marker 写为 Released。取消只释放排队元数据，所以跨到期日或副本后来丢失仍可取消，不必将旧的“未到期”指纹伪装成当前可删除计划。其他 owner、Unknown、foreign marker 均不能借取消解除保护。原归档 claim、原 Purged source/purge/marker/HDD 和复制结果完全保留。回执中的 `sourceFenced` 仅描述本退役层，不表示原归档 claim 已释放。

## 持久化与中断边界

- Preparing 已提交但 marker 未发布：同操作观察可提交 Aborted，不发布 marker 或重试预留。观察前，其他操作仍会被活动日志扫描拒绝。
- marker 已发布而 Reserved 尚未提交：收敛 Unknown，保留 marker，不推断执行成功或自动释放。
- Reserved 与精确 marker 匹配：仅观察；索引丢失/冲突/过早 Released 则 Unknown，活动日志继续保护，不修复或覆盖 index。
- Aborted 已提交但 marker 仍为本操作 Reserved：观察可补齐 Released；已 Released 或后来由新操作占用时，旧操作晚到不改写新 marker。Unknown marker 不自动解除。
- marker 孤立而原日志丢失：精确旧请求只能重建 Unknown；Released 孤立墓碑只能重建 Aborted。不同请求拒绝接管。

## 自动化验证

21 项新增专项全部通过，使用真实临时 Linux 文件、原日志/claim/HDD、fork/pidfd 和五个真实 SIGKILL 切点：Preparing、SourceFenced、Reserved、AbortedSaved、Released。另验证 marker 目录 fsync 失败保留 Unknown、marker 写入失败、原 claim 丢失不修复、冲突与孤立记录、丢索引时先于观察的竞争拒绝、旧 owner/ID、到期或副本失效后的安全取消、伪造资格拒绝、目录独立/锁忙/权限不安全，以及独立真实记录 join 与 marker 篡改拒绝。命名空间和硬件证明仍为明确替身，不当作安装态矩阵。

最终完整回收相关 root **690 项通过、零跳过，51.713 秒**；8 范围、14 前端 CI 合同、5 文件编译、完整 archive 基线/行覆盖导入检查及 Git diff 检查通过。早期 19 专项通过时出现非 raw 正则的 SyntaxWarning，已修正，并补目录 fsync 和独立 join 回归；没有新实机失败，不重建未保存的完整原始日志。

## 全新容器/当前 HDD 分项

unit `whaledeck-qa-archive-retirement-fences-a5829de40001` / Invocation `ea1c34642a5e4a35a641c0d959d0a0c2`，success/0，94.233 秒。冻结 18 宿主模块与 126 验证闭包文件，使用全新自有惰性容器、新来源/清理/复制/退役 ID 和私有 Root 注册表。真实时钟仍在原捕获后 30 天之前，未修改时钟或生产保留配置。

1. 实际回收并清理本轮原件后，从当前 HDD 复制 1,048,583 字节为新 inode，完整内容及原 UID/GID/mode/mtime/user 属性匹配。
2. 新退役 A 在未到期时登记 Reserved，竞争 B 和新计划均 SOURCE_BUSY，B 无执行日志。取消 A 后提交 Aborted/Released，B 才可登记 Reserved。
3. A 的旧 reserve/inspect/discard 均返回历史 Aborted，不改写 B 的 marker。B 重复 reserve 仅观察，再明确取消成为 Aborted/Released。8 条实际回执/对应原日志/marker 和 Root 文件哈希、两条来源拒绝均独立 join；原归档 claim、原记录、HDD 和容器目录前后保持。
4. 存活新副本/HDD/退役记录先经独立进程核验，才准确 QA 收尾。收尾后旧 B 仍为 Aborted/未执行，Root marker 墓碑保留，原复制历史 Completed/当前绑定 false；最终独立及第三次当前 HDD 重读通过，夹具已准确删除。

父级与独立验证器均改用完整 archive 基线：生产 archive Root 48、recycle Root 202、恢复95/目录102/purge90、2346保护/11核心/21安装模块及 `f318d9ab` 保持；四类业务表 78/29/65/46 全行摘要前后相同、活动0。HDD 185→187，只增本来源 `.data/.json`。新退役的5个文件均在本批私有注册表，未进入生产 Root。源与 HDD 同 device，不签跨物理盘灾备。

证据 `container-recycle-archive-copies-e0ea3f3d73f24c1193c0dba54cec4789`：186 文件另加 manifest，SHA256 `a9ccf6f8c3e55f3aa9966917c6d01864494dad71e0899a860c4976559fc61a58`，Root:Root/0700 目录和0600文件封存。私有路径/资源引用不上项目图谱。

## CI 与续跑

上批验证 `b90fe893` / CI `42335125756` 和文档 `7baf380b` / CI `42335724188` 已分别核对12路 success。新候选 `a5829de4` / CI `42337313796` 尚待准确完整终态，不能沿用前批绿灯。

本批只签内部预留/取消分项，未签安装 RPC、真人身份/Vue、生产调度、自然到期、实际退役或完整故障矩阵。后续执行应保留旧预留计划历史，并取得新的到期/副本证明与明确执行授权；不能直接按旧计划的诊断位执行删除。继续持久化退役墓碑及兼容读取、实际 HDD 退役与恢复、专用协议/安装、永久业务/API/Vue/Jobs，以及 stage 安全退役、来源复用、维护连续性、大文件和全部59项。17项移出不算完成；整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持。
