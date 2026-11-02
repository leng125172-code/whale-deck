# 递归普通权限：Worker强杀、原自然租约与真实页面恢复

> 后续2026-11-02：目标消失后Unknown18与宿主实际1项修改分开保留、原账号删除后的另一管理员真实页面处置/迟到不续做和独立核验已通过，见[后续Unknown证据](CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN.md)。原实例保留的退出/撤权/Agent重启及其他组合仍待；以下原批次正文和失败记录保留。

2026-11-02。运行产品保持`0dede0804a5f9d1847c23744b90c7e3bae5f7751`/Agent 1.49；本批仅新增验收与记录，没有发布、升级或回滚。冻结验收源码`23704b93f76f8d68132162c4bc92429d5327971a`，首次通过的完整门禁为本批第三轮；前两轮QA错误与准确清理保留，不改为通过。此前[页面取消及Partial不同管理员处置](CONTAINER_RECURSIVE_PERMISSIONS_PARTIAL.md)没有重做。

## 真实故障与自然租约

新建一个独占、无网络、非root1000:1000、只读容器根/限资源、唯一自有HDD bind的容器。树为16文件+2目录，每文件2097159字节/普通xattr、原纳秒mtime和混合UID/GID保留；明确预检文件0600/目录0700，18项预计修改。原fixture自带文件的指纹/内容SHA也纳入baseline。

真实Chrome/Authentik正常可见登录→Vue预检/确认→永久SQL/双Job/Worker/已装Agent/helper。无Cookie/响应注入、SQL/缓存/Root编辑、时钟/租约修改或产品测试钩子。15份被依赖验收源码按冻结Git blob及SHA保存。

为预先布置观察器，在其他任务/终端/锁/屏障为0时先短暂停启同一Worker。原apply helper以准确pidfd、固定argv、容器root FD和原operation lock FD定位，信号前后双重核验。切点为第一项已经实际修改且其子回执Completed，聚合Applying、finishedThrough=0/startedThrough=1，其余树未完成。页面只查询原任务，**没有点击取消，实际取消POST为0**。

再次预检只豁免本任务两个Job，所有其他文件工作为0、活动transient至多本helper，随后以宿主验收权限对**同一原Worker容器**执行SIGKILL。实际观测退出码137、未观察到自动重启，显式启动原容器；容器ID/镜像不变、StartedAt改变。不把该特权QA操作描述为普通面板用户可以停止受保护核心。

产品在2.329秒内结束原helper（观察上界，验收器未强杀helper）。helper退出时已协作保存Root Partial，这是合法提前回执；SQL仍持有崩溃留下的原租约和保护，不能要求Root也一直Applying。

| 原租约观测 | 实际结果 |
| --- | --- |
| 原到期时间 | 2026-11-02T16:51:41.333302+08:00 |
| 自然等待起点 | 2026-11-02T08:50:17.506542+00:00 |
| 等待结束 | 2026-11-02T08:51:41.333840+00:00，已越过原到期点 |
| monotonic等待 | 83.827秒，与UTC墙钟交叉核验 |
| 到期前采样 | 40次：原SQL Applying/原lease token和expiry/保护/Job Running；未请求取消 |
| 实际对象 | 每次全树九字段、退出后的Root原字节不变；完整内容在切点、Partial、处置后和迟到重放分别重读 |

到期后Worker只观察原操作：原intent/Subject/容器instance/计划/签名/Root开始时间及executionJobId保持。SQL恢复Partial，lease清空、保护仍在、Job仍Running，不自动续做或回滚。实际结果为**1完成核对/1修改/1中止/16未开始/0待证明**，互斥合计18。恢复后树的全部九字段与强杀切点完全相同；不是通过完成第二次修改来凑计数。

## 页面、不同管理员与独立证据

