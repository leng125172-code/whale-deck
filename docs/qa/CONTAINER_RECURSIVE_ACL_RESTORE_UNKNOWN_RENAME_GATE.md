# 首项恢复后固定重命名Unknown与跨管理员处置实机门禁

2026-11-05，承接[Unknown业务与隔离PG候选](CONTAINER_RECURSIVE_ACL_RESTORE_UNKNOWN_BUSINESS_PROOF.md)。验证源码冻结 `d4e484aa5ca405ca611430c3660319d5f369c5ad`、产品仍为 `34153d2`；一次全新资源门禁已完整通过。只签26对象access首项恢复后 `item-01.bin`→`renamed-item-01.bin` 这一形态，不重放或改判旧Partial任务。

## 真实执行与原件事实

新建自有容器及26对象树，安装态Agent经真实签名RPC完成一轮来源捕获；真实Authentik管理员登录Vue，查看来源历史与第一页25项恢复前像，生成Ready并核对原规划Job。原意图/幂等键/登录绑定/来源/计划/容器实例/Root/HDD均在确认前交叉核验。Worker只在固定维护门禁内停启，恢复确认由原页面完成，没有取消请求或产品故障钩子。

内核pidfd、进程start ticks、精确cgroup/Invocation及两个文件描述符绑定原helper；观察到聚合Applying/startedThrough=finishedThrough=0后暂停，原首项 `item-00.bin` 已真实恢复且Completed子记录和HDD前像完整，其余25项保持。原页面实际观察Applying后，门禁仅在该新自有树内以固定目录FD和RENAME_NOREPLACE原子重命名 `item-01.bin`；拒绝覆盖已存在或检查后晚到的目标，不改ACL、不删除文件、不把路径改回。

恢复精确原pidfd后，helper在 **0.248391994秒** 退出，门禁没有杀helper。产品记录Unknown/RESULT_UNKNOWN，聚合五计数 **0/0/0/0/26**、空outcomeDigest、历史已验证、围栏保留、无取消/完成时间及活动租约。独立原件证明的首项真实改变 **1** 与聚合计数分别保留，绝不将0解释成零修改，也不将1填回聚合。

原路径缺席、新路径保持原inode/device/mode/UID/GID/link/size/mtime/内容/ACL/属性；只允许根目录因重命名变化的目录元数据，其余对象完整不变。cut和Unknown之间的聚合HDD、首子Root/HDD三件原始字节相同，来源全清单/摘要与原执行绑定保持。

## 实际页面、身份与处置

原管理员Vue页面查询同一原任务Unknown；Jobs列表/详情的26字段、23标签与SQL/原计划/回执逐一匹配，“回执已证明修改”显示0，待证明26及来源原修改26分开呈现。Unknown计数限制说明在真实DOM按全文核对；本次13张截图未单独框定完整说明段的可视区域，不把DOM文字检查扩称该段完整截图验收。

原上下文正常完成BFF/提供方双端退出，9个私有GET均401。另一真实管理员正常登录，原owner查询404；先在异常处置页选择“暂不处置”，ACK次数0且SQL/文件/Root/HDD完整不变，再一次明确点击“确认处置并释放保护”，请求202、原意图Acknowledged、原执行Job Failed/phase Acknowledged，围栏释放且原Unknown/五计数/原件保持。原规划Job仍是Ready历史；不是成功、回滚、续做或再次恢复ACL。

处置者与原用户不同，新增唯一unknown-acknowledged/Unknown审计；原plan-accepted/plan-result/confirmed/result审计及原执行绑定正确，无cancel审计。第二管理员正常双端退出/9个GET401。第三个普通用户正常登录后容器/任务页面拒绝且私有UI请求0，9个GET403、3个写POST403、恢复域审计0，再正常双端退出/匿名GET401。三主体不同；临时身份均删除，未保存凭据或登录token。

## 独立复核、清理及生产保持

准确unit `whaledeck-qa-restore-unknown-2591bf9f`、Invocation `6eefb9b519664469a3bd7f670cb0e861`，exited/ExecMainCode=1/ExecMainStatus=0/Result=success。独立进程在清理前重读活容器与完整重命名树、来源及Root/HDD、当前SQL/页面回执和源码闭包；清理后另一个独立进程验证源目录/容器与临时身份缺席、历史原件及最终保持结果。清理后没有声称重新读取已删除的源字节。

异常资源保留标志一直保持到独立live证明及完整重命名清单精确删除之后，不能只因UI结束就清空；失败时保留现场。实际清理逐文件核对inode与清单后删除，不递归清空目录。原容器精确移除、源目录缺席，原历史永久保留。

| 对象 | 本次结果 |
| --- | --- |
| 保护文件 | 旧2292文件完整保持，新增来源记录后共2346 |
| 来源Root | 1910→1964，旧记录保持 |
| 恢复Root / HDD | 252→257 / 166→169，旧记录及本次前缀原件保持 |
| 目录Root | 94，保持 |
| 恢复永久业务 | 23→24意图、18→19执行，活动0 |
| 普通递归ACL业务 | 62意图/56执行/118 Job，保持 |
| 核心与版本 | 11核心healthy/身份镜像保持；仅原Worker停启且StartedAt改变，两宿主与产品34153d2保持；没有覆盖发布 |

## 测试与封存

新增14项流程/物理安全测试，关联212项root工具通过、零跳过；Python AST、3个Node语法及diff通过。覆盖旧Partial与Unknown模式互斥、非布尔计数/同主体/权限拒绝、失败在Ready前保留资源且不启动Worker/重命名、页面无认证/响应注入、精确scope/符号链接/硬链接/过期inode/内容变化拒绝、检查后目标晚到不覆盖，以及独立live/精确清理前不能解除保留。文件/目录测试使用真实临时文件和显式scope/ACL接口替身，不算工作站验收；上面的单次实机证据另行绑定。

冻结前复核补齐主stdout的Unknown模式标志，改为内核不覆盖重命名，并将资源保留解除移到独立live及精确清理后。离线生成verifier时一次保守断言误匹配正常logout数据键，阻止了文件生成；修正后才编译/测试/冻结，没有触发实机或伪造失败stderr。本次新资源实机门禁一次通过，没有第二次恢复或重放旧操作。

私有目录 `recursive-acl-restore-unknown-rename-cfa989fabf154683ae88904044d72318` 封存 **308文件**，清单 `EVIDENCE_SHA256.json` 摘要 `cbb76fc2c5c591d52275b3f0c964baaa1caa372c9bc32cc1d7ba0e2021c0176d`，逐项大小/SHA256通过。含127文件冻结验证闭包、签名RPC/真实13张320暗色PNG、原始边界/三身份页面/SQL/Root-HDD/完整物理及两次独立核验、unit回执/日志和实际后置保持。人工检查了实际计数与元数据图；完整Unknown说明段独立截图边界如前。旧145业务候选、32发布、30中断封存逐项保持。

本批未签直接迟到Apply RPC、容器消失/实例变化、其他Unknown原因、其他写后撤权/重启及跨写入组合，也不重算前像翻页用例。继续剩余故障矩阵、可恢复删除等59项目标；主任务和阶段完整签收、整体25%–35%工作量粗估、AI子计划0%保持。

收尾：准确d4e484a已推送dev，范围8项、2092本地Markdown链接/缺失0、diff及308文件封存再核验通过，两Git索引仍归用户所有。项目图谱事件whale-deck.container-acl.d4e484a.restore-unknown-rename-real-gate取得projectId/key/eventId匹配、duplicate=false回执，pending移至synced、阶段active。本批未取得准确d4的整条CI终态，不将本地/实机通过冒称CI全成功。
