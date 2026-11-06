# 清理Reserved执行中实时撤权：移组与账号停用

2026-11-06，产品保持9694af1c，验证04bf8d3e。承接[正常退出与新登录隔离](CONTAINER_FILE_RECYCLING_PURGE_INFLIGHT_LOGOUT.md)，两套全新自有容器/真实临时身份分别通过Reserved切点移出管理员组、停用账号。只签这两个组合，不代表重启、自然租约、写后或全部故障矩阵完成。

## 验证准备和边界

四个新门禁文件及8项针对性检查已接入。排除导入TestCase导致的六项重复计数后，139项清理相关Linux root检查、12项临时身份范围检查均执行通过、零跳过；14 CI合同、8范围和Node语法通过。构造SQL/UI/身份检查不充当真实工作站证明。

使用已签收的四FD/pidfd切点：固定apply程序、Root UID、独立传输实例Invocation/cgroup，以及本轮容器根、原执行锁、原来源锁、当前attested HDD数据FD。暂停后验证完整计划、来源Root Trashed、来源marker Reserved、Native Reserved，没有pendingIdentity/unlinkConfirmed，原件和HDD完整保持；SQL Applying的预留/Apply入口记录独立保留。随机传输实例ID不当作持久执行ID。

权威变更仅由OwnedAuthentikRevocation针对本轮生成的QA账号执行：按随机QA用户名查唯一用户，绑定Subject UUID、数值PK、active和管理员组；修改前再次读取，最多一次PATCH，响应丢失也不重试。移组只令groups=[]且仍active；停用只令is_active=false且组保持。凭据仅在内存，原账号不恢复组或active，最终由临时身份机制准确删除原账号和独立恢复账号。

原pidfd退出观察从变更前开始，变更及确认后原pidfd都须仍活着，之后20秒内观察同一进程退出。executionExpiresAtUtc须比权威确认晚十分钟以上，排除临近自然执行期限。原用户不点击退出或取消：owner logout POST=0、清理cancel POST=0，意图与执行Job取消时间null，两个Job ID取消审计0。

读取与收尾使用不同Subject的新真实管理员。先验证原ID/key/来源三个所有者引用均404，只从管理员恢复元数据接口读取原Canceled/Aborted，不向原清理提交任何修改；随后只允许新独立恢复的登记和确认两次写入。fixture原Subject在各阶段保持，专用恢复验证使用副本中的预期恢复Subject，并由SQL/独立核验交叉证明恢复者不同于原操作者，原来源归属未改。

## 两项真实结果

| 场景 | 原pidfd退出 | 原用户可见结果 | 原任务终态 |
| --- | --- | --- | --- |
| 移出管理员组 | 2.715745秒 | 同Subject仍登录、isAdministrator=false，管理读取403，进入无管理权限页 | Canceled/Aborted，purgeApplied=false |
| 停用账号 | 2.746904秒 | 原会话读取及身份被拒绝，回到工作站登录页 | Canceled/Aborted，purgeApplied=false |

移组unit whaledeck-qa-purge-revoke-group-04bf8d3e / Invocation ce6b1168f17e426da66cebaaad68a3b8；停用unit whaledeck-qa-purge-revoke-disabled-04bf8d3e / Invocation 0764214f485d4e9cb0aa94252c4006ff。两者最终active/exited、Result success、ExecMainCode1/ExecMainStatus0。每项冻结132验证源码并匹配已安装helper字节；产品六组件仍9694af1c，没有覆盖安装或Worker/Agent重启，仅暂停该轮严格绑定的原helper。

每项都通过真实Authentik/Vue回收新的1,048,583字节文件，父进程逐步核验来源/SQL/留存/HDD和Ready才放行明确确认。真实权威变更后，两项取消时间均null、双Job审计0、页面cancel和owner logout均0。SQL/执行Job Canceled，Native Aborted、marker Released，purgeApplied/sourceFenced/ownedFence均false，围栏/租约释放、Outbox归零，原规划Job完整Ready历史保持。原件仍留存，HDD仍Trashed，预留与Apply入口事实没有抹除。

