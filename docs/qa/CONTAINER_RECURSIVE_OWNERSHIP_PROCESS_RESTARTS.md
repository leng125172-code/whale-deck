# 递归所有者：Worker强杀自然租约、Agent重启与维护入口

后续更新（2026-11-02）：cf291aa两套新资源真实组撤权/账号停用与另一管理员Partial五计数页面处置及独立核验已通过，见[撤权证据与边界](CONTAINER_RECURSIVE_OWNERSHIP_REVOCATION.md)。以下Worker/Agent批次正文逐字保留，其“实时撤权仍待”仅是当批状态；Unknown/跨写入/其他切点及ACL/删除继续，未重做先前操作。

2026-11-02。产品保持`b1fafedddfa1767d2d573bd94d2748f3cb5e7b44`/Agent1.50，不发布、不自动回滚。验收冻结`0442fb4ec7ff881ce13b5ec09f73cbaffab8b352`，两套新独占资源分别首次通过真实Worker SIGKILL/原自然租约恢复与Agent实际重启；两例均经真实页面和不同管理员处置、原SQL/Root/HDD及准确清理独立核验。

此前[取消及同上下文正常双端退出](CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL.md)与[常规六场景、旧退出失败及补证](CONTAINER_RECURSIVE_OWNERSHIP_REAL_UI.md)未重做或改写。此处只签收两种新故障，实时撤权、目标消失Unknown/跨写入互锁和其他回执组合、ACL/删除等仍待，59主项保持部分实现，17项移出不算完成，master未交付。

## 共同的实际切点与恢复约束

每例新建无网络、非root1000:1000、只读容器根、唯一准确HDD bind、限CPU/内存/PID容器；16文件+2目录，每文件完整2097159字节，混合原UID/GID、文件0640/目录0750、固定纳秒mtime和普通user xattr。页面明确目标2200:3200，18项预计变更。数字目标不等于Authentik管理员Subject或执行身份。

正常可见Authentik登录→Vue预检/确认→SQL/双Job/Worker/已装Agent/helper；没有Cookie/响应注入、SQL/缓存/租约/时钟/Root修改或产品故障钩子。21份实际依赖按冻结Git blob和SHA核对。为了提前布置观察器，仅在无其他工作时短暂停启同一Worker；真正的故障动作另记，不把准备停启算作强杀。

原SQL execution ID/计划、准确容器instance PID、容器root FD与原operation lock FD、固定root apply argv/cgroup及pidfd共同绑定原helper。第一项已经实际fchown且子回执Completed，聚合Applying、finishedThrough=0/startedThrough=1、根目录完整九字段原样时才暂停；不凭名称/定时猜测信号目标。

两例实际Partial均为1完成核对（含1实际修改）、1中止、16未开始、0待证明。原intent/Subject/instance/请求-计划-执行签名/原Started和execution Job均保持；回执恢复不续做或回滚，保护保留，Job Running，当前另一管理员明确处置才Failed/Acknowledged并释放保护。文件完整字节、inode、普通模式、mtime、普通xattr与未改项九字段不变，仅已改UID/GID及必要ctime变化。

## Worker真实SIGKILL与自然租约

预检只豁免本任务两个Job，其他文件工作/终端/锁均0、活动transient至多原helper后，以宿主QA权限SIGKILL准确原Worker容器。实际退出137、未观察到自动重启，显式启动原容器；ID/镜像不变、StartedAt改变。普通面板用户没有取得停止核心的能力。

产品在2.255秒内结束原helper，验收器未强杀helper。Root提前协作保存Partial合法，SQL仍必须等待原租约，不能要求Root也一直Applying。

| 原自然租约事实 | 实际证据 |
| --- | --- |
| 原expiry | 2026-11-02T22:56:35.028164+08:00 |
| 等待起点 | 2026-11-02T14:55:11.044012+00:00 |
| 等待终点 | 2026-11-02T14:56:35.028817+00:00，已越过原expiry |
| 单调时钟等待 | 83.985秒，与UTC墙钟交叉核对 |
| 到期前样本 | 40次，原SQL Applying/原token和expiry/屏障/Job Running及无取消均保持 |
| 实际对象与Root | 每次全树九字段及退出后Root原字节不变；完整字节在切点/Partial/处置/迟到探针重读 |

