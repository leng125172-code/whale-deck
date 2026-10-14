# 通知发送 Worker 与受控传输

2026-10-14，运行时6ad5970已覆盖发布并分项签收。前置运行时e5b2cfc的元数据API/UI已有证据。本增量不是P4-06、P0–P5或76项完成。

## 本轮实现

- 独立1秒Sender Worker，批次100路由/10领取，32渠道/512路由预算。所有HTTP/SMTP传输在数据库事务及数据库执行重试之外；Sending持久后只调用一次传输。20秒传输超时小于90秒租约，10秒独立完成预算；中断/超时/异常未知不自动重发。
- 逐次尝试1–5与开始/结束/固定结果码，同事务持久化；Sender/租约恢复、事件保留级联。管理员GET `/api/v1/notifications/manage/deliveries/{id}/attempts`返回最多5条，无租约/私有配置/响应正文；前端真实抽屉查询与降权清理。
- 配置绑定的SHA256 revision只在内部保存；目标或凭据变更，旧待发送意图取消，不改用新目标。已进入Sending使用捕获的旧不可变配置，不能撤回/盲目重发。渠道/路由现有version仍再次校验。
- 每渠道及真实目标共同限流，SQL事务锁跨Worker有效。相同URL/SMTP服务的渠道别名、凭据更改不扩大发送预算；5–300秒间隔，限流延期不消耗发送次数。目标门控过期1小时后每轮最多清100条。
- Claim token在执行重试间固定；发送开始与结束提交不明时只读本次回执确认，不在数据库重试中调用网络。配置失败在开始前终结，无原始异常持久化。后台错误仅固定Warning且每分钟一次。
- Webhook正文只含固定事件码/级别/来源GUID/时间/投递GUID；可选Bearer与HMAC，DingTalk毫秒签名、Feishu秒签名、WeCom密钥URL分别处理。机器人按响应code/errcode判断接受，非简单HTTP200。5xx/畸形响应为Unknown；一般429不猜测“未处理”。只有确切未连接/SMTP临时拒绝才自动退避，最多5次。
- SMTP使用MailKit4.18.1，必须STARTTLS或直接TLS，不忽略生产证书；只发送批准邮箱，稳定Message-ID。SMTP接受不是收件人已送达。模板不含主体、原始请求/结果JSON、目标、凭据。

## 私有配置边界

Worker固定读取 `/run/secrets/notifications/registry.json`，仅Worker挂载宿主 `/etc/whaledeck/notifications` 的只读父目录（看到原子替换）；API不挂载它。目录root:whaledeck/0750；文件0600且属实际Worker UID（默认1654）。不自动创建含凭据文件，不将目标或密码放到Git/示例/日志。示例应使用保留的example.test/example.invalid或空注册表，不填写真实邮箱或群地址。

JSON version=1、targets/credentials各最多32、整体64KiB、深度8；拒绝重复键、未知字段、符号链接、宽松权限和控制字符。引用64字符固定格式。轮换必须原子替换文件；WebUI私有值编辑/专用Agent写入和审计仍需实现，不把手工文件配置当作最终产品已完成。

公网HTTPS使用证书校验并在socket建立时解析/检查/固定地址，可指定批准IP子集。私网必须显式允许且列出精确IP；HTTP只准显式批准的私网。禁代理、重定向、Cookie、HTTP2/3跨端点复用；禁止loopback、metadata/link-local、multicast、unspecified、IPv6转换绕过。测试的loopback及自签证书pin只通过测试构造器注入，生产DI不提供，无运行时配置开关。

Worker单独无端口的受保护egress网络；数据库/cache/API网络仍内部隔离，Agent UDS及核心保护不变。双IP/USB维护入口不改。user全量免密保留；dev不自动回滚。

## 验证状态与剩余范围

本机Release0警告/错误、538 Unit、73 Integration及119前端通过（6原生本机明确跳过）；36项实际Chrome原布局/搜索/消息/限流/新配置抽屉回归通过，抽屉320/1440明暗4项先独立复测。冻结源码6ad59707b99c926a0b53f20163b2a1b8d9a5a88f的原生dispatch报告7c41589d8f2f4d54b661fecc4b5a3356实际执行1/通过1、cleanupConfirmed=true，2026-10-14 06:50:33–06:52:42 UTC。包含原有派送/管理兼容、真实PostgreSQL路由、签名HTTP接收、共享目标限流、轮换取消、SMTP/TLS本地接收与尝试查询，独立库/SDK/私有文件清理；不是外部厂商群/邮箱验收。发布前核实生产启用渠道数为0，不自动配置收件人。

第一次发布在重复Git拉取阶段TLS失败，没有创建更新任务，实际unit inactive且runtime仍e5b2cfc；随后核实冻结源码与无活跃更新后重试。第二次任务8181fbb9-de93-467d-89cc-ce9d3a089953成功移交，另外核实宿主unit最终inactive/dead、Result=success、ExecMainStatus=0，部署记录精确6ad59707b99c926a0b53f20163b2a1b8d9a5a88f。SenderRuntime及SharedTargetGate两项migration实际落库，Worker只读父目录挂载、root:whaledeck/0750、受保护独立egress及内部PG网络保持；11核心healthy，Agent/维护/timer active，sudo保留。生产启用渠道0，不创建真实外部目的地。

真实OIDC管理员/普通/匿名元数据CRUD、幂等/版本/CSRF/Origin/权限/OpenAPI和清理回归再次通过。8个HTTP入口/共享/页面分块与当前Gateway逐字节一致，页面含新提示与attempts查询；index-BaFi9er_.js SHA256=5459f2fdd160e404ba5fa6f11318e4a7b479bda44ac4dc1dcbf54473b15c6f66，NotificationSettingsView-iYM45rmo.js SHA256=efd98bf69ce46ce5b31b052b5d9071272eb46e66c480baef07c815bdb8ead520。现场生产身份浏览器未另行走查，attempts的真实行读取在原生仓储中已验证，不冒称生产已生成发送记录。首轮只读migration检查用了错误SQL引号/列名，随后正确TABLE读取确认两项migration，未制造新发布或改动数据。

仍缺：WebUI真实目标/凭据配置与安全Agent写入/原子变更流程、测试发送入口及管理员确认Unknown手动重试、模板/偏好管理、更多故障/延迟/超额组合，以及批准目标的真实Email/Bot厂商验收。不能因适配器或本地接收端通过而标整项渠道完成。当前“已写入”与“已原生/部署验收”必须分开。

## 依据与外援

- [.NET socket连接回调](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0)、[MailKit4.18.1](https://packages.nuget.org/packages/MailKit/4.18.1)、[SMTP客户端socket+TLS](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_ConnectAsync_1.htm)。
- [SMTP规范RFC5321](https://www.rfc-editor.org/rfc/rfc5321.html)区分临时/永久拒绝及接受，不能把传输超时推断成未送出；[飞书自定义Bot文档](https://open.feishu.cn/document/client-docs/bot-v3/add-custom-bot)签名算法与DingTalk不同。
- GLM glm-5.3-flash机械测试草案，52123ms、输入11025/缓存256/输出720、成功1次、费用未知。未采纳其“Secret版本改变不影响旧语义”的泛化建议；按已确认安全契约取消旧预发送意图。安全、设计、实现与复核由GPT完成。