- 原真实320×600暗色页面按精确dt/dd逐一核对五类计数，并保存单独计数PNG；警告、原任务查询和处置入口真实存在，正常双端退出。注册1/确认1/取消0/内容PUT0。
- 原临时Authentik账号实际删除后，另一位临时管理员正常可见登录/files，在320×600明色页面先“暂不处置”（处置POST0），再明确确认（唯一POST202）。最终Acknowledged/原执行Job Failed，保护释放，Outbox0；新管理员只写处置主体，原Subject/计数/outcome/history保留。
- Root聚合、两个子回执（Completed/Aborted）及HDD聚合/一个已完成子项历史逐项验证；未开始16项没有伪造子回执。计划/清单/子计划/实际版本/独立域摘要/outcomeDigest重算，Root/HDD原字节跨处置不变。保留的是元数据，不是文件内容备份。
- 已装固定privileged helper追加迟到原apply，返回原Partial/replayed，完整树及Root字节不变；**该追加探针不是单独签名UDS RPC**。主业务链仍经实际已装Agent完成。
- 主门禁退出0，准确自有CID/源目录移除；独立只读进程再次读取实际SQL/Root/已attest HDD，与私有原件一致，重核40份自然租约样本/同一个原Job/时间边界/迟到回执/准确清理。它不声称清理后重读已经删除的文件。
- 5条实际递归审计：唯一partial-acknowledged为新管理员，其余属于原Subject；details仅intentId/containerId和重算一致的instanceHash/pathHash。5张320×600 PNG通过，已目视复核暗色实际五类计数与明色完整暂不处置/确认按钮。
- 已装11个helper/依赖与产品0dede08 Git blob逐字一致；两宿主实际PID/sourceRevision不变。11核心healthy，8数据库平台容器ID/镜像/状态/StartedAt不变；API/Gateway不变，原Worker按本批故障停启，独立核对其最终StartedAt正是本次强杀后的启动，不重建容器。
- 源和HDD实际device均2049，仅同设备元数据历史，不计NVMe业务、异盘灾备或HDD故障验收。user NOPASSWD授权保留，产品Agent权限未扩大。

## 两轮失败与修复

1. `1430da2`首轮在浏览器startup拒绝：Python的文件类型循环覆盖了故障类型变量，baseline误成File。实际SQL确认该CID的递归意图0行，未停启/强杀Worker；自有目录先按安全策略保留。另行按准确随机命名、全树原指纹/完整字节、原fixture唯一创建种子内容及inode现场证明精确清理，证据保留。修正变量，并新增无登记失败清理：不能以“没有浏览器引用”推断请求未到达，必须实际SQL0行/无活动工作/全树完全原样，才清理自有树。
2. `6715ce2`第二轮实际SIGKILL成功，但QA错误要求helper退出后Root只能Applying。产品已合法保存Partial，SQL仍等原自然租约；原树没有继续变化。门禁因此失败，页面协调超时记录保留，随后走正常BFF失败处置、原Job Failed、准确清理。不能据这个失败轮计完整自然租约/页面验收通过。

修正为区分切点Root Applying、helper退出后Root Applying或Partial，以及SQL自然租约后恢复；不放宽原token/到期时间/原Job、保护、文件/HDD、停止时限或页面断言。第三轮使用新容器/目录/账号/intent，完整通过。新增2个租约/原绑定拒绝边界，连同原5个探针、7个常规浏览器探针和8个范围检查，**22项通过**，Python/Node语法及diff通过；不冒称重跑前批后端/前端全量或新产品CI。

## 私有证据与未验范围

- 首轮：`deploy/artifacts/container-recursive-permission-faults-5f3c99ea5fb24400a05130371defd75e/`，含independent-failed-cleanup。
- 第二轮：`deploy/artifacts/container-recursive-permission-faults-c6a38d4d60194cdfbb4fdb93aac3c80b/`，含实际崩溃/pre/crash SQL/切点/failed-gate/failure-acknowledgement及Root观测。
- 第三轮：`deploy/artifacts/container-recursive-permission-faults-1dc6ec0ab9454a0d9bcd772ce4ab6807/`，冻结baseline/SHA、bound-helper/cut-root、worker-crash、pre/crash SQL、physical-cut、after-helper-exit-root、40样本natural-lease、Partial/最终SQL与Root/HDD原字节、原/新管理员回执、late-apply/history/audit/installed-core/independent-final和5 PNG。均root私有，不入Git。
- 日志：`/tmp/wd-recursive-worker-crash-first.log`、`/tmp/wd-recursive-worker-crash-second.log`、`/tmp/wd-recursive-worker-crash-third.log`、`/tmp/wd-recursive-worker-crash-independent.log`。

```sh
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/validate_container_recursive_permission_faults.py 0dede0804a5f9d1847c23744b90c7e3bae5f7751 worker-crash
sudo -n env GIT_OPTIONAL_LOCKS=0 python3 deploy/validation/verify_container_recursive_permission_faults.py container-recursive-permission-faults-1dc6ec0ab9454a0d9bcd772ce4ab6807
```

已有本批回执，不为展示再次执行第一条。它要求冻结源码/无其他活动工作并真实强杀原Worker；第二条只读验证保留记录，后续产品版本或Worker又重启时需区分新的运行边界。

仍需退出/实时撤权、Agent重启、其他Worker/回执丢失组合、业务Unknown不同管理员处置、首次登记未到达，以及递归所有者/ACL/可恢复删除/长期清理、P0-05手动回退来源和其余59项。当前未新增整项签收；[比例与代码量估算](../PROGRESS_ESTIMATE.md)不替代验收。17项移出不算完成，master尚未交付。
