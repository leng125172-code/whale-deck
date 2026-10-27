# 容器历史内容恢复：业务、Worker 与页面候选

## 当前安装态：3bac49d 已通过原路径内容恢复实机门禁

2026-10-27 正常 dev 覆盖 `whaledeck-dev-3bac49d.service`，0/SUCCESS，4分9.686秒；Agent/MaintenanceHost/API/Worker/Gateway/Migrator及实际HTTP均为 `3bac49dc734f2510ac12fbc93ceb4bbb34faa393`，minor42不变。运行库/PKCS10.0.13、systemd/权限/UDS/固定helper/动态入口校验通过；生产实际迁移为 `20261027074344_ContainerFileRestorationSources`，没有自动回滚。

同提交冻结 SDK 在隔离真实PG完整运行1执行/1通过/0跳过，状态 `dispatch-0c0cf9c4d58742a188a9ad95b5d94976` 及 `/tmp/wd-container-restore-native-3bac49d.log` 保存；临时库与容器清理确认。先前0632200测试查询失败保留，未计通过。

实际正常管理员BFF先上传生成HDD旧捕获，随后仅停止/启动独占普通容器。真实1440明色/320暗色网页分别恢复原始内容和替换内容，每份2097159字节；确认为每次一个Job，确认取消零登记，所有恢复无浏览器PUT/文件字节。原捕获来自重启前实例，恢复使用新的当前实例。独立检查永久SQL目录及canonical来源、job type、完整Root暂存、发布回执、实际文件、两份被覆盖inode与完整HDD内容链，原捕获元数据和HDD数据指纹始终不变。

另一页面正常退出后，两页回登录，待确认恢复及历史窗口消失，WD401/Authentik拒绝；没有额外恢复。主代理查看实际手机暗色历史窗口和桌面明色确认截图：无水平溢出、状态与按钮可见。证据 `deploy/artifacts/container-restore-browser-37e063c3cbd14ce3abcd51dfb8a55247/`，最终日志 `/tmp/wd-container-restore-browser-3bac49d.log`。

独立最终核对：34上传/恢复意图，19 Succeeded/8 Canceled/7 Failed，恢复2；活动/Unknown/租约/发布屏障0。三类临时身份均0，测试源缺失且精确容器明确no-such-object，隔离dispatch库0，11核心healthy。永久Job/审计/Root/HDD保留。

3bac49d CI后端/原生PG/原生Valkey/安装器已success，前端完整浏览器组仍运行，不能称全CI通过。本次仅签同一当前容器、原路径的内容版本恢复常规链；完整历史权限/所有者/时间属性、跨路径和容器重建身份迁移、历史清理/长期保留、普通真人身份及更广撤权/Worker与Agent中断/发布故障仍继续，P2文件管理及总59项保持active。下方候选/未部署是历史记录。


2026-10-27。本批尚未部署，当前安装态仍为 e6b5034/minor42；不得把候选测试当成真实恢复成功。

新增容器独立永久历史目录和 EF migration。实时 Root 分页观察只登记可信元数据，不将未返回的捕获当作缺失或删除；Present 仅为外形可读。计划固定当前管理员登录、目标实例/路径/父与叶版本、完整来源元数据摘要。独立恢复计划签名不能与上传计划互换。

恢复仍使用永久上传发布状态机、Job/Outbox、租约、别名发布屏障、HDD覆盖前捕获、单次发布和已有 Unknown 管理员接管；job type 为 containers.files.restore，取消与通用重试分流已接，禁止普通重试重复发送/发布。canonical 来源 JSON 以 text 精确保留并纳入不可变 MAC；普通上传的 null 新字段不序列化，旧签名和冻结恢复快照格式保持。HTTP 原始上传入口拒绝恢复任务。

Worker 只从已批准 HDD 来源读取。SDK 要求末帧与正常 EOF 才能正常结束，随后 Stage 才可提交；最后一块之后验证失败仍不能 Ready。开始接收的 SQL 标记先提交；Worker 重启后完整 Staged 回执只观察推进，不重新读取来源，缺少/中断回执则永久拒收封住原操作。当前登录、权限、取消和租约在传输中继续检查。

页面支持当前容器整体/单文件历史、严格分页、来源状态、二次确认、任务查询/取消、登记回执丢失时只重用原计划与原幂等键、Unknown停止自动操作；账号/权限/实例/目录改变同步清空私有视图和确认框。浏览器不传输恢复文件字节。

本批是同一容器原路径的内容版本恢复：覆盖时沿用当前文件的写入属性策略；尚不声称完整历史权限/所有者恢复、跨路径/跨重建容器迁移、目录恢复或整个文件管理完成。这些缺口保持在总目标内，不能以本子功能替代。

本地门禁：Release零警告错误；2682 Unit通过，198 Integration通过，7原生夹具默认跳过。494前端单元、lint/格式/类型/生产构建通过。组合生产预览35场景首轮34通过，取消合成夹具未递增版本被防倒退正确拒绝；修夹具后新历史7场景全通过，其他28项未改的读取/上传/编辑场景此前通过。未削弱生产检查。

构建首轮自动migration触发namespace/数组分析器已按仓库风格修正；Vue多语句内联事件在格式化后不可解析，已改明确方法并全前端重跑。失败日志保留。本机Corepack旧入口不适配pnpm12包形态，使用已安装并锁定的Linux原生pnpm12.10.1，无新增全局包或技能。

新增真实数据库门禁在 NativeNotificationDispatchTests.ContainerRestores：实际迁移/目录/精确JSON-MAC/Job与事务、合法租约接收/Ready/发布屏障与取消；其中Root回执明确为合成，不冒充真实Root。尚待冻结Git源执行。

首轮冻结0632200实际PG在Outbox检查处失败，SQLSTATE42883：新测试把CLR字符串Contains用于jsonb列，产生不支持的SQL函数。此前同轮迁移/目录/来源MAC/租约/Ready/发布步骤已执行，但整轮不计通过；已改为参数化jsonb jobId字段查询并等待完整重跑。隔离容器与临时库清理已确认，生产未部署，失败状态及日志保留。

真实工作站门禁 validate_container_restore_browser.py/.mjs 已准备：BFF先生成2MiB+7旧版本，实际仅重启独占容器，以1440明色/320暗色网页分别恢复两个历史内容版本，确认取消零Job/零浏览器内容、正常跨页双端退出；独立完整字节/SQL目录/不可变来源/Root/HDD与旧inode链核对，永久证据保留、仅精确清理自有测试数据。目前未执行，不能签收。Worker/Agent实际中断与发布故障、普通真实身份/更广恢复边界和其余59项继续。