原lease到期后Worker只观察原操作，SQL恢复上述Partial，不重新Apply或修改余项。页面不发送取消（取消POST0），实际五计数逐dt/dd核对；原上下文正常双端退出后，原账号按finally准确删除，另一管理员真实小屏处置/正常退出、迟到不续做和独立核验通过。

门禁增加异常租约观测边界：只接受剩余25至95秒的原租约，100秒单调时钟硬上限，过期/过短/远期/无时区输入直接拒绝，不编辑租约来过验收。离线测试覆盖这些拒绝条件；实际租约仍按产品90秒自然到期。

## Agent真实重启与维护入口

另一套资源首项修改后，仅执行固定`whaledeck-agent.service`真实restart。实际Agent PID2380326→2542378，实际apphost来源仍b1fafed；MaintenanceHost PID2380322/来源保持。重启起止为2026-11-02T15:01:38.388556Z至15:01:41.054584Z；没有重启维护服务、Docker或数据库平台。

维护入口`/maintenance/status`在重启前到结束后共17次实际HTTP采样，全部200/healthy/Ready/no-store，字段白名单及≤2048字节边界核对。这里只证明观测窗口，不宣称未采样时刻或整机重启可用性。维护进程PID与源码不变，实际Agent新PID映射已确认。

产品2.341秒内结束原helper，切点/退出后/恢复后的整树九字段一致；原ID恢复上述Partial，没有继续fchown。当前另一管理员真实页面先暂不处置（原任务/源/Root不改）再唯一确认，原Job Failed/Acknowledged、Root/HDD/计数保留。原/新上下文均正常双端退出，取消POST0；迟到固定helper Apply只重放原Partial，不改变源或Root。

Worker例的宿主PID稳定事实及其独立核验发生在本Agent重启之前；后续Agent PID合法变化，不能据此重跑旧PID断言或改写旧批次证据。

## 永久证明、独立核验及清理

| 门禁 | 私有目录（deploy/artifacts下） | 结果 |
| --- | --- | --- |
| worker-crash | `container-recursive-ownership-faults-cb672975c7194c49a1ede15a8e381ee0` | 主门禁Passed、独立只读Passed，5 PNG/5哈希审计/2 HDD元数据文件 |
| agent-restart | `container-recursive-ownership-faults-f7fe133c69634b6eb3a6fe8eea8da7cf` | 主门禁Passed、独立只读Passed，5 PNG/5哈希审计/2 HDD元数据文件 |

每例保留1 HDD聚合与1完成子项历史，聚合含18项原计划；清单/计划/历史/实际版本/outcomeDigest按独立ownership域重算，与SQL/Root/页面一致。当前观察字段不冒充永久事实，原Root Partial不改为成功。额外迟到探针是固定受限helper调用，不是签名RPC，不扩大重放验收结论。

独立进程分别在各批次结束时重读永久SQL/Root、attest HDD、冻结源、原/新主体唯一哈希审计和实际PNG，准确源/CID及临时身份均不存在。只读观察明确不声称清理后重读已删源字节。两例10张真实320×600图尺寸/SHA通过，已目视复核计数及处置确认；普通模式/mtime/属性保留。源与HDD设备均2049，同设备元数据不算异盘灾备或NVMe业务覆盖。

最终活动任务/终端/锁、八类容器意图/屏障/租约及对应transient均0，11核心healthy，8数据库平台保持ID/镜像/StartedAt，Worker保持同ID/镜像。Agent新PID2542378与维护PID2380322均active。没有发布/回滚或改变开发NOPASSWD/密码。

本批56项递归边界、8项范围、Python -W error及Node语法通过并沿CI离线入口执行；没有把原生SIGKILL/restart/文件写入门禁交给CI自动运行。前批56以外的后端/前端全量结果不冒称本轮重跑。日志`/tmp/wd-recursive-owner-{worker-first,worker-independent,agent-first,agent-independent}.log`和root私有目录中的baseline/verified/independent-final/natural-lease/agent-restart及图片摘要保留；目录0700、原始文件0600，凭据只在内存/子进程环境，不进Git/日志。

后续继续实时组撤权/账号停用、目标消失Unknown与跨写入保护、更多回执/文件生命周期组合，再ACL、可恢复删除、长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D。工具目前新增启用worker-crash/agent-restart，未启用的类型或旧chmod证据不算递归所有者已验收。

文档检查：216份Markdown/1310处相对链接无缺失、git diff通过；前批Partial/取消/退出正文逐字保留，仅前置后续说明。
