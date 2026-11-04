# 递归ACL恢复预览真实跨页门禁

2026-11-05，dev；承接[五场景常规真实恢复整链](CONTAINER_RECURSIVE_ACL_RESTORE_REAL_UI.md)。产品运行保持eeb24dd，本批不发布产品、不重启宿主或Worker，不签完整59项或整个权限能力。

## 候选范围与检查（执行前记录）

新增独立的26对象恢复预览门禁：一份全新自有无网络/非root/只读根容器，根目录加25文件（含中文末项），65536+7全字节数据、异构owner/mode、真实命名ID0/权限0 ACL、根default与普通xattr。复用已发布并有SHA256清单的d15b698分页捕获探针，实际产品/helper与eeb24dd核对；每次使用新的operationId，不重放任何旧捕获。来源捕获通过签名RPC，不冒称由本批业务页面或Worker生成。

真实管理员在Vue选取新来源，登记预检Ready、原opaque引用重载、相同key返回原意图；恢复预览按offset 0→25→0→25、take固定25，严格核对25/1/25/1条原始前像、ID0与mask、末页根目录、ACL及当前来源摘要、DOM无跨页残留。每页后父进程再读实际源字节/九字段/ACL/xattr、原Root/HDD和真实Ready SQL、读取审计，再允许下一页。

最后撤销取消确认框并核对无POST，随后明确取消；不按确认恢复。验证真实取消SQL/双Job状态、确定性执行ID下Root/HDD缺席，原物理状态与所有旧记录保持；实际普通/匿名拒绝、原上下文正常双端退出和临时身份清理。清理前独立进程重读26对象和保留记录，确认后才精确删除源；清理后独立验证仅重读保留证据，sourceBytesRereadAfterCleanup=false。

10项新分页专项、10项关联历史分页、10项业务证明及8项范围检查通过，均0跳过；Python/Node/YAML和diff检查通过。负例覆盖来源/请求范围/顺序、页标志/跨页残留、根对象/ID0/default不匹配和证据目录逃逸。首轮Node语法检查发现query标识符重复声明，改为historyQuery后通过；离线修正，不涉及真实操作。候选编写时修正installed函数被路径变量遮蔽，不放宽安装或源验证。

候选冻结时真实运行尚待，实机结果见下节；以上自动化检查本身不是实际跨页或磁盘签收。只有预览和写前取消，不以此签26对象恢复写入；恢复进行中Partial/Unknown/撤权/重启/来源异常和跨写入矩阵、删除/长期清理、维护连续性及其余59项继续。17项移出不算完成。


## 真实执行与独立签收

冻结门禁 `272474c31e3812fd94edc8a1c98394f7d49c00a1`，78份验证源码逐Git/工作区哈希一致；复用原发布探针d15b698，二进制SHA256 `5a5b0ce4a436bb73bd3730b90be1906db06a868531c3814e174f74885912d927`及目录原清单通过。实际产品eeb24dd、两宿主PID1379812/1379807及安装helper一致。原生产54普通意图/49执行/103Job、恢复6意图/5执行/活动0、1574保护文件为启动基线。

只启动一次 `whaledeck-qa-restore-paging-272474c`，Invocation `c1a7fac804554886ab8ab2ba8c47f6a6`。成功终态Result=success、SubState=exited、ExecMainCode=1（CLD_EXITED）、ExecMainStatus=0；准确状态及journal归档后才停止已退出QA unit，未停止产品服务。

新26对象通过真实签名RPC捕获Completed、26实际修改，原26子项和聚合54份Root/HDD原始字节保存并逐页重读。来源捕获不新增普通业务意图；真实Vue只新增一笔恢复预检/取消，未确认恢复，也无26对象恢复执行证据。

