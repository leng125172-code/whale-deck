# API多人额度与会话可用性

2026-10-14，P0-01/P0-08可靠性增量已在runtime4ec6e86签收；完整76项目标仍未完成。

## 原因与实施

原Program在UseAuthentication前调用UseRateLimiter，按sub分区时尚无认证主体，同一代理后的用户会退到IP共用额度。默认超额503也混淆依赖故障。现在由生产与测试共用扩展明确先认证再限流；MVC仍要求命名policy，原Origin、全局CSRF、当前身份/管理员授权不放宽。顺序与429/Retry-After用法核对[ASP.NET Core官方文档](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)。

- 已认证且有合法sub按主体；匿名/无有效sub按实际RemoteIpAddress，不读取任意客户端自报用户/代理Header。
- 普通操作与`/api/v1/auth`、`/signin-oidc`会话路径分别有每分钟120、无队列的预算。操作耗尽不阻断会话检查/CSRF/退出；会话预算仍有限。不会开放匿名管理、角色或测试身份接口。
- 超额返回429 Problem Details、API_RATE_LIMITED、traceId、no-store与整数Retry-After。不输出主体、请求体、Token或额外逐请求日志。
- 前端429中文等待提示，非法/过长重试时间不采信，不自动重新提交写操作；消息抽屉保留已有数据与已完成批次，不假称服务失联或已回滚。

## 本地证据与待验收

- Release全方案0警告/错误、476 Unit/73 Integration通过，6原生Fact明确本机跳过。新增9项是真正Authentication/RateLimiter/Origin/Authorization中间件链执行，不是仅反射配置；同IP两主体各120/121、匿名IP、未认证/非法sub、会话独立且仍有限、401/403均覆盖。模拟身份Handler只存在测试程序集，不注册生产。
- 113前端单元、lint/类型/生产构建通过。初轮测试Mock缺少类型、WriteAsJsonAsync重载使用错误已修复；以最终回归为准。
- 浏览器初轮因默认Playwright运行包缺失未启动，30项失败不是业务证据；随后使用已安装Chrome重跑，原布局22、原消息4、新429的320/1440明暗4项共30真正通过，截图保存在web/test-results。不是现场生产身份浏览器走查，不以启动失败或历史结果替代。
- schema没有改变，EF检查无待生成migration；以下发布验收已完成，旧ad0e661为本轮前置基线。
- 真实验收脚本`validate_api_rate_limits.py`使用临时Authentik管理员与普通Cookie主体、最多121次只读概览摘要请求；不对匿名生产流量灌压，不改变主机/容器/数据。验证第二主体仍可用、普通管理403、匿名401、额满时CSRF与面板退出、Cookie退出后401；不冒称真实浏览器完成Authentik全局SSO退出。测试窗口跨期失败必须如实处理，不能无限加请求凑429。

GLM glm-5.3-flash仅机械测试草案：72089ms、输入11046/缓存256/输出1465、1次成功0重试、费用未返回。配置、权限、实现与复核由GPT负责；窗口恢复/CSRF校验不因草案而冒称已全部原生验证。

## 工作站最终签收

- 冻结源码/runtime `4ec6e8617da8f30e059f407a29b6a046d9ab6c98`。更新任务610e3d46-87ca-4e31-8a72-4c9a75de760b移交成功后，另核实实际unit为inactive/dead、Result=success、ExecMainStatus=0及root部署记录精确该SHA，不以任务中心成功代替宿主构建终态。
- 真实Cookie管理员从同一维护/Gateway入口120次只读摘要成功，第121次429/固定code/traceId/no-store/Retry-After正确；同入口新普通Cookie用户摘要200、管理403，匿名单次401。耗尽用户仍可CSRF200、面板退出302/303且随后auth/me401，操作预算在会话检查前仍429，未跨窗口伪造。没有对匿名生产流量灌压、主机/容器写操作或外部发送；两个临时账户经各自finally清理。没有真实浏览器跟随Authentik全局SSO退出，不能据此把整项登录/退出能力签完。
- 发布后消息中心真实身份/权限/CSRF/Origin/固定集合消费/完成重试/Worker触发与后续恢复未读回归再次通过，临时账户删除、规则禁用恢复，合法历史/审计/消息按设计保留。
- 前端首轮资产检查只扫描入口而遗漏modulepreload共享分块，报告失败并进一步核实，没有盲目重发。最终7个入口/预加载JS经HTTP与当前Gateway逐字节相同，新中文429/retryAfterSeconds代码在共享块。入口index-C4mcrLQF.js SHA256=1f67402947bca3277a4fc0f98b3edd3dfca191dd2de475ec0775b7a6092b4685；共享_plugin-vue_export-helper-DZwVprX-.js SHA256=10b37de893ca8a522a7b674281043f2eb03ff412f204cf0d56198bd441653865。不是现场生产身份浏览器走查。
- 11核心均healthy，Agent/维护/周日timer active，user免密保留。本增量没有DB模型或Agent协议变更，不冒称重新执行所有原生DB套件；它们已有独立历史证据。本限流为单API进程内预算，P5多实例的全局治理仍需单独设计/实现/验收，不假称目前已经分布式限流。
