# 容器上传 HTTP、专属 Worker 与精确代理候选

日期：2026-10-27。单工作站59项中的容器文件子范围。候选已接通业务入口；随后宿主Agent与MaintenanceHost已安装b681e1d/minor41，API/Worker/Gateway仍为f5a0029，新的业务入口尚未部署。本文不以本地HTTP、SDK或合成页面替代真实Authentik、安装态Agent、SQL/Worker、HDD及实际网页整链。

## 实现

- 独立管理员Controller提供计划、确认登记、原意图状态、单次原始接收与持久取消。路由只接受64位小写完整CID及36位小写D格式UUID，不允许名称、缩短ID、其他UUID格式或尾随换行。Route中的MVC方括号按属性路由规则转义。
- 写入口沿用全局CSRF、Origin、HttpOnly服务端Cookie及业务中的当前管理员/原登录校验。确认登记、接收及取消要求唯一且匹配的Idempotency-Key；状态与响应禁止缓存，不返回owner、MAC或原Cookie。
- 接收仅接受application/octet-stream、明确Content-Length、最多1GiB；拒绝Content-Encoding、Content-Range、Range。前台仅完整暂存，返回202及原任务Location，不直接Publish；接收期间与结束后复核原登录，退出不能Ready。
- 注册真实SQL仓储、上传服务与调度器。独立HostedService每秒取任务，发布与指标采集/通用Outbox分开，故障警告最多每分钟一次且不输出异常正文或文件内容。既有共享发布保护、原子任务/审计、租约和取消约束保持。
- 明确实例停止、实例/根/目标变化的固定Agent失败进入Unknown，保留发布保护，允许显式管理员历史接管。未过期的短暂通信失败仍等待；过期且无法取得正面结果的任务进入Unknown，不无限重试或伪称取消/上传成功。
- MaintenanceHost仅对精确PUT内容地址使用1GiB与660秒活动预算，保留OriginalHost。nginx只对同一精确地址放宽到1GiB并关闭请求和响应落盘缓冲；计划/状态/取消/其他API保持原额度。尾随换行不匹配宽额度路由。
- OpenAPI使用共同的原始流Transformer，为宿主与容器的Content PUT生成必需的二进制requestBody；其他操作保持原schema。
- 前端修复JavaScript正则末尾可接受换行的边界：完整CID长度和原始启动实例全匹配，不归一化纳秒。

## 已验证证据

- Release构建零警告/零错误；完整2604 Unit通过、191 Integration通过，7项专用原生夹具在默认运行中明确跳过。EF model检查无待迁移差异；范围元数据8项通过。
- 新增17项HTTP/文档/边界测试：实际loopback Kestrel、MVC、加密opaque Cookie与服务端TicketStore、CSRF及来源中间件。计划/登记/接收仅Stage、202/Location/no-store、跨CID拒绝、匿名401/普通403、CSRF/Origin前置拒绝、过时管理员声明降权、Stage结束前撤销原Cookie不能Ready、媒体/压缩/Range/幂等键拒绝、非规范ID404、请求长度边界与实际生成OpenAPI均通过。目录、SQL和Agent为明确夹具。
- 新增11项.NET专项覆盖固定失败映射/保留保护与过期通信结果，以及实际YARP loopback：完整2MiB+7全SHA、上游6秒无数据后仍通过、保留Host；其他路径/动词/非规范标识不得使用上传路由。
- `validate_container_upload_proxy.py`使用当前Gateway不可变镜像ID，在无网络/无宿主端口/无生产卷/无Docker socket挂载的独占只读容器中验证原生nginx配置语法、精确PUT、GET/POST拒绝、1GiB上限、9类负例及其他API的1MiB默认额度，结束后精确清理。只注入QA标记与容器内dummy upstream；没有改运行中的Gateway。
- 前端456单元、Oxlint/ESLint、Prettier、类型及生产构建通过。同一生产构建的52项上传/恢复浏览器测试全部通过（5分钟），覆盖320/1045/1440明暗、一次发送、丢响应只查询、取消/Unknown和旧恢复兼容；合成API不算真实SSO/UI签收。
- 实机11核心容器逐项healthy；本批尚未安装minor41或更新API/Worker/Gateway。上一冻结34ffb2e原生PostgreSQL和两类丢封存请求/回执的证据沿用原记录，不冒称本批运行了实际依赖整链。

