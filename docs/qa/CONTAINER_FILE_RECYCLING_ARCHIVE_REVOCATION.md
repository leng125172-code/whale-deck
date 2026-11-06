# 归档恢复：Reserved 实时撤权两种真实整链

2026-11-07，已发布产品 `f318d9ab130ecab94668acf9754a6a6e448de92c` 上，冻结QA `08873d58b36ce6344bf2fe08025023ca3314514f`，分别用全新资源完成移组和账号停用。承接[正常退出](CONTAINER_FILE_RECYCLING_ARCHIVE_INFLIGHT_LOGOUT.md)，本批不覆盖部署、不改SQL/Root事实、不重放旧Apply，只签这两个Reserved复制前组合。

## 权限变化与原helper停止

每轮使用一个本轮生成的临时管理员、另一个不同Subject的恢复管理员及自有非核心容器/1,048,583字节文件。Owner经可见Vue完成原回收、清理Ready取消/新清理成功、归档Ready取消，再确认一个新目标归档。Root Reserved和五FD、固定apply argv、MainPID/InvocationID/cgroup及原pidfd、SQL实际预留回执/计划/owner/来源、目标与stage缺席、当前HDD字节和历史元数据证明均成立后，才允许权限变更。

`OwnedAuthentikRevocation`只接受严格生成名、canonical Subject及唯一数字PK，核变更前完整身份快照和管理员组，再一次PATCH并GET回读。`group-removal`实际是清空该QA账号所有组（groups=[]），账号保持active；`account-disable`只改is_active=false，保留原组。没有变更任何既有用户，也没有恢复原账号权限来完成验收；临时身份最终由自身fixture准确删除。

变更前和权威回读确认后，原pidfd都必须仍存活；原执行期限在变更前及确认时均至少还剩10分钟，排除自然到期。计时在权限变更前并发开始：

| 场景 | 原helper退出 | 原会话实际界面与身份 |
| --- | ---: | --- |
| group-removal | 5.221454秒 | 原意图403、me200且同Subject/isAdministrator=false，/forbidden和“没有管理权限” |
| account-disable | 5.303301秒 | 原意图与me401/403，WhaleDeck /login和登录标题 |

退出由产品处理，QA不通过SIGCONT恢复Apply。纳秒排序字段由开始时间与pidfd观测elapsed反算，用于核对确认先于观测退出，不声明额外纳秒测量精度。进入Applying及后续的Root切点不按“未复制”签收；Unknown须独立证明，不能在本门禁算false。

## 失权、管理员只读与持久化

原Owner没有调用目标归档取消，也没有发BFF logout POST；目标意图/执行Job两处取消时间均null，两个ID的cancel-requested审计严格0。作为对照的另一个Ready取消意图单独计数。原执行Canceled、Root Aborted、archiveCopied=false、claim清除，无目标或stage，lease/发布保护清除、Outbox已处理。原规划Job保持19字段Ready历史；实际HTTP及可见任务详情19字段执行摘要、SQL和公开receipt一致，原回收与成功purge任务不改成Restored。

失权后原容器文件/归档私有区和dialog消失，归档/清理/回收三类sessionStorage hints清空。停用场景只用实际DOM证明登录页，不截图登录页。原context关闭后，另一Subject管理员通过正常可见登录读取原recovery结果；9个旧Owned/by-key引用404。恢复管理员对产品资源的POST/PATCH/PUT/DELETE为空，原操作恢复写入也为空，不ACK、不取消、不确认或重试；仅正常退出允许的认证POST不算资源写入。

每轮完成9普通/5匿名拒绝和恢复管理员正常BFF/身份源双端退出。独立live读取SQL/Root/当前文件/HDD通过后，仅移除本轮已证明为空的旧private、自有容器及夹具，保留HDD。随后final及第三次当前独立重读通过，旧业务全行hash、Root/HDD和2346保护/11核心保持。第一轮第三次检查及封存完成后才启动第二轮，避免将后续合法增量误当旧基线不变。

凭据仅经私有stdin传给Node并从fixture移除，没有会话注入、route替身、trace、storageState或原生Apply重放。身份API凭据只在内存；退出诊断只保存有限origin/path/status和参数存在布尔值，不保存token/state/query值。

## 检查与封存

完整514项回收root测试零跳过（含10新专项），最后的不同管理员目录适配后10专项与Node语法复验通过；8范围/14 CI合同及3份Python语法通过。本批新QA准确CI `42300680074` 采样时仍in_progress，不签远端完整终态。

两个实际unit均由原invocation的passed journal及正常退出记录确认，不用临时unit回收后的默认systemctl值：

- group：`whaledeck-qa-archive-revoke-group-08873d58` / `413dea24dbf74b6c8acbc0897d9f7f39`，206.824秒。
- disable：`whaledeck-qa-archive-revoke-disable-08873d58` / `31ee6034f9f84173b6aac94618d45ecc`，206.261秒。

root目录0700/文件0600封存，文件数不含manifest：

| 证据目录 | 文件数 | manifest SHA256 |
| --- | ---: | --- |
| `container-recycle-archive-revocation-d3407eb077cd444fa81df143b664c887` | 236，含142冻结源码/13真图 | `cb26e1e4d66713a17f69d67d19c3db7f1082c9ff9f0b2b59173a0c3f08ac724d` |
| `container-recycle-archive-revocation-ff6c488983fa43e4b8a2913adcdba9c0` | 235，含142冻结源码/12真图 | `d76446170681de2ac3d6a744a18c98735521976f39425717b277009447fa451d` |
| `container-recycle-archive-revocation-prep-21555325e0354fd79a9b77c457a4f114` | 144，含142源码 | `b4a73a99817eb41387f9b40eba51de1c597bbf7664348d64ab775c6aadc0d70d` |

只增两轮限定业务，最终61回收/29恢复/31清理/12归档、活动0；Root/HDD151/145、恢复82、目录98、purge56、archive16。f318d9ab、21安装模块及user NOPASSWD保持。源/HDD同device，不能签跨物理盘灾备；本轮通过不抹去上批大文件页面超时或未定因身份页失败。

继续重启/自然租约、写后/Unknown、来源/跨写入、claim暂存/重复来源、长期策略、维护连续性、大文件和退出稳定性及59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
