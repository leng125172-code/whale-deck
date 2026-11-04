# 递归ACL部分写入后 Worker 强杀与 Agent 重启实机验收

2026-11-04，承接[Partial实时撤权](CONTAINER_RECURSIVE_ACL_PARTIAL_REVOCATION.md)。**两套新隔离资源均已完成真实身份、Vue、部分写入、进程中断、不同管理员明确处置及独立只读核验。** 只签收本表两种组合，不代表完整故障矩阵或59项功能已完成。

## 真实结果

| 场景 | 产品结束绑定helper | 原租约 | 完成/修改/中止/未开始/待证明 | 最终处置 |
|---|---:|---|---|---|
| 同Worker SIGKILL | 0.709秒 | 自然等待86.177秒，41次原租约样本 | 1/1/1/2/0 | Partial保留保护；另一管理员ACK→Acknowledged |
| 固定Agent重启 | 0.445秒 | 原Worker继续观察，不改租约或时钟 | 1/1/0/3/0 | Partial保留保护；另一管理员ACK→Acknowledged |

两项各保留1个聚合及1个完成子项HDD元数据；Worker留3个Root记录，Agent留2个Root记录。完成计数已包含实际修改，不能相加。Worker第二项实际Aborted；Agent第二项未开始，按真实原件分别记录，不能统一写成同一计数。

## 冻结来源与准确运行回执

工具冻结 `7f30ea225caf5282413c38bb9e9d04e5b47353bb`；Python/MJS静态依赖闭包及安装helper字节均运行前核对。产品六组件仍为 `b880208f6d9c606fcb1c48aada2843ec1a826526` / Agent 1.53，无新产品发布、迁移、自动回滚或Agent权限扩展。

- Worker：`deploy/artifacts/container-recursive-acl-partial-7e5bf883af2e4c849e14856424fb67c4/`；unit `wd-qa-recursive-acl-partial-restarts-7f30ea2-worker.service`；Invocation `f25d5cf17cee4f7d83ff6411035f9ec5`；16:28:49–16:31:34 CST。
- Agent：`deploy/artifacts/container-recursive-acl-partial-275465971f9b4b9c85fa907e3132f0ef/`；unit `wd-qa-recursive-acl-partial-restarts-7f30ea2-agent.service`；Invocation `9d9087df63074ecba2e58172258b022b`；16:32:27–16:33:48 CST。

两unit在停止前分别保存准确Invocation、MainPID=0、active/exited、Result=success、ExecMainCode=1、ExecMainStatus=0及原日志，随后只停止各自已结束QA unit。不是用被回收unit的默认show补签退出。

## 安全门禁与真实证据

每项使用新的非root、无网络、只读根容器、四对象树、两位临时管理员。真实Authentik→Vue填完整access目标→异步预检→确认原执行；首项真实ACL/模式/ctime已变化，首项Completed/原HDD证明成立，其余三对象原样，才暂停准确helper。两个内核FD、固定transport/cgroup、root身份及pidfd绑定，不接受任意PID或unit。新QA unit例外只接受明确worker/agent两名，仍须当前MainPID/cgroup/Invocation一致。

重启前参数化只读检查10类容器文件工作及原租约屏障，拒绝任何其他活动或未处置工作；仅允许本QA和准确helper两个transient。Worker原容器ID/镜像保持，保存SIGKILL前后StartedAt/结构化inspect、原执行/登录绑定/lease token及期限；自然到期前41次SQL仍Applying/保护保持，执行Job Running，Root字节与完整部分物理清单持续保持。SQL/Root/时钟均未修改，已有首项修改未回滚。Partial行尚未completed，证明使用实际观察时刻，不伪造完成时间。

Agent PID由3795144变4044173，维护PID3598736及两个宿主来源保持。实际重启窗口1.042秒、7个维护样本均200/healthy/Ready/no-store；保存前、中、后时刻及间隔。**采样不能证明瞬时零中断，P0-05完整连续性仍未验收。**

两原页面均实际显示Partial五计数并禁止再次确认；两位恢复管理员均真实独立登录、原owner端点404，通过共享异常入口看到原任务。先“暂不处置”ACK=0且完整SQL/源/Root/HDD保持，再一次明确ACK=202→GET Acknowledged；执行Job Failed/Acknowledged、outbox清空/保护释放，唯一审计主体为另一管理员。原执行ID、命令、计划、RootStarted、已发生修改、计数、outcome/history digest及原Root/HDD字节保持，不续做、不回滚、不把处置当整树成功。

每项两个上下文均正常用户菜单双端退出/私有GET401，无取消或上传请求。每项5张真实320×600 PNG（原用户暗色/恢复用户亮色），共10张；实际计数、处置确认与结果图已检查。仅在完整文件字节、九字段、ACL/普通属性证明通过后，精确清理该树与容器；各两临时身份删除，未清理历史元数据。

各`independent.json`独立重读原SQL、Root、HDD、唯一处置审计及源/身份缺席。Worker独立核验在Agent新重启前完成，保存当时宿主一致回执；不把这份历史回执当作Agent重启后PID仍相同。共享重启核验器抽取后的旧写前Worker/Agent记录另做只读兼容（`archived-*-compatibility.json`），不重放旧实际操作、不重写旧回执、不声称旧门禁在新PID下整批复跑。

## 检查、最终状态与未验范围

94项相关Python工具测试在Linux root下全部通过（零跳过）、8项范围检查及Node语法/diff检查通过。没有产品C#/Vue/协议改动，不把旧产品全量测试当作本批重跑。工具CI `41799880348` 最后核查仍运行，未取得成功终态。

两项分别保留起始514、519个配置/账本/旧Root/HDD文件的原字节与九字段。最终11核心容器均running且健康检查healthy，8个配置/账本、145普通ACL Root、100递归Root、153普通ACL HDD、117递归HDD，共523个保护文件。数据库23意图：8Succeeded/5Canceled/1无执行Expired/3预期中断Failed/6Acknowledged，21执行/44相关Job；活动工作0。开发user NOPASSWD:ALL按原授权保留，凭据仅stdin及服务端内存，不进入仓库或日志。

源与逻辑HDD元数据实际设备号均2049，**不签物理跨盘或完整NVMe/HDD布局**。收尾统计首次误用不存在的plan_job_id（规划Job实际即意图ID），第二次把State.Status误当Health.Status，两次只读查询/断言失败均另存，已按实际字段复核；不影响两个原实机/独立门禁，不隐藏或改写失败回执。

仍需Partial/Unknown目标消失与实例变化、跨写入与路径/磁盘/依赖组合、其他写后故障、递归ACL历史恢复、可恢复删除/长期清理、维护连续性及其余59项。酒馆/供应商/Live2D与UI子计划范围保持，不提高整项完成率，不恢复17项移出任务。

项目图谱已按事件 `whale-deck.container-acl.7f30ea2.partial-process-restarts` 同步为active，准确回执已保存；完整目标保持未完成。
