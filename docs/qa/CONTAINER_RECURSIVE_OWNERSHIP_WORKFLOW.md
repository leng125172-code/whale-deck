# 递归所有者：内部业务编排候选

> 2026-11-02后续：a19f5c4已接具体EF仓储/迁移、九方共享保护及专用Worker候选，4036 Unit/309 Integration（7默认原生跳过）和双隔离真实PG全套1执行/1通过/0跳过及独立清理通过。未部署、无管理员API/Vue或真实身份文件页面；下方未接仓储等保留为7a94733原批次事实。[持久化候选及边界](CONTAINER_RECURSIVE_OWNERSHIP_PERSISTENCE.md)。

2026-11-02，冻结产品及测试源码 `7a94733`。这是独立Domain/Application编排和签名候选，**不是已上线的永久业务功能**。本批没有具体EF仓储、migration、共享写入保护实现、专用Worker注册、管理员HTTP/Vue或真实身份页面验收；测试内存Repository与合成身份/时钟/Agent只验证内部状态机，不替代真实PostgreSQL、Authentik、目录树或HDD证据。

前批宿主 `22906f5` / Agent协议1.50的[安装态签名RPC/HDD验收](CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)保留，API/Worker/Gateway沿用前批 `0dede08`。本批没有部署、重启、修改生产数据或重做已完成的实机操作，也没有自动回滚dev。59主项仍未完整签收，17项移出不计完成，master尚未交付。

## 本批实现

- 新增`ContainerRecursiveOwnershipIntent`及独立仓储契约，预检和执行各自Job ID；原Actor/原LoginBinding/幂等键派生独立请求ID、执行ID、Root owner。尚未加入`PlatformDbContext`或注册具体业务仓储/Dispatcher；只注册已实现的签名器，不把测试仓储注册为产品服务。
- 请求、密封整树计划、执行与确认令牌分别使用`whaledeck-container-recursive-ownership-business-{request,plan,execution,confirmation}-v1` HMAC域，与单对象所有者、递归chmod及底层RPC令牌分离。完整请求/计划/原登录/期限绑定，拒绝字段篡改、非规范JSON/时间、重复/额外字段和跨域确认。
- 原根属主取`Plan.RootIdentity.OwnerUid/Gid`，目标取`Plan.Command.Uid/Gid`，执行身份取`Plan.OwnerUid/Gid`，三者分列。测试分别使用1000:1001、2200:3200、5100:5101，并单独覆盖显式0:0。Command的UID/GID增加`JsonRequired`，缺失不能默认为root；未来HTTP仍须实现严格请求解析、重复字段拒绝及接口验收。
- 预检登记不调用扫描或修改，独立确认原计划后才可进入执行。请求规划窗口15分钟，Ready计划5分钟，确认后的执行窗口15分钟；原幂等重试/过期查询不续期、不重新预检或创建新执行。
- 当前权威身份与原BFF登录共同授权。读取、幂等重试及写入返回前复核；确认与取消在写入前复核。仓储读写期间合成降权不能返回私有结果，已登记任务在后续调度中也不能继续Apply。执行中每6秒检查授权、取消、租约，总RPC预算335秒；真实90秒持久租约仍需具体仓储落实。
- Dispatcher先Inspect原执行ID，取得共享发布屏障/Started标记后才能Apply。回执丢失、Started后、过期或撤权后只Inspect/Discard原ID，不自动续做、不回滚。当前绑定改变的完整完成标为Superseded；Partial/Unknown保留五类计数、历史事实与保护。
- 当前另一管理员处置须明确确认与版本匹配，以原Actor/owner/执行ID取得永久Discard fence，再复核当前身份后申请仓储释放保护。已存Root状态、五计数、outcomeDigest、已验证历史摘要与historyMayExist不可被矛盾回执覆盖；BindingCurrent/HistoryAvailable/Replayed只是当前观察，不冒充永久历史证明。

## 同步修复递归chmod可信回执归档

