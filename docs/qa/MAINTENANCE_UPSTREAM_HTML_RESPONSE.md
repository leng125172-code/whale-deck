# 维护代理的上游 HTML 错误边界

2026-11-11。此修复尚未发布；实机六组件仍为 `6d87fde1`。完整主任务 **0/59**，本批不计 P0-05 整项完成。

## 真实失败与可复现缺口

`6d87fde1` 准确 CI 13/13 success 后执行一次标准更新，更新退出码 0、六组件/23 模块同源和 11 核心健康通过。473 次观测中出现一次 `/health/live` HTML502，持续约 3.115 秒；448 次 200、24 次维护 HTML503、零连接错误。原证据与安装核验分开封存，见[真实部署退役记录](DEVELOPMENT_WORKSPACE_RETIREMENT_INSTALLED.md)。没有重新发布来覆盖失败。

样本没有原响应体或当时旧 Gateway 日志，不能断言具体是 DNS 缓存还是 API 容器连接中断。已确定的代理缺口是：`UseStatusCodePages` 只填充尚未开始、没有 Content-Type/响应体的错误；当上游直接返回 HTML502 时，它不会将该响应转换成维护页。真实回环 Kestrel/YARP 的对照用例明确复现旧链仍透传 HTML502。

## 候选改动

- 仅普通 Gateway 和 Authentik cluster 在 YARP 响应体转发前识别上游 HTML502/504，改成不可缓存的 503 维护页并抑制原响应体；清理旧长度、编码与实体校验头，避免旧实体元数据污染新页面。
- HEAD 只返回维护响应头。正常服务恢复后原样透传 200；没有自动重试任何原请求，POST 的实际上游调用计数为 1。
- 保留 `application/problem+json` 业务错误、认证错误和专用流路由的原状态、内容和正文。沿用有界状态读取、固定健康探测、当前路径及原维护页 HTML 转义。没有改变身份、BFF、CSRF/Origin、目的地选择或权限判定。

## 验证边界

`deploy/artifacts/maintenance-upstream-response-20261111/` 保存 Release 构建（0 警告/错误）和测试输出。完整单元 **6277 passed / 0 failed / 0 skipped**，普通集成 **837 passed / 0 failed / 7 skipped**；7 项专用 native 环境跳过不计通过。其中 11 项新回归包括旧缺口对照、两 cluster、HTML502/504、POST 一次转发、HEAD、恢复及业务错误/专用流保持。首版 10 项专项输出也保留。8 项范围、3 项公开 native 状态和 14 项 CI 合同通过。

16 文件、11,079,899 字节已封存，manifest SHA256 `9ef3e5ffe6c5f0598534d3b9b94f0394958e64e1bd41c0fd91ed8162c7fc92c0`。TRX 读取器首次假定摘要 `notExecuted` 应为 7，但当前 runner 将其写为 0，已改用 total-executed 及 7 个明确 `NotExecuted` 结果核对，未重复执行或将跳过算通过。

这些用例运行真实 HTTP/Kestrel/YARP，但上游错误由专用夹具产生，**不能代替已安装标准发布时的 API/Gateway 重建验收**。新提交仍需准确 CI、标准覆盖发布、六组件同源和真实升级连续性复验；原 502 保持失败，不宣称已在生产修好。破坏性退役中断恢复与其余目标继续。