原用户刷新后敏感面板和原回收/清理hints均清空；没有注入会话或替身响应。另一个真实管理员登录，三个旧所有者引用404，管理员元数据GET读同一原执行；此时无管理写。仅在父进程重新证明原终态/原件/HDD后，由该管理员选择原来源创建独立恢复并明确确认。恢复SQL actor等于recoverySubject且不等于原Subject；同一原inode返回原路径，Restore Succeeded/Completed、Root/claim/HDD/SQL一致。

独立进程在容器仍在时重开命名空间核验后，才准确移除已证明的测试文件、空private及自有容器，保留HDD。每项清理后父与独立进程重新取得当前attested只读HDD FD，再执行第三次独立最终重读，均通过；captureState仅因专用恢复Trashed→Restored，metadata其他内容、完整数据inode/字节/属性/存储保持。第一项第三次重核和封存完成后才开始第二项。新管理员只发生新恢复登记+确认两次管理写，没有原清理cancel/ack/再次确认。

每项真实Jobs列表/详情16字段对照原SQL，显示Canceled/Aborted和本操作未清理事实，曾记录预留/Apply入口仍显示，无通用重试；各3真PNG，共6图。抽看两种320宽失权页面和桌面Job，原件root私有权限保持，只逐字复制三图到单独user私有目录并核对SHA。新管理员正常BFF/OIDC双端退出；每项另有真实普通9拒绝、匿名5读取401。原用户没有主动logout。

## 旧业务保持与证据

每项分别核验全部旧业务/Root/HDD；合计范围另核验初始43回收/25恢复/7清理完整行SHA、2346保护和11核心healthy保持，仅增加两个确定来源/恢复/清理作用域。最终45回收、27独立恢复、9清理、活动0；SourceRoot/HDD100/101、恢复Root74、目录Root98、PurgeRoot18。上一批失败的完整/partial归档和原生历史保持，未重放旧任务。

移组私有证据container-recycle-purge-revocation-eb1b908b21ab47e4b471889e6c1ade9f，190条文件散列另加manifest，摘要21c9043b93f55d41c05acda0ce5b4a41cf62f93f533dcb3cb577ead4c0356c0d；停用证据container-recycle-purge-revocation-9efc1ea087bc4cffb1f691c3f0e7b7a1，同为190条另加manifest，摘要3ef2f852cf874187c06c997c3402fe578e45747b414fd803c65a3c55e202284d。各132冻结源码/3真PNG，原件目录700、文件600；所有散列逐项重算。

准备与CI证据container-recycle-purge-revocation-candidate-6383217e43fe4bfda258011c641e1ad0，15条散列另加manifest，摘要9ac91d5fa29ffef304e38448e08700bf1b1e0707878cddf9df811060650bf7b8。凭据未进入源码、日志或项目图谱。

## CI原失败与浏览器时序修正

准确86375a52的CI42206649529浏览器第1分片在“登记回执丢失后重载查询原key”失败，131其他用例通过；原日志摘录和原测试保留。本机旧版单用例重复3次通过并不否定该失败。首版收紧容器定位、原key及重载后读取后，第二轮明确捕获key仍为空，结果15通过/1失败，代码/截图/上下文均保留。

原因是重试按钮在hint创建后立即可见，此时登记POST尚可能未到测试服务端；重载会在“服务端接受并丢回执”这个目标故障切点之前中断请求。912bd27b修正测试顺序：等fixture已接受一次登记且丢失响应、重试按钮解除busy，再保存有效原key和持久hint；重载后准确选择同一demo容器，清空旧读取记录后核对原key查询且写入始终一次。没有增加超时、重试或额外查询按钮来掩盖缺失调用。最终该文件两轮构建预览16/16、零重试，类型/oxlint/eslint/格式通过。

这只是显式模拟UI协议用例修正，不能代替上述真实Auth/PG/Root/HDD验收。产品代码与运行发布未变化，仍待新准确CI完整终态，不因本地通过补签旧CI。

继续重启、自然租约、写后/Unknown/共享写入、留存策略/归档恢复、维护连续性和完整59项。源与HDD同device不计独立物理磁盘灾备。正式整项0/59、阶段0/6、工程量粗估25%–35%，酒馆/Live2D功能0%保持；17项移出不算完成。
