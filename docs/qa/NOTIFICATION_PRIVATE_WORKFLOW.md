# 通知私有配置：管理员 API 与持久业务流程

2026-10-14。源码实现中；不得以本地测试或宿主基础验收冒称完整通知 WebUI/76 项完成。

新增管理员 Inspect、五分钟签名 Plan、确认 Submit（202 Job）、Status 与 Unknown 确认解除入口。使用已有主体绑定的短时 staging；受理后私有输入只保存到独立 Valkey TTL 命名空间，PG 不保存输入、地址、密码、staging 票据。HMAC 绑定 Actor/渠道/动作/输入，签名计划绑定版本与到期时间。永久操作记录、Job、Outbox、入队审计和渠道停用/版本失效在单个 PG 事务提交；精确已受理请求可在票据和计划到期后重放原 Job。

独立 Worker 在无指标采集成功依赖下运行，跨 Worker 90 秒租约，先按固定操作 ID 查询宿主回执、再作权限复核和写入。Root I/O 不进入 EF 执行重试事务。输入过期但已开始时不推断“未写入”，保留 Unknown；回执可在输入丢失或请求者降权后完成事实记账，不重新调用 Apply。根绑定被后续变化取代时标 Superseded。渠道配置、测试及启用在未对账期间阻断；失败/Unknown 后另有发送阻断标志，新成功配置对账前不能启用。确认 Unknown 只释放旧意图锁，不启用发送、不宣称回滚。

固定 root transient unit 加入 RuntimeMaxSec=15、TimeoutStopSec=5、KillMode=control-group；仍只有固定目录可写。新增 EF migration 保留已有记录；存在私有历史或发送阻断时拒绝降级。客户端 DTO、Job/Outbox/审计只有公共 ID、状态和不可逆 HMAC/请求指纹。

本地 Release 构建 0 警告/错误，561 Unit / 73 Integration 通过；6 原生本机跳过。新增用例验证 owner/确认/键/输入绑定、计划/票据过期后永久重放、独立 TTL、回执优先于权限/输入、丢响应对账不重复 Apply、签名/重复字段拒绝。隔离原生 PG 用例已加入迁移、原子暂停与 Job/Outbox/审计、版本/编辑和测试阻断、丢响应/输入丢失/降权后回执记账及永久重放；实际执行状态待报告。

冻结源码 6a32ea4 的原生套件实际执行1/通过1且清理；报告 `dispatch-be8a64c0ca3a41baa19f06d5ced2f7ba.json`，12:08:27.905424–12:10:53.351437 UTC，SDK ID `sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6`。新增私有业务的 PG 原子暂停/Job/Outbox/审计、丢响应/输入丢失/降权后回执记账和永久重放均真正通过；其中根回执使用有界测试适配器，不能冒称该段用了真实 Agent。

运行时 `39c990b965110d929f64f8d3cfdd17302ff7026e` 已覆盖发布并签收。Job fe89d524-6561-4c03-ab17-b1cd33e4a115 调度；最终宿主 journal 20:19:03 CST 成功结束，维护 healthy，11 核心 healthy、Agent/维护/更新 timer active、sudo 保留。首次远程 fetch Git TLS 失败时尚未启动测试或部署，核实终态后才重试，不重发已运行任务。

真实 Authentik→API→Valkey→Worker→Agent 正向链路已执行：随机停用渠道与 example.invalid 绑定，签名计划、202 Job、相同键重放同 Job、实际 root writer 应用及正常业务移除均成功；渠道始终停用，生产外部发送0。对应临时 Channel/Job/事件/Outbox 精确清理，不可变审计保留；私有 registry 中仅移除本次别名，不修改其他绑定，有限 root 回执保留供正常清理。普通/匿名的新私有读取/Plan/Submit/确认入口拒绝、既有元数据 CRUD/CSRF/Origin/OpenAPI回归、实际 UDS/宿主权限/动态入口及8个HTTP资产匹配均通过。新增 migration 20261014113018 已实际落库。本次前端源码未变，资源相同不代表新增配置 WebUI 已完成或生产身份浏览器已走查。

尚未签收：更多并发/取消/Unknown 人工确认与一致性故障组合、配置 WebUI、实际批准厂商目的地启用及整个 P4-06/76 项。下一步仍继续这些工作，不以此次后端正向验收缩减范围。

本轮 GLM Flash 只读定位尝试 1 次，54376ms，输入36149/缓存12672/输出1269，费用未知；受限定两次读取与输出截断，只得到部分定位，未采纳未证实方法名。GPT 接手检查、设计、实现和复核；无权限/敏感决策委派。