同一真实管理员原上下文完成选源→Queued登记→Ready→原引用重载→恢复预览下一页/上一页/再次下一页：offset **0→25→0→25**，take均25，返回 **25→1→25→1**。末页仅根目录，首/返回页无根且包括中文末项；每一项来源ACL、ID0/权限0/mask、类型及摘要与原HDD逐项比较，DOM项数严格对应当前页。每页实际GET均200/no-store，绑定原capture/完整历史摘要/实例/路径/offset。当前来源库存扫描2次List，选源1次Read加恢复预览4次Read，共7条实际PG读取审计与每阶段归档精确一致；不是只凭截图签收，也不把库存扫描计为恢复预览翻页。

7份实际PNG保存：selection、4页、取消和普通拒绝；320×600暗色前进/末页与1440×600亮色返回/再次前进通过，导航/关闭/确认/取消可见且无横向溢出。人工查看暗色末页“前像26–26 /26”和亮色返回页中文项/ID0/mask。截图查看临时副本已精确删除。

同key重复登记仍原意图；取消确认框先“继续等待”，SQL仍Ready且取消POST0，随后明确取消，最终Canceled。页面登记1/取消1/确认0/上传0，原确定性执行ID的Root/HDD/子目录均缺席；恢复SQL真实双Job投影只规划Job成功、执行Job空，审计3条绑定原主体/资源/意图。当前物理26对象仍为来源捕获后状态，普通xattr/default/全字节/九字段保持，不把取消当作回滚。

管理员及普通身份均在原上下文完成正常BFF/Authentik退出；普通6GET（4恢复/2历史）全部403，普通页面私有请求0，普通历史/恢复审计0；两上下文退出后相同6GET全部401。清理前另进程实际重开26对象全字节、原Root/HDD、真实PG审计和原取消SQL，physicalTreeReread=true且身份已清理；父级再次验证后精确移除自有文件树/容器。清理后另进程退出0，physicalTreeReread=false、sourceBytesRereadAfterCleanup=false、sourceAndContainerAbsent=true，保留事实边界。

普通业务仍54意图/49执行/103Job；恢复现7意图/5执行/活动0，仅新取消。普通保护1628（旧1574逐字节九字段保持）；来源Root1246，恢复Root82/HDD76全量与批前保持，目录Root94保持；11核心ID/镜像/StartedAt及healthy与两宿主版本/PID保持。原源/HDD同device不算异盘，旧维护连续性失败不覆盖。

私有证据 `deploy/artifacts/recursive-acl-restore-paging-d72717370a4d4739bca0ea0e687f0e21/` 含真实RPC/HTTP/SQL、54原始记录、7PNG、保护基线、清理前/后独立退出日志与成功unit回执。96文件已生成并逐项验证SHA256清单；清单SHA256 `1d92d0d6f2ef07def2573e4e17a6cdf09d04c7777e170477ca846c99e8122ee9`。原五场景证据两份已封存清单不改写。

## 未验收范围

本批只提升“恢复预览真实跨页”分项证据，不提升59项完整完成数。26对象实际恢复写入、恢复进行中Partial/Unknown、写后取消/撤权、Worker/Agent进程中断、来源异常/实例变化/跨目录写入及跨管理员处置尚需恢复类型专属验收；不能用正常递归ACL旧故障矩阵替代。可恢复删除/长期清理、维护入口完整连续性及其余59项继续，17项移出不算完成。


收尾：8范围复跑通过；本轮修改MD的728个本地链接存在、缺失0，原常规整链QA正文按Git原字节前缀保留，CI YAML解析与diff检查通过，Git索引1000:1000/664。候选272474c已普通推送成功，无部署重放；新文档提交的CI未核实终态，不声称新CI已全绿。

项目图谱事件 `whale-deck.container-acl.272474c.recursive-restore-paging` 首次传输失败，同eventId重试取得project/key/event匹配且duplicate=false成功回执，pending移入synced；container-acl保持active。


后续（2026-11-05）：新Ready原上下文正常退出、同Subject新登录隔离与原五分钟期限自然到期已独立实机签收，见[退出与自然到期](CONTAINER_RECURSIVE_ACL_RESTORE_READY_LOGOUT.md)。本报告原跨页/明确取消事实保持，不改记为当时已验证退出中的Ready。
