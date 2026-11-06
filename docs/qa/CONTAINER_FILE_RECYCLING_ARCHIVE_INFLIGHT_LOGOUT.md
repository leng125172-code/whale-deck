# 归档恢复：Reserved 执行中正常退出真实整链

2026-11-07，在已发布 `f318d9ab130ecab94668acf9754a6a6e448de92c` 上，冻结QA `06bb0087de95e8c85137051f991df4170dbca395`，全新1,048,583字节资源完成归档执行中正常退出分项。承接[执行中取消](CONTAINER_FILE_RECYCLING_ARCHIVE_INFLIGHT_CANCEL.md)，未覆盖部署、修改SQL/Root事实或重放旧Apply；本批只签该组合。

## 真实退出与原进程停止

真实临时Authentik管理员经可见Vue完成本轮原回收、留存清理Ready取消/新清理成功、归档Ready取消，再确认新的目标归档。切点仍要求五个实际FD、原MainPID/InvocationID/cgroup、固定apply argv及pidfd重绑，Root Reserved、无目标/stage、SQL Applying及实际保存的Reserved预留回执一致。原source/purge/marker/claim和当前HDD完整字节、inode、元数据及历史JSON摘要重新核对后才放行退出。

目标归档没有点击或调用取消入口。通过正常用户菜单发起BFF导航POST，再走Authentik退出；观察器要求该实际POST标记到达时原pidfd仍活着，原进程 **5.449341秒**内退出，单调时间满足开始→实际POST→退出顺序。计时开始在HDD核验之后、允许页面动作之前，不用systemd服务排空代替进程退出。

两条正常双端退出receipt分别证明BFF POST302/303、身份源导航、BFF401、身份源401/403及最终WhaleDeck登录页。第二次退出在任务页核验后完成。新会话可见重新登录仍为同Subject，但原回收/清理/归档意图和by-key共9引用均404；退出后原归档读取401。新会话通过管理员recovery读取原终态，不把旧Owned引用重新授权。

## SQL、任务与物理结果

目标意图和执行Job的cancel_requested_at_utc均null，两个ID范围的cancel-requested审计严格为0；Ready取消对照意图另计，不混入该断言。原执行Canceled、Root Aborted、archiveCopied=false、claim清除，lease/发布保护清除、相关Outbox已处理。原规划Job保留19字段Ready历史，实际HTTP和可见任务详情的19字段执行摘要与SQL、公开receipt、双来源链一致；没有重新预检并重试按钮。

原回收/成功purge的SQL和Root保持，HDD仍Purged且原字节/身份/属性完整，不改写为Restored。不以绑定null/false替代物理读取：独立live确认目标和stage均缺席后，仅移除本轮已核对的空旧private与自有夹具。final和第三次独立当前重读通过，HDD保留，旧业务全行hash、2346保护、11核心和21安装模块保持。普通9项/匿名5项拒绝通过。

密码仅经私有stdin传入Node后从fixture移除，没有会话注入、route替身、trace或storageState。新增退出诊断仅保存有限的阶段、origin/path/status及id_token_hint/post_logout_redirect_uri存在布尔值，不保存token、state或query值；新门禁只对WhaleDeck非登录产品页截取失败图。

## 首轮失败保留

首版 `dfd44efd` 的全新资源确实取得Reserved，实际BFF POST时原pidfd仍活着，5.465098秒退出；但双端退出检查随后失败，原失败图停在Authentik空登录页，未签整链通过。另行以SQL实际回执、Root/文件/HDD核对原任务Canceled/Aborted/未复制及取消审计0，再准确移除自有夹具，HDD和原失败均保留；该收尾明确标记SQL-derived，不冒充API/UI验收。

只补安全观察和截图边界后，用另一个全新资源复验。新一轮记录显示end-session携带ID token hint和post logout redirect，实际经过身份源、signout callback返回WhaleDeck登录页；完整两次退出和后续核验通过。没有修改产品认证逻辑或放宽正常退出合同，首轮原因未确认，不声称已修复认证根因；不能据一次成功签稳定性或完整故障矩阵。

## 检查、证据与范围

修订后完整504项回收root测试零跳过，含11新专项；范围8项、CI合同14项及3份Python/1份Node语法通过。上批QA45ac608a准确CI `42295626521` 固定12路全success；本批新QA准确CI完整终态仍待，不因本机通过声称远端全部通过。

真实unit `whaledeck-qa-archive-logout-06bb0087`、invocation `1e07c3500c5741cea4dae21f8659fc88` 的journal报告passed且正常退出，耗时193.626秒；单独按原invocation匹配passed条目，不使用临时unit回收后的默认systemctl值作退出依据。

私有证据均root目录0700/文件0600，以下数量不含manifest：

| 目录 | 文件数 | manifest SHA256 |
| --- | ---: | --- |
| `container-recycle-archive-inflight-logout-e0d05d9d11c845ddbe14383d6a9f5849` | 240，含138冻结源码/13真图 | `c54431bb474a112f4456f4b6411793ae0ab9f7d441c4d9a47cc90edc55472722` |
| `container-recycle-archive-inflight-logout-7715661e8ef24c47a1b0f9bcaeaa70c9`（首轮失败与收尾） | 208 | `e2772c9aa33816ea4f6892769231fbb64ab9f01385f57613553f6c5ffdfbe0c7` |
| `container-recycle-archive-logout-prep-1156b970fa934b499a039990f01a9f0e` | 140 | `0409ef551b6b5b20aca23768f3a06d7f751c64ef8aa21d19b6795389b70522d3` |
| `container-recycle-archive-logout-observation-c7fa568b98714fddb8b6b5fec7363218` | 141 | `59e207a66bef6b94df9b25ac161f8d250841458c3ed519327c689d78466f5f93` |

含首轮保留记录后，总59回收/29恢复/27清理/8归档、活动0，Root/HDD145/141、恢复82、目录98、purge52、archive12；只增两轮限定记录，f318d9ab和user NOPASSWD保持。源/HDD同device，不签跨物理盘灾备；1MiB用例不抹去上批大文件页面超时。

继续实时撤权、重启/自然租约、写后/Unknown、来源/跨写入、claim暂存/重复来源、长期策略、维护连续性、大文件耗时及退出稳定性与59项。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持，17项移出不算完成。
