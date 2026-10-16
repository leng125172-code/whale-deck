# 宿主终端 BFF 会话层

2026-10-16。完整76项仍在开发。本增量在已签收的宿主PTY之上新增真实后端，不代表已有前端或容器终端。

## 实现

- `POST /api/v1/terminals/host/grants` 要求当前管理员、现有Cookie写请求CSRF/Origin、明确确认和相同body/header UUID幂等键。30秒连接票据绑定主体/当前登录，未消费且未过期时重试返回同一票据，不增加会话；只保存票据哈希，签名用途与Agent capability分开。
- PostgreSQL `terminal_sessions` 与EF migration保存元数据、版本和期限；全局4/每主体2个待用或活动会话，SQL事务锁保证跨API副本预算。消费票据行锁一次性转Connecting，与审计同事务；续租/关闭必须同时匹配主体及登录绑定。10分钟硬期限、20秒连接租约、35秒活动租约、终态不可覆盖。状态查询不返回票据哈希或登录引用。
- `GET /api/v1/terminals/{id}/connect` 必须带Cookie、精确同源Origin、固定子协议且URL无query。票据只在第一条最多256字节消息中发送，5秒期限；输入帧最多8KiB/解码4KiB，拒绝重复/未知字段与目标/命令覆盖。输出慢消费者5秒限制，Agent未ready10秒失败，不新增任意宿主路径/UID参数。
- 每10秒直接重读服务器票据，再复核当前管理员与会话租约；不依赖长请求中被缓存的AuthenticateAsync。注销移除票据/禁用/撤权按既有身份复核策略失败关闭，显式关闭立即落库并在下一次租约检查断开。Cookie引用处理参照[ASP.NET Core 10官方实现](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Security/Authentication/Cookies/src/CookieAuthenticationHandler.cs)。Token/Cookie引用只在内存，不放URL/审计/SQL；日志只记录会话UUID和异常类型，不记录异常对象或输入输出。
- Gateway补充API路径WebSocket Upgrade；维护入口继续YARP代理。实际shell依然是受限非root宿主身份，无法继承开发用户sudo或Agent Docker组；此入口不是管理员root控制台，不以其替代未来批准的高权功能或容器Exec。

## 当前证据边界

本地641 Unit、89 Integration通过，6原生测试在Windows明确跳过；Release全方案0警告/0错误。新增输入协议、授权/确认/票据幂等、无敏感字段状态和Cookie删除后直接重读等测试。EF生成迁移已修正为仓库文件范围namespace和静态索引列配置，未放宽分析器。

新增隔离原生PG验证覆盖双连接并发抢票、错误主体/登录/哈希、不可重放、续租/过期/终态、全局/主体预算、审计失败回滚，以及等行锁期间到期拒绝。新增真实OIDC/代理/BFF/UDS/PTy验收脚本，但本文件写入时尚未执行新增原生或发布，不得视为已通过。

尚缺：前端xterm界面与浏览器走查、容器Exec、文件传输、长期会话元数据清理、进程重启/更多故障组合及完整P1-11签收。

## 首轮实际结果与修正

- 冻结 `61c97c187e2a6ed33a8637555cf21e1bdbdb4f54` 的原生报告 `dispatch-354b48dfb3744f7c91022b556203c5af.json`：2026-10-16 03:57:15–03:59:18 UTC实际执行1/通过1、cleanupConfirmed=true。SDK ID `sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6`。包含新增真实PG抢票/预算/审计事务与行锁到期测试。
- 更新Job `ecf433a5-afdb-401f-a63f-30c788972b6c` 提交，实际更新unit于12:02:17 CST成功结束，inactive/dead、Result=success、ExecMainStatus=0。真实BFF验证首次失败RuntimeError：两条测试会话最终均Closed，正常shell的exit 7已持久化，另一个主动关闭会话未收到协议关闭帧。测试会话已关闭，审计保留，无残留终端unit。
- 根因是取消待接收WebSocket操作先中止了socket，关闭帧随后无法发送。修正为与输出互斥地先发CloseOutput，再取消接收/清理；不放宽验证脚本的EOF断言。新增传输回归模拟待接收取消即abort，验证正常close帧先于取消、exit 7最终持久化。异常只记UUID/异常类型，无正文/异常对象。
- 同时区分Connecting与实际Ready：签发Agent capability前仅验证Connecting；Agent返回Ready后，Active/ConnectedAt与terminal.opened审计同事务，仅一次写入；定时续租不重复审计。修正后642 Unit、90 Integration及Release通过，6原生在Windows跳过；新原生/覆盖发布与最终链路验收尚待继续。
