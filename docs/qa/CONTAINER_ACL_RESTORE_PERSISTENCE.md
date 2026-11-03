# ACL历史恢复：内部业务与永久化候选，真实恢复待验

2026-11-03。产品实现提交`ffd08a894428e7ff4dd2951c90ae1d166d9732db`，验收记录器修复及冻结候选`47e3ba1bada600a1625ce4d6d9fa3196681d1a9a`。本批接通内部恢复业务、现有Worker调度与永久SQL保护，**没有管理员恢复API、Vue恢复入口或实际发布，没有进行真实ACL恢复写入**。此前[历史只读常规整链](CONTAINER_ACL_HISTORY_REAL_UI.md)保留自己的实际来源和验收范围，不能拼接成本批恢复通过。

## 恢复语义与当前依赖

请求只允许准确当前容器/启动实例/非空路径、captureId、预期历史digest及当前文件版本，不接受调用方提交历史ACL正文。服务端通过已有1.52历史读取能力取得完整原始快照；根据`Entry.ChangedAclType`派生新的ACL修改命令，**每次恢复access或default其中一种**，不是同时回放整份元数据。目录空default表示明确清空；另一类ACL、所有者、文件内容和子目录保持原有写入器的保护语义。本批不递归，不恢复原inode/UID/GID/mtime，不代替内容恢复或完整权限验收。

新请求创建新的`ContainerAclIntent`及规划Job；明确确认后创建独立执行Job，继续使用原ACL JobType、Outbox、专用Worker、90秒自然租约、原ID回执观察/Discard及Unknown保护。历史capture不作为新的执行ID重放。现有Root写入器仍须先留存写入前的当前ACL，再执行目标修改；本批没有改Root写入内核，实际新恢复产生的HDD备份仍待实机证明。

实际依赖沿用.NET10/C#、PostgreSQL/EF迁移、实时Authentik管理员及原登录、Agent签名历史Read和ACL Plan/Apply/Inspect/Discard；未增加新protobuf、Agent minor、root能力或任意命令/路径入口，API/Worker不挂Docker socket。没有新生产依赖包；临时EF脚手架工具10.0.4仅装在开发`/tmp`，未加入产品包或仓库。

## 权限、来源与失败恢复

完整来源快照经严格字段/位置/摘要/模式/条目验证并深复制，序列化为`restore_source_json`，纳入请求HMAC及其计划/执行绑定。返回对象或上游数组的后续修改不能替换已保存意图。普通ACL请求在来源为null时保持原有规范JSON逐字相同，旧请求MAC不因新可选字段失效；普通与恢复请求不能借同一幂等key互换。

登记前、规划前、确认前、第一次实际Apply前重新核验同一完整来源；RPC前后均校验实时管理员与原登录。来源缺失或摘要/完整元数据变化时，新规划失败、确认拒绝；首次写入前发现变化则对新执行ID执行Discard，以Root实际回执为准，仅Aborted记来源变更失败。NotFound/Aborted转换为固定来源变化错误，Unavailable/PermissionDenied/Unimplemented保持原依赖或权限失败，不把依赖中断冒充“历史不存在”。确认错误继承InvalidOperationException，沿用现有HTTP 409映射；管理员恢复端点仍未添加。

原执行已有Root回执时先Inspect并对账，不因历史离线而重做或拒绝读取已完成事实；取消、过期、已开始执行沿用原Discard/观察路径，不另开恢复写入。已登记的同key重试返回原快照和原时限，不因HDD不可用而生成新意图或续期。恢复来源不可用不等于当前文件未被修改，Completed/Unknown等原回执事实优先。

## 永久存储和共享保护

EF迁移`20261103085710_ContainerAclRestoreSources`仅扩展原`container_acl_intents`的可空text来源列，新增一个CHECK（总18个）。不可变PL/pgSQL校验函数限制快照预算、准确字段、时间/模式/数量、CID/实例/路径、单ACL类型、原完整ACL合法性及派生目标条目；存在plan时类型必须与历史File/Directory一致。独立BEFORE UPDATE触发器禁止任意替换、移除或后加来源；原请求/计划/回执不可变触发器继续保留。

降级迁移只要存在任何恢复意图（包括终态）就明确拒绝，错误为`container_acl_restore_downgrade_requires_no_restore_intents`；不能靠删除来源列丢失审计依据。仍复用原共享事务锁41307341、发布屏障、层级路径和预算保护，不创建可绕过旧文件/目录/复制/移动/权限/所有者及递归写入的独立通道。