## 失败与修正

新Controller首轮实际MVC启动拒绝正则方括号，被识别为属性路由占位符；按框架转义后全部HTTP测试通过。Swagger测试引入OpenAPI源码生成器，需要在测试项目明确启用其生成命名空间，已按编译器要求配置。一个测试错误地向框架ContentLength属性赋负数，setter本身即拒绝；修正为可到达Controller的缺失及超过1GiB边界，没有放宽产品限制。前端新测试补齐明确错误消息后lint通过。

nginx首轮直接绑定0600源码给容器服务用户，校验被文件权限拒绝；改为独占临时可读副本，未修改源码和生产文件权限。临时副本首次0444也阻止QA写入标记，修正仅该临时副本的owner写权限后完整门禁通过。早前MSBuild子节点异常退出，使用每次命令独立的单节点、不复用构建服务器后构建通过，未更改全局工具链配置。

## 后续验收

冻结源码、安装minor41并验证新历史观察/封存签名RPC，覆盖发布后完成真实Cookie→SQL/专属Worker→UDS→Root/HDD的完整覆盖与新建、取消/撤权、停止/重启和Unknown接管，以及桌面/手机页面验收。编辑、历史恢复和其余59项继续，17项明确移出不计完成。当前不签完整文件管理或整个项目。

## b681e1d 宿主安装态验证

冻结b681e1d6abe26999bcdb47a86f62a190c2b0abdc构建自包含宿主包并覆盖安装。两宿主实际build-info一致，运行库与PKCS均10.0.13；systemd active/零自动重启、UDS/只读密钥/受限helper/固定HDD策略及当前动态网卡入口校验通过。真实UDS GetCapabilities独立解码确认major1/minor41及三项上传能力。没有重建、更新API/Worker/Gateway或基础数据库容器。

同源码冻结TerminalProbe真实签名五方法完整通过：完整2,097,159字节发布/原只读RPC全字节读回、原inode/HDD原件、主体/方法/请求体/MAC/重放拒绝、显式暂存取消、首字节前Prevented及迟到首帧拒收。独立观察器核验全部字节、文件元数据、HDD捕获与根回执后精确清理这次自有资源。

新增独立Python protobuf与MAC客户端71e2005验证另外两个安装态RPC，不调用产品C#签名工具制造预期：真实自有容器取得目标证明，ObserveRecovery=Missing；实际停止、重新启动后原实例仍观察同一完整摘要，而普通Inspect拒绝旧实例。错误批准摘要精确Aborted/CONTAINER_UPLOAD_RECOVERY_OBSERVATION_CHANGED，正确Seal永久保存schema3；同一固定请求重放与再次Observe完全一致，修改范围精确PermissionDenied/CONTAINER_UPLOAD_RECOVERY_SCOPE_CHANGED。匿名及改请求体的旧MAC被拒绝。最后独立复算原记录、主体/Trace/请求和观察摘要，检查根所有/0600/单链接、原操作锁及零payload，仅清理这次已证明的Missing封存；不能将此清理用于任何有数据、Published或Unknown的生产封存。自有容器/原件与测试封存均精确清理，11核心healthy。

前两次恢复RPC验证失败为验证脚本错误预期：将已有契约中ObservationChanged的Aborted和ScopeChanged的PermissionDenied猜为FailedPrecondition。第二次已保存固定状态诊断，按源码/契约改为精确状态及错误码后完整第三次通过；没有改产品映射或放松权限。失败时未发生封存或写入文件，留下的两个空锁保留，未凭时间猜测删除；私有状态目录最终只有五个零字节lock，其中三个为运行前已有。后续客户端已在执行前保留私有范围/源码SHA基线和验证记录。

以上是受信root QA调用方证据，当前管理员的人类身份/SQL恢复审批、真实Worker和网页仍待，不签完整容器文件模块。CI b681e1d的backend/native-postgres/native-valkey/installer已success，frontend最后观察仍in_progress，不宣称整条CI成功。