旧递归权限Dispatcher对所有非Completed回执都再次查询操作者身份来判断取消。若Apply后身份/会话依赖失联，已核对Partial/Unknown可能被延迟归档，之后越过执行期限进入无回执Unknown，丢失已知计数；已知Aborted也可能被错误归类。

两种递归Dispatcher现在先核对Root回执兼容性。Partial/Unknown按可信状态直接保存；Aborted优先读取明确取消事实，身份查询失联时保留已知Aborted/Failed，不伪造已取消或无回执Unknown。执行前/进行中的授权与租约检查保持；此归档只记录事实，不再次Apply、不释放Partial/Unknown保护。

两套工作流各有6个回归组合：Partial/Unknown/Aborted × 执行期限前/后。合成Apply返回后触发身份失联，首次等待恢复、再次Inspect取得原可信回执，断言原计数/历史、Apply仅一次、无多余Discard和正确保护状态。**这是候选测试，当前0dede08 Worker尚未部署此修复，不算新增真实故障签收。**

## 未验收与下一步

1. 实现具体EF仓储/迁移、原子幂等/双Job/Outbox/审计、90秒租约/CAS，以及请求/计划/执行/Root计数历史不可改约束；实体类和接口不算持久化。
2. 新增递归所有者后，要与上传/目录/复制/移动/普通权限/单对象所有者/递归chmod及主机路径锁构成九方双向数据库、路径及预算保护。所有旧写入方也须检查新屏障，不能只有新表自检。用双隔离真实PostgreSQL验证并发、租约、版本、不可变触发器和准确清理。
3. 再接专用Worker、严格管理员API、Vue明确目标/预检/确认/取消/原引用恢复、五计数/历史/当前观察和其他管理员处置；正常冻结覆盖后以真实Authentik→Vue→API/Worker/SQL→Agent→实际树/HDD验收，补权限/取消/退出/撤权/重启/回执丢失等故障组合。
4. ACL、删除、长期清理及其他59项继续；历史普通属主元数据不等于完整ACL/特殊位恢复，同设备HDD记录不等于异盘灾备。保留核心保护、内部服务隔离、稳定维护入口及用户自行撤销开发sudo的既有约束。

## 尝试与证据边界

首次文件生成的自校验因测试仍残留chmod字段中止；已生成文件保留，修正剩余测试适配后继续，没有运行生产操作。一次先于测试文件完成的过滤测试返回“No test matches the given testcase filter”，不计作通过。之后依次106、113、197项相关测试通过；补读写返回前授权后递归所有者Workflow87项通过，最终冻结源码全量结果另列下方，不将各轮数量相加。

本批仅后端C#源码/测试与文档变化，无新增前端测试或真实PNG。此前22906f5的CI运行`41440833378`本批只读复核为backend/frontend/native-postgres/native-valkey/installer五路success；该旧CI不代表本批7a94733已通过远端CI，也不等于新业务已部署。

## 冻结源码验证结果

源码`7a94733`，产物位于本机忽略目录`deploy/artifacts/recursive-ownership-workflow-7a94733/`，不是新增实机证据。

| 检查 | 实际结果 | 边界 |
| --- | --- | --- |
| `dotnet build WhaleDeck.slnx --configuration Release --no-restore --nologo` | 成功，0 Warning / 0 Error | 构建候选，不发布 |
| Unit，Release `--no-build --no-restore` | 4034通过 / 0失败 / 0跳过 | 含递归所有者124项、旧递归权限新增6项；依赖为测试夹具 |
| Integration，Release `--no-build --no-restore` | 308通过 / 0失败 / 7跳过 | 默认原生PG/Valkey测试未执行，不代表本功能真实PG通过 |
| `python3 -m unittest deploy/validation/test_scope_baseline.py -v` | 8通过 | 只验证59项范围和记账 |

日志：`release-build.log`、`unit.log`、`integration.log`；已有真实RPC/HDD验收的原件与失败记录沿前批保留，不重复生成。

附加文档检查：211份Markdown、1279处相对链接无缺失，`git diff --check`通过；安装态RPC与旧递归权限Workflow两份QA原始正文逐字保留，只前置后续说明。此检查不验证产品功能。