恢复审计使用`containers.files.acl.restore.*`固定动作，Job和审计详情只有`historyRestore`标记、来源SHA256和原有目标hash，不放完整ACL、原capture、宿主路径或密钥。登记、Job/Outbox与审计同事务；审计失败整笔回滚。原普通ACL动作与四字段摘要顺序保持不变。完整ACL仅在受保护意图列及授权业务响应中留存，不进入日志或任务摘要。

## 测试事实及边界

最终冻结检查和运行边界见下方收尾记录。候选已通过4361项Unit和463项Integration，另7项默认原生测试跳过；新增13个内部业务案例、5个SDK错误映射案例已包含在总数内，不重复相加。

内部业务测试使用显式内存仓储/身份/Agent夹具，覆盖新计划/执行、单ACL类型与空default/无变化、数组冻结、同key/当前版本冲突、来源三切点变化、旧普通请求MAC逐字兼容及完成后历史离线对账。SDK测试验证签名调用和错误分类，仍不是安装态UDS与HDD恢复。

原生PG门禁在两个准确命名的隔离数据库运行冻结Git源码，使用不可变SDK镜像、只读源码、无Docker/Agent socket、cap-drop和临时0600连接环境文件。覆盖真实EF迁移、旧普通请求null兼容、来源MAC/不可改/非法字段、规划确认执行持久化、两Job和专用审计、同路径共享拒绝、审计失败事务回滚及带来源意图拒绝降级；历史快照、Root回执和时钟仍是合成输入。不是生产表迁移、实际Worker进程中断或当前ACL/HDD恢复证据，外部通知发送为0。

## 保留的失败

初次EF生成普通迁移采用块namespace，Release报IDE0161；按仓库file-scoped格式修复后通过，没有关闭分析器。EF工具开始未安装，只在临时目录安装与项目相同10.0.4版本，未改变运行环境工具链。

第一次冻结`ffd08a8`的隔离PG已跑到TRX 1执行/1通过/0跳过并出现本批边界标记，但验收器写新增`nativePhases`时被公用记录器字段白名单拒绝（ValueError: Non-public native status field）。因此**整体状态仍Failed**，不人工改签Passed；原status `dispatch-0ba72ff719fa45e693857bdd945cf1a8.json`保留，准确临时资源清理已确认。修复为最多128项、每项仅1–160个小写字母/连字符的公开阶段标记，新增拒绝原始输出/换行/非字符串/超长/过量测试；冻结47e3ba1后重新独立运行。

## 未验收及下一步

补齐管理员恢复API和Vue：所选捕获来源、单ACL类型、历史值与当前差异、明确确认、新操作与当前原件备份提示、幂等刷新/取消和异常恢复。然后标准覆盖部署、生产迁移，使用新独占资源通过正常Authentik→真实Vue→API/Worker/PG→Agent→实际ACL/当前原件HDD与独立核验，包含普通/匿名/撤权/退出、源消失/变化、依赖中断及原执行对账。此前只读成功不能替代恢复整链。

历史查询进行中故障、ACL递归/恢复完整矩阵、可恢复删除/长期清理、维护安装态故障及其余59项/UI/C#酒馆/统一供应商/AI Live2D仍待；17项移出不计完成。整项完成率不提升，master尚未交付。开发sudo按授权保留，不自动回滚dev、不重启主机。

## 冻结结果和运行边界

- 47e3ba1全解决方案Release重新构建：0 Warning、0 Error；本地API `--build-info`来源准确为47e3ba1，随后完整Unit 4361/4361、Integration 463通过/7默认原生跳过。
- 原生第二轮status `dispatch-a92ba06486734aaeb43633c0df5d1e27.json`：Passed，executedTests=1、passedTests=1、cleanupConfirmed=true；78个公开阶段含`container-acl-restore-source-mac-immutable-audited-new-jobs`。不能将TRX一个大场景等同为78个独立测试。SDK镜像为sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa。
- 3项记录器/8项范围测试通过。没有前端改动或本批新页面验收，已有只读页面结果保留其原验收来源。
- 独立只读前后比较：11核心完整ID/镜像/StartedAt/运行状态相同且全healthy；Agent684268、MaintenanceHost684263与来源08b2bb8均不变；全部预检计数0。生产恢复来源列不存在、CHECK仍17，明确没有迁移/部署新候选。
- 原件在忽略的root私有`deploy/artifacts/acl-restore-persistence-candidate/`：冻结源码、Release/测试/原生失败和成功日志及status、前后运行边界和SHA清单；不存环境变量或连接文件。历史只读QA原文与目标第1节保持。

收尾本地校验：目标第1节逐字摘要不变、3份此前历史QA全文SHA保持，7份相关MD的817条相对链接有效；27份冻结候选源码和41份静态制品SHA及root:0700/0600权限核验通过。项目阶段保持active，不把候选或隔离PG提高为整项完成。
