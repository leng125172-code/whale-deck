# Prepared写后：Worker自然租约与Agent重启、Unknown/管理员ACK

2026-11-07，产品 `f318d9ab130ecab94668acf9754a6a6e448de92c`，冻结QA `64ac7caa73c2d908201a627496789193c9ca7082`，两套全新资源分别通过Prepared写后Worker SIGKILL/自然租约和Agent重启。承接[Prepared页面中断/ACK](CONTAINER_FILE_RECYCLING_ARCHIVE_PREPARED_UNKNOWN.md)及[Reserved重启](CONTAINER_FILE_RECYCLING_ARCHIVE_RESTARTS.md)。本批仅签这两个写后组合，不签全部Unknown/发布后场景；无部署、主机重启、SQL/时钟修改或旧Apply重放。

## 故障前边界

每轮真实Owner经Authentik/Vue完成新原回收、清理Ready取消/新清理成功、归档Ready取消，再确认目标归档。原Root实际Prepared，完整暂存新inode及字节/SHA256/属性已核，目标未发布；原SQL预留回执、Apply入口、原pidfd/Invocation/cgroup及八个互异FD一致。服务操作前再次要求原Root等于冻结Prepared记录、原进程T/t停止、八FD仍绑定；不能套用Reserved/目标与stage均缺席的写前证明。

固定服务控制只允许原Worker容器SIGKILL/同ID与镜像启动，或固定Agent服务重启/同版本新PID。15类文件表（包含archive及残留lease expiry）、其他Job/终端/主机锁及运行中的WhaleDeck临时服务都须为空，只豁免目标规划/执行ID、当前helper和当前已核MainPID/Invocation的QA主单元。维护进程、其他容器/镜像/旧业务数据不在变化白名单内。失败路径精确终止绑定helper、不SIGCONT，并恢复本轮实际尝试的固定服务、保留未决夹具。

## 两种不同收敛语义

| 场景 | 原helper退出 | 原任务及附加事实 |
| --- | ---: | --- |
| Worker强杀 | 0.124386秒 | 同容器/镜像恢复；原lease真实等待86.697693秒/41个到期前完整样本；expiry后先记录Unknown，再由不同管理员ACK |
| Agent重启 | 0.012103秒 | Agent PID改变且同f318版本、维护PID不变；152样本全200/healthy/Ready/no-store；活Worker保存Unknown，再由不同管理员ACK |

Worker场景在原lease期间核相同token/expiry、Applying/Running/barrier与全部不可变来源/计划/签名/预留/Apply字段、Root与claim原始字节/hash、完整stage物理快照、source/purge/marker及当前HDD。Root允许Prepared或Unknown但其他字段必须与原cut相同，claim仍属于原操作，目标缺席。跨越expiry的读取不计到期前样本，样本间隔上限5秒。Unknown最早result审计的时间必须不早于原expiry，不能用很晚的ACK完成时间替代此证明。

Agent场景的Worker仍存活，可及时保存Unknown并清lease，不强迫它维持旧lease到期；仅签固定重启/原pidfd退出/维护观测与最终stage/claim/HDD保持，不签自然lease窗口。Unknown等待上限从80调到120秒，只放宽观察时限、不改业务期限；Worker自然等待先完成，再进入该收敛等待。

目标意图/执行Job取消时间均null、双ID cancel-requested审计0，结果前Owner仍为同一管理员且未logout；Ready取消对照另计。两轮都保持Unknown/Running、复制null、Root Unknown、原claim/完整stage、barrier与1个未处理Outbox，不能按写前重启记Failed/Aborted/无写入。独立核Unknown审计→父级观察→不同管理员ACK审计的顺序，两个审计各恰1条且actor分别为原Owner/恢复管理员。

## 页面、处置与准确收尾

Owner故障结果后正常BFF/身份源退出，另一Subject管理员正常可见登录；旧Owned引用404，19字段实际Jobs显示Unknown/null无重试。先“暂不处置”且管理写0，父级重读证明原Unknown快照不变，再唯一ACK；原任务Acknowledged/执行Job Failed、Outbox0，复制事实仍null、Root/claim/stage/HDD不改。两条真实双端退出receipt、每轮9普通/5匿名拒绝、权限/CSRF/Origin门禁保持。

独立live之后，仅复用已通过的QA-only三原operation锁/父-stage-output FD及完整内容身份检查，精确移除本轮已知stage；不是产品Unknown清理，不是原子inode条件unlink。Root、claim和HDD保留，历史parentAfterStage不回写。然后准确删除本轮空旧private/容器夹具，final及第三次当前Root/HDD读取均通过。每轮第三次检查和封存结束后才开始下一轮，合法后续增量不冒充旧全局基线不变。

原件/HDD仍Purged、原清理Completed，不重建原inode或改成Restored；源/HDD同device，不签跨物理盘灾备。凭据仅私有stdin，未进入argv/env文件/证据；无route替身、session注入、trace/storageState或登录截图。

## 检查与回执

完整562项回收root测试零跳过，9新专项/8范围/14 CI合同及4 Python/1 Node语法通过。初次测试导入旧TestCase导致重复计数，已改module别名，最终专项9；最后80→120观察时限变更后专项/语法复验通过。无实机失败或业务断言放宽。上批fe2063a1准确CI `42309644527` 固定12路success；本批新QA完整CI终态仍待核验。

- Worker unit `whaledeck-qa-archive-restart-64ac7caa0001` / invocation `a662627ac53648acab8ac7636d8d281d`，286.249秒。
- Agent unit `whaledeck-qa-archive-restart-64ac7caa0002` / invocation `90fb643896e143b98e9bfad4f6e6e816`，232.751秒。

原invocation journal各报告passed并正常退出，不使用临时unit回收后的默认systemctl值。root目录0700/文件0600封存，数量不含manifest：

| 证据目录 | 文件数 | manifest SHA256 |
| --- | ---: | --- |
| `container-recycle-archive-prepared-restart-5dbbdb085e144de48dc46ec25755aadc` | 242，含149源码/12真图 | `9c0de4c5bf9320010111aa36696bcd19756c22d0632fe0063bd6a095e5a9b524` |
| `container-recycle-archive-prepared-restart-b203aba4984e46d2bd3482449fd3b801` | 239，含149源码/12真图 | `a3dbf3c49fb349c87fe3342b0c550fd9bd3b09363afcb4af88ad0559aad8259c` |
| `container-recycle-archive-prepared-restart-prep-b7fc198415c44c8c893efddc6b46c720` | 151，含149源码 | `25a633d30f6e61282b4ce07a88a7865bef6abe316a37ea631ae34efbb3b5a30a` |

仅增加两轮限定记录，最终67回收/29恢复/43清理/24归档、活动0；Root/HDD169/157、恢复85（新增保留claim）、目录98、purge68、archive28。只允许本轮Worker StartedAt和Agent PID变化，旧业务全行hash、2346保护、11核心其他实例、21模块/f318及user NOPASSWD保持。

继续其他写后权限变化/正常退出、发布后/真实绑定不确定性、来源/跨写入、claim/重复来源与长期策略、维护连续性、大文件/退出稳定性及59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
