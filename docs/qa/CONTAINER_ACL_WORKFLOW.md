# 容器 ACL：登录绑定与原任务恢复的内部业务候选

> 后续永久化候选（2026-11-03）：72d678e已接具体EF仓储/迁移、十方共享保护、租约/CAS及回执MAC原子保存，双隔离真实PG与独立清理通过；c9c9265通用任务入口加固，4272 Unit/364 Integration（7默认原生跳过）通过。未部署，管理员API/Vue及真人业务链仍待。下方41f02fb“无仓储”等原批次事实逐字保留。[最新候选和边界](CONTAINER_ACL_PERSISTENCE.md)。

2026-11-03。产品及测试源码提交 `41f02fbd6848744e7af9983b72c3266bd05fb649`。**本批只实现内部 Domain/Application 编排及基础设施签名器，没有具体 EF 仓储、迁移、共享写入保护、专用 Worker、管理员 API/Vue 或真人业务页面；不记完整 ACL 或主任务验收完成。** 内存仓储、身份、时钟与 Agent 都是明确的单元测试夹具，不作为生产依赖或默认实现注册。

前批 `4a58961` / Agent 1.51 的[真实签名 RPC、HDD 与兼容证据](CONTAINER_ACL_AGENT_RPC.md)保留；API/Worker/Gateway 沿前批 `b1fafed`。本批没有发布、重启、修改生产数据库或重复真实 ACL 写入，没有自动回滚 dev，也没有修改用户 NOPASSWD 或密码。运行版本沿前批记录，本批未重新签收健康、PID 或核心容器状态；不得把旧健康数据当本批实测。

## 本批实现与边界

- 新增独立 `ContainerAclIntent`、仓储契约、请求/计划/执行/确认/回执 DTO、`ContainerAclWorkflowService` 与 `ContainerAclDispatcher`。只注册已实现的 `IContainerAclProtector`，不注册不存在的业务仓储或伪造可用服务；现阶段没有可供人类调用的新 HTTP 入口。
- 请求 ID、执行 Job ID 与 Root owner 按原 Subject、原 LoginBinding、幂等键和 ACL 独立域派生；重复键仅接受逐项相同命令，不续期、不再次预检或创建新执行。15 分钟规划窗口、5 分钟 Ready 和确认后 15 分钟执行窗口的实际原子时限仍须由永久仓储落实。
- 请求、完整计划、执行、确认令牌与 Root 回执分别使用 `whaledeck-container-acl-business-{request,plan,execution,confirmation,receipt}-v1` HMAC 域。它们与 chmod、chown、递归所有者及机器 RPC capability 分离。确认拒绝额外/重复字段、非规范 JSON/时间、错误主体/登录和跨域令牌。
- ACL command、Entries、原 access/default ACL 以规范序列签名；所有命令相等检查使用结构比较。首次仓储/Agent 等待前深复制调用方列表，Agent 返回计划也独立复制，DTO 返回列表不能改动已保存的命令或原计划。named ID=0 与权限0有效；access 空条目拒绝，default 空条目明确表示目录清空；文件 default、缺 mask、模式/noChange 不匹配及映射未证明等拒绝。
- 原文件 UID/GID、容器执行 UID/GID、原两类 ACL、目标模式及 NoChange 分列；access 的 mask 控制目标组权限位，default 设置/清空保持原模式。确认警告说明不递归、保留另一类 ACL/原属主/目录子项，HDD 原 ACL 保留及 Unknown 不自动重做/恢复。服务端 DTO 同时提供历史原回执，避免把当前业务状态当 Root 新事实。
- 每次操作先查询实时权威身份，再验证原 BFF 登录，读写返回前复核；执行期间每6秒检查当前授权、取消及租约，单次 RPC 总预算65秒。依赖失联不能沿用正缓存去写入；90秒租约、版本 CAS、双 Job/Outbox/审计仍是仓储下一阶段的实现要求。
- Dispatcher 优先 Inspect 原执行 ID，取得共享屏障/Started 后才可 Apply；Started、撤权、取消、过期或丢失回复后只观察/Discard 原 ID，不再次 Apply。Completed 且当前绑定成立为 Succeeded，绑定改变为 Superseded；Unknown 保留保护、无成功版本与三态确认 null，不能当未发生。
- Root 回执单独密封，校验原操作/实例/目标计划/类型/模式/历史摘要；数据库 JSON 与标量 AclConfirmed、版本、历史事实必须一致，改 JSON 及其对应标量仍不能绕过回执 MAC。Completed 必须 true、Aborted 必须 false、Unknown 必须 null，未核历史不能报告成功。
- 当前管理员明确处置 Unknown，先对原 actor/owner/执行 ID 取得 Discard fence，再复核当前身份和版本申请释放保护；不恢复 ACL、不清历史、不宣称成功。此前 Root 事实不能被矛盾回执替换。父代理已核对 `whaledeck-container-acl.py` 的继承关系：`ContainerDirectories.TERMINAL` 包含 Unknown，ACL `recover` 在终态立即返回；已落盘 Unknown 不会再解析为 Aborted/Completed，因此候选拒绝该转换符合当前实际协议。
- 可信原 Unknown/Aborted 回执归档是只读事实记录。身份服务失联不丢弃它，也不授权新写入；失败回复/无回执仍保留 Unknown 保护，不把依赖故障伪造成用户取消。

