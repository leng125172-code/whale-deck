# 退役准备候选标准发布与真实归档回归

2026-11-11，准确提交 `da519f2774605d007ae30f5cf9004e913efcbefe` 已经标准入口覆盖发布 dev，并完成真实 Authentik / Vue / BFF / PostgreSQL / Worker / Agent / NVMe / HDD 归档回归。源码退役准备仍为内部候选：没有生产 DI、用户 API、Worker、签名删除入口或预算释放，不能计 P3-18 或完整主任务完成。

## 准确 CI 与发布

[CI 42868198846](https://github.com/leng125172-code/whale-deck/actions/runs/42868198846) 的准确 head 为 da519f27，13/13 job 全部 success，包含六个原完整浏览器分片及汇总。已保存 backend、native-postgres 和 installer 的原始完成日志及摘要：6,253 单测通过/0跳过，837 普通集成通过/7项专用 native 跳过；独立 PostgreSQL job 实际执行1项/通过1项/0跳过，TRX 门禁要求新的退役准备阶段实际出现。installer 的真实 systemd/cgroup/pidfd 归档收尾检查通过；前两批 CI 失败日志继续保留。

正式仓库 `/data/GitRepos/whale-deck` 由 user 干净快进至准确提交后，仅派发一次固定维护 `update`。观察器先就绪，维护按既有发布、安装、冻结镜像构建、Migrator 和三应用强制重建流程执行；没有自动回滚。发布 unit 和观察器均实际到达 active/exited、MainPID=0、Result=success、ExecMainStatus=0，再进行后验。

六组件二进制/镜像及真实 HTTP 来源一致；标准 host validator、23 个安装模块逐字节对照目标 Git、两宿主实际 exe inode 对照安装文件均通过。新增内部 `whaledeck-development-retirement.py` 没有纳入安装或能力广告，不把源码文件存在视为已安装。11 核心服务健康，8 个数据库容器身份保持，三应用与两宿主更新。

四个固定本地 HTTP 入口共采样1,052次：1,021次200、31次预期维护503，连接错误0。PID 1 持有的8080/8081监听 inode 和 socket 激活时间在采样中保持；发布后新非特权宿主也持有同一对监听。证据只证明采样及前后监听身份，不推断所有未采样时刻。

## 迁移与真实用户链

迁移历史只新增 `20261110171841_WorkspaceRetirementPreparation`（EF 10.0.4），public 表只新增空的 development_workspace_retirements。已核对15列、4索引、8个PK/FK/CHECK约束、PostgreSQL 18的9个NOT NULL目录项及延迟配对约束触发器；新表仍0行。部署前后18,344条原记录完整保持，未插入退役准备意图。

使用临时真实 Authentik 管理员，经实际320px暗色页面登录、BFF/CSRF/Origin和资源授权完成：

1. 固定 QA 空间 Active epoch19 → Stopped epoch20。
2. 首次 Plan 同键重放返回同一任务，随后显式取消，未产生 HDD 副本。
3. 第二次 Plan 显式确认，完成真实 Capture 和独立 Verify；页面重载后回执仍可见，未完成归档期间 Resume 围栏保持。
4. 真实320px暗色及1440px亮色页面无横向溢出；320px实图已人工查看。
5. 经正常产品流程恢复 Active epoch21，再从用户菜单退出；BFF返回401、身份会话返回403，临时管理员精确删除。

独立 SQL 和实盘核对两归档意图、四个阶段原请求/任务/成功证明及审计。新副本包含38项、4,132字节源码，实际压缩文件3,025字节；实盘大小、摘要、inode、私密回执与 Capture/Verify 数据库证明一致。原源码、空间和预算继续保留；新副本也保留，不做手工资源删除或SQL清理。

## 保持、失败与收尾

真实用户链后，18,339条原非空间历史记录、5空间的其他登记字段/源码/inode/预算/硬配额、18份原报告保持。原HDD的10个树条目（6个文件）及journal的126个条目保持，既有4份清单合计268个证据文件逐字节重验通过。NVMe120,000,000,000字节和HDD300,000,000,000字节的完整预留、挂载和强制限制保持；user NOPASSWD配置原样保留。

两项验收脚本问题保留原件并修正后复验：检出脚本继承私密证据umask 077，导致user自己的Git索引变0600，已精确恢复user:user/0664并补强派发检查；外层shell未在该后置断言退出后停止的事实也记录。迁移后验最初只预期8个普通约束，误拒PostgreSQL18新增的NOT NULL和延迟触发器目录项，现逐类精确核对全部18项；没有修改生产schema来迁就验证器。最初旧证据清单的bytes/sizeBytes字段差异也保留为验证器修正记录。

任务、路径锁和归档租约归零，原归档执行cgroup为空。浏览器正常关闭，两个准确且已完成的QA transient units通过systemctl stop收尾，InvocationID、终态和journal保留。两仓库索引均user:user/0664；原用户source helper diff摘要仍为 `e56a10d30f5cb49bf771ae6fc9bd35a3564bfc586b4ca3338899940b84c0eca2`，未修改或提交。

私有证据 `deploy/artifacts/development-retirement-preparation-release-da519f27-20261111/` 已封存66文件、6,721,949字节；SHA256-MANIFEST.json摘要为 `b41bab827e188939931ce1b878c6c58b18ea53e948fb0d25a0da803b8eeb354d`。封存后不追加；后续提交/CI回执另存。

下一步接当前身份授权、固定helper和签名RPC、持久物理执行及未知态恢复、完成回执/预算墓碑、API/Vue/Worker；使用新建专属验收空间完成源码退役与资源释放，原五空间不直接删源。59项及ST272/PET6/供应商Live2D16继续，17项移出不恢复；完整主任务仍0/59，master尚未交付。项目图谱仅本地pending，未远程同步。
