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

第二轮 `2e9e512dde96078d9ea546dcf8db992ef755e1a6` 原生报告 `dispatch-5a6037b248b6428a8c1d8d2fc9e81de6.json` 于04:13:50–04:15:47 UTC执行1/通过1并清理，含Opened一次审计。更新Job `ca0f66f3-98c0-456d-9eeb-5e174dbd2508` 对应实际unit12:18:06 CST成功结束，但真实链路仍在 `explicit-close-protocol-frame` 失败 `TERMINAL_TRANSPORT_EOF`，不能签收BFF。测试会话再次精确关闭。

随后补全有界Close握手：CloseOutput后最多等待2秒，让接收中的客户端Close确认自然完成，超时才强制取消；仅提前写CloseOutput仍会因立即Abort导致代理来不及转发。验证客户端收到Close必须回送RFC6455确认，依然不接受裸EOF。新增真实TCP回环上的.NET ManagedWebSocket双端测试，要求完整Close而非Aborted，以及遵从/不遵从确认的两个有界传输测试；当前3项专项通过。它证明本地传输，不替代真实代理链路重测。

前端尚未生成。本机pnpm 12.10.1原生客户端在官方源、系统代理及镜像源均遇TLS handshake eof；没有关闭TLS验证或更改全局代理，失败安装没有改动package/lock。官方元数据通过Windows HTTP读取确认候选依赖@xterm/xterm 6.0.0、@xterm/addon-fit 0.11.0，后续仍须正常锁定/干净安装及UI验收，不能视为已安装。

## 最终后端签收：5bc2551

- 源码 `5bc25519639b0d53896b642f395754522185e03d`，本地642 Unit、92 Integration通过，6原生在Windows明确跳过；Release全方案0警告/0错误。包含真实TCP回环ManagedWebSocket关闭握手、确认/不确认的有界清理，不以抽象Socket替身单独证明.NET行为。
- 冻结原生报告 `dispatch-cd51c0b4c6f141f493aaf88ac1e9609b.json`：2026-10-16 04:27:39–04:29:43 UTC，实际执行1/通过1、cleanupConfirmed=true；SDK ID与前两轮一致。迁移/并发抢票/预算/事务审计/行锁到期和原通知恢复实际通过。
- 更新Job `5ddc975c-b32b-4faa-b79f-969413dcc70d`，实际更新unit于12:31:12 CST成功结束：inactive/dead、Result=success、ExecMainStatus=0、Deactivated successfully。无自动回滚。
- 真实OIDC Cookie → 维护代理 → Gateway → BFF → UDS → 宿主PTY通过：幂等票据、同源/query拒绝、Ready/120×40/非root身份/exit 7、持久关闭、已消费票据不重开、显式关闭后协议Close握手和租约撤销通过。没有放宽EOF断言，正文/票据/Cookie未输出。测试会话均关闭，元数据和不可变审计按设计保留，无终端transient unit残留。
- 普通用户/匿名对新终端读取、申请、连接和关闭入口分别真实403/401，管理员OpenAPI存在；原任务SSE 1006条/续传/终态/错误及清理通过。通知管理/私有值全链路与永久重放、8个HTTP资产和私有配置UDS拒绝回归通过；外部发送0。11核心容器healthy，Agent/维护/timer active，开发sudo保留。

签收边界仅为受限宿主终端后端。没有前端终端、容器Exec或文件传输，也没有真实身份浏览器走查、完整进程重启和宿主掉电演练。固定whaledeck-shell是管理员共享维护身份，不能据此宣称租户级内核/文件系统隔离或root控制台完成；长期会话清理与更广隔离仍是缺口。完整P1-11和全部76项保持进行中。