## 自动验证与保留的失败

构建和全量测试在源码提交前、同一完整内容树执行；提交后逐个 SHA256 与 `git show 41f02fb:<path>` 核对11个源码/测试文件完全一致。构建时 Git 基线为 `dc87d1c`，不冒称程序集已嵌入41f02fb，更不冒称运行安装来源已改变。记录见忽略目录 `deploy/artifacts/acl-workflow-41f02fb/` 的 `evidence.json`、原始日志和 TRX。

| 检查 | 实际结果 | 证据范围 |
| --- | --- | --- |
| Release 全解决方案 `dotnet build WhaleDeck.slnx -c Release --no-restore --nologo` | 成功，0 Warning / 0 Error | 候选构建，不发布 |
| 最终内容树全量 Unit `--no-build --no-restore` | 4270通过 / 0失败 / 0跳过 | 内含 ACL 新增137个业务边界用例；合成依赖 |
| ACL 专项中间复跑 | 234通过 / 0失败 / 0跳过 | 后续仅DTO隐私输出/原回执展示及第四MAC域断言追加，由最终全量覆盖；两轮不相加 |
| Integration `--no-build --no-restore` | 363通过 / 0失败 / 7默认原生跳过 | 没有 ACL 原生PG验收，不冒称新 schema 已应用 |
| 范围基线 | 8通过 | 59保留、17移出及范围记账 |

首轮 ACL 过滤测试 **201项，200通过/1失败**：测试 Agent 未保存已返回 Unknown 回执，随后 Discard 返回 Aborted，被新事实兼容检查正确拒绝。修正显式测试夹具，使其像真实 Root 一样保留回执，并新增“矛盾 fence 拒绝且保护不释放”用例。原失败日志和 TRX 保留，不把它改成首轮全绿。另有纯读取命令因未显式设置子仓库工作目录、以及猜测历史文档/脚本名不存在而失败；随后按实际文件定位修正，没有任何生产动作或真实验收重做。

文档收尾的范围门禁另有一次7通过/1失败：新元数据误写“17移出不算完成”，缺少既定完整表述“17项移出不算完成”的“项”字。已修正文案，保留原失败日志，未放宽范围测试。

核心测试覆盖：原登录/实时组撤权与依赖失联、原幂等键/过期不续期、明确确认、可变列表与返回 DTO 别名、default 设置/清空/NoChange、mask 对模式影响、合法条目篡改、错计划/三态回执/错历史、原开始标记/租约丢失/取消、Apply 丢回复后不重写、历史回执独立MAC、Unknown 当前其他管理员处置及矛盾事实拒绝。它们不代替实际 Authentik、PostgreSQL、Worker 进程、ACL 磁盘或 UI 故障证明。

文档终检：223份Markdown、1355处相对链接无缺失；目标第1节正文保持不变，59主项状态没有晋升，安装态QA原始正文按字节保留，`git diff --check`通过。

## 下一阶段

1. 具体 EF 仓储与 migration：独立永久表、双 Job/Outbox/审计、请求/计划/执行/回执 MAC、原子幂等与版本 CAS、自然租约、原历史不可改约束。回执JSON/摘要/标量事实须原子保存，不能只补空接口。
2. 与上传/目录/复制/移动/普通权限/单对象所有者/递归权限/递归所有者及主机路径锁形成双向数据库、路径和预算保护；所有旧写入方也必须检查新 ACL 屏障。隔离真实 PostgreSQL 验证并发、租约、不可变与准确清理后，再注册专用 Worker。
3. 严格管理员 API 与 Vue 原 ACL/目标条目/mask影响、预检/确认/取消/原引用恢复及 Unknown 处置；输入须区分 ID/permissions 缺失与显式0，拒绝重复/额外字段。普通用户不因看板娘或AI功能获得文件管理权限。
4. 正常冻结覆盖后，以真实 Authentik→Vue→BFF/API→SQL/Worker→Agent→实际 inode/HDD 验证常规和故障；随后 ACL 递归/历史恢复、可恢复删除/长期清理和其余59项。NVMe/HDD异盘证明及完整灾备独立验收，保留稳定维护入口、USB/动态网卡和最小 Agent 授权。

仓库同步收尾：源码41f02fb和文档7543c31均已本地提交；两次正常HTTP/1.1推送均在TLS握手阶段以 `GnuTLS, handshake failed: The TLS connection was non-properly terminated.` / exit128失败，未取得远端更新回执，不能称已推送或新CI通过。项目图谱事件 `whale-deck.container-acl.41f02fb.business-workflow` 已取得成功回执并从pending移入synced；后续重试正常Git推送，继续本地永久业务实现，不以网络暂故障推翻已验证的源码/测试。
