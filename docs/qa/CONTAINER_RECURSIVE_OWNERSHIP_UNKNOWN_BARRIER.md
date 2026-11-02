# 递归所有者Unknown：跨容器目录真实延期与明确释放

2026-11-02。产品保持`b1fafedddfa1767d2d573bd94d2748f3cb5e7b44`/Agent1.50，无发布/回滚。冻结`a6f472be23fad869404651206c2a062512b4df4f`/21依赖，新资源组合首次主门禁与独立核验均Passed。此前[目标消失单例](CONTAINER_RECURSIVE_OWNERSHIP_UNKNOWN.md)已经完成，不复用或重做原操作；本组合另建两个准确自有容器，验证Unknown保留时另一容器目录任务真实延期及释放后的实际发布。

## 原Unknown保持，另一任务自然延期

第一容器与前一单例采用相同有界夹具：16文件+2目录、每文件完整2097159字节、普通模式/混合UID-GID/纳秒mtime/xattr，显式目标2200:3200；正常可见Authentik→Vue→永久SQL/Worker→已装签名Agent/helper。两容器均无网络、只读根、非root执行、准确独占HDD bind及资源限制。

原SQL/实例、两个内核FD、固定argv/cgroup及pidfd绑定首项实际fchown后的原helper，切点finishedThrough=0/startedThrough=0、首项Completed，根保持。只移除准确第一容器CID并立即确认缺失，宿主源完整保留；原管理员正常菜单双端退出，产品2.197秒结束helper，验收器未强杀。Unknown汇总0完成/0已证明修改/0中止/0未开始/18待证明，宿主实际1项已修改；原保护/Root/HDD不因此丢弃或伪造为零修改。

原账号准确删除后，新管理员正常BFF登录，为**另一仍运行的独占容器**通过真实目录plan/确认接口登记一次`work/递归未知保护解除后的目录`。不是手工INSERT或伪造排队；两容器ID不同，目录任务也不重建为新ID。全局发布保护来自已装九方仓储/数据库trigger，未借目标容器消失绕过其他容器写入栅栏。

| Worker运行时实际UTC采样 | 原目录意图版本 | 实际状态 |
| --- | --- | --- |
| 15:38:12.433667 | 2 | Pending、下一次尝试推迟 |
| 15:38:18.723341 | 4 | Pending、下一次尝试再次推迟 |
| 15:38:26.271992 | 6 | Pending、下一次尝试再次推迟 |
| 15:38:33.793723 | 8 | Pending、下一次尝试再次推迟 |

四次样本均保存完整原目录SQL，核对相同intent/Job/Subject/实例/请求/计划、accepted=1，无lease token/expiry、root_started/completed为空、目录publish_barrier=false。Worker保持同CID/镜像/StartedAt且运行；其他任务/终端/锁/发布工作除这两个原任务外均0。主门禁逐轮实际检查目标目录、目录Root回执及私有namespace不存在；原Unknown SQL/Root原字节和完整源树不变。版本至少每轮+2、next_attempt严格前进，所有样本在明确处置之前。

上述历史目标不存在/原树不变的附加样本仅保存布尔标记，独立核验不能从标记重算当时的文件系统；运行时主门禁确实逐轮实读并断言，完整切点/最终物理树和Root原件另有保存。独立进程重算实际保留SQL与当前Root/HDD，不将布尔样本描述为可独立重建的历史文件系统快照。

## 当前管理员明确处置后，原目录任务才发布

新管理员在320×600暗色真实异常处置页定位原Unknown，逐项核对五个dt/dd的0/0/0/0/18及不得推断零修改/成功警告。先“暂不处置”，处置POST0、原SQL/Root/完整源及目录延期状态仍保持；再唯一明确确认HTTP202，不由QA编辑SQL/租约/时钟释放。

| 时序与最终事实 | 实际证据 |
| --- | --- |
| 原Unknown明确处置时刻 | 2026-11-02T23:38:46.298696+08:00 |
| 原目录任务开始实际Root写入 | 2026-11-02T23:38:53.318649+08:00，晚于处置 |
| 原递归任务 | Acknowledged、执行Job Failed、保护释放；原Root仍Unknown/18待证明 |
| 同一目录intent/Job | Succeeded、accepted=1、无待处理Outbox/屏障/租约 |
| 实际目录 | inode/发布version/Root Completed核对，所有者1000:1000、模式0750 |
| 页面结果 | 同一容器详情文件列表可见“递归未知保护解除后的目录”，无横向溢出 |

没有重新提交目录任务、改用新ID或靠预先存在目录通过。原递归Root/HDD/五计数/Subject/计划/Started/源树保持；新Subject只记acknowledged_by。新管理员完成同上下文正常双端退出；附加真实BFF会话也正常退出。迟到固定受限helper Apply原递归请求仅重放Unknown，不继续fchown或改变Root；该探针不是另一次签名RPC。

## 独立核验及保留范围

私有目录：`deploy/artifacts/container-recursive-ownership-faults-8b78fd6fdc0c44b5b74780738d374c9e/`。主/独立均退出0/Passed；独立重读永久SQL/Root与attest HDD、21冻结依赖、四个真实延期SQL样本/时序、原目录计划/发布version/实际inode证明、哈希审计及5张实际320×600 PNG。原递归5审计，目录2审计；递归HDD为1聚合+1完成子项元数据。原文件完整内容及属性、两个准确fixture源/CID清理通过，Root/HDD原件保留；清理后不声称再次实读已删字节。

两例合计9真PNG/SHA、10递归+2目录哈希审计、4份HDD元数据。最终2026-11-02T15:39:54.785778Z只读复核三类临时账号前缀0、任务/终端/锁/八类意图/屏障/租约及递归transient均0；11核心healthy、8数据库平台ID/镜像/StartedAt不变，Worker原CID/镜像保持，Agent PID2542378/MaintenanceHost PID2380322及实际b1fafed来源保持。源/HDD同device2049，不计异盘灾备或NVMe业务验收。

56递归探针边界、8范围与Python -W error/Node语法、diff检查通过；此批只新增验收入口/断言，无产品/schema/宿主授权改动，不借用旧后端/前端全量或CI结果。目录root/0700、文件0600，凭据只在内存/子进程环境，不入Git/日志。保留baseline、21 SHA、原/最终SQL/物理/Root/HDD/历史/迟到、waiting-baseline/blocked/final/physical/proof/independent、原/新浏览器回执、图片SHA和最终idle证明；日志`/tmp/wd-recursive-owner-barrier-{first,independent}.log`。

本批只签收跨容器**新建目录**这一组合，其他八方写入/跨路径/实例/磁盘/回执故障不能据此视为实机完成。ACL、可恢复删除与长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D仍待；17项移出不算完成，整项完成比例不因本批测试上调。NOPASSWD/密码保留，无自动回滚、主机重启、磁盘填满或外部通知。

文档检查：219份Markdown/1324处相对链接零缺失、范围8项及diff通过；前批撤权正文逐字保留（SHA256 `19d90b216b97d8196c34b67b7d6734bda2d82fc5bde962c9220dd6c410172448`），仅前置后续说明，旧失败/版本/未知事实未改写。
