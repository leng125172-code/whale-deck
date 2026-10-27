# 容器上传 HTTP、专属 Worker 与精确代理候选

日期：2026-10-27。单工作站59项中的容器文件子范围。候选已接通业务入口，尚未部署；当前安装态仍为f5a0029/minor40。本文不以本地HTTP、SDK或合成页面替代真实Authentik、安装态Agent、SQL/Worker、HDD及实际网页整链。

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
