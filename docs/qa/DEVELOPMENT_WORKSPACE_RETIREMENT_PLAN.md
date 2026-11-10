# 开发空间源码退役前核对候选

2026-11-11，新增内部只读计划引擎及真实 NVMe/HDD 隔离实盘验证。产品实机仍为 b46f1bbc。新增引擎没有安装、CLI、RPC 或 API 入口，持久退役意图、用户确认、源码删除及预算释放尚未实现；不能计 P3-18 或完整主任务完成。

## 实现边界

deploy/host/whaledeck-development-retirement.py 仅接受 schemaVersion/storage/expectedEpoch/archiveId/archivePlanSha256/captureReceiptSha256 六字段，拒绝未知字段、布尔数值、非法 UUID/摘要和非偶数停机 epoch。未来调用方须从已授权的持久 Capture/独立 Verify 原任务取摘要，不接受浏览器自报归档事实作为业务前提。

按 NVMe 独占锁→HDD 共享锁顺序核对当前分配、硬配额、停机版本和完整源码清单。保持 HDD 外层锁期间独立重读归档全部内容及私密 request/receipt，逐项比较源清单/版本/配额证明和 HDD 持久身份。返回前再次校验源清单/元数据，退出源上下文时重验命名空间、分配和配额。内部第二个 HDD 共享锁不升级、不反向取得 NVMe 锁，真实冲突立即拒绝。

计划使用独立 purpose 摘要域，绑定原空间、停机 epoch、源清单/版本/字节数、原归档 ID/计划/回执、HDD UUID/证明及归档摘要/索引/字节数/inode，不含源码路径或内容。输出明确 retainsSource=true、reservesBudget=true、deletionAuthorized=false。摘要只表示确定性事实，没有身份、签名或时效，不能作为删除授权或执行 token。

## 已执行验证

- 14 项 root 专项通过：实际 FD/codec 覆盖源码修改、同内容但 inode 替换、目录属性/新增文件、旧 epoch/已恢复空间、错误环境/预算/空间、错误回执/HDD/配额、源读锁、归档损坏/回执缺席、晚到源复验/命名空间失败、读前后源码/HDD/配额保持。该层配额和设备为明确测试替身。
- 原归档专项 18 项和 CI 分片合同 9 项通过。CI installer 接入新增 14 项及实盘入口语法检查；候选仍需自身准确 CI。
- 实际运行 validate_development_archives_native.py：使用 O_EXCL/inode 绑定的新建 NVMe/HDD 各 2 GiB 完全预留 ext4 镜像，实际设备类型、prjquota 和独立文件系统核对通过；正式 300,000,000,000 字节 HDD 只读核验。非特权写实际取得 EDQUOT/ENOSPC，原三次归档、两次真实强杀接续、重挂及固定 helper 的 systemd/seccomp/Landlock/复制中取消回归通过。本轮未启用签名 RPC 探针。
- 新计划实际重复验证一致，真实源共享读锁和 HDD 独占写锁均阻止计划，错摘要/旧 epoch 拒绝。另建专属隔离空间：归档后改源被拒绝，重新 Capture 才匹配；随后真实截断新 HDD 对象被拒绝。两次额外 Capture 与修改后的源仅在隔离镜像内保留至收尾，原证明源内容/inode/配额保持。
- HDD 实际重挂后活跃挂载 ID 变化而计划一致；原源码路径实际移出时 HDD Verify 仍通过，退役计划拒绝。最终按登记挂载、镜像 inode 和私密目录精确清理，无临时镜像/挂载/执行进程残留。

实盘私有证据：deploy/artifacts/development-archives-native-7535077fb3504df0a260bbfcbba270f5/，含源码原件、retirement-plan-native.json、accepted.json、归档/强杀/重挂/helper 回执及 cleanup.json。额外夹具 Capture 单列，不混记为原三次归档。

130 个文件已封存并逐字节重验；SHA256-MANIFEST.json 摘要为 7167fe2b09e619df83998de4d1901f390f12de2952a7a8b95c1dba273ca1c153，后续提交/CI 回执另存。

独立对照前批故障验收 after 快照：五产品空间/epoch19、源码/inode/预算/配额、18 报告、18,344 条既有记录、原 HDD/journal、11 核心及宿主进程、私有配置和部署账本保持，活动 job/路径锁为零；120 GB/300 GB 完整预留保持。回执另存 deploy/artifacts/development-archive-fault-records-df67fc31-20261111/production-preserved.json。原用户 source.py 修改摘要及两个 user:user/0664 Git index 保持。

## 接续与未证明事项

本候选没有持久计划、留存期判断、当前身份/会话、业务授权、执行围栏或删除入口；依赖持锁阻挡产品正规源写入，不声称阻止不遵守锁的宿主 root 写入。未来执行必须在可靠源围栏下重新核对完整源和归档，不能拿旧摘要直接删除。

继续持久退役意图/幂等任务/确认、当前授权和成功 Capture+Verify 服务端绑定、旧源码/运行/恢复入口围栏、签名固定 helper 与精确回执及未知状态恢复。只有物理退役完成回执持久化后，才能原子释放预算并保留墓碑/审计/报告。用户闭环使用新建专用验收空间，原五空间不直接删源；HDD 留存与后续删除另行处理。已校验副本不自动构成删除授权。

候选未标准安装或真实身份验收，59 项及 ST272/PET6/供应商 Live2D16 门槛继续，17 项移出不恢复，不签 master。图谱仅本地 pending，未远程同步。
