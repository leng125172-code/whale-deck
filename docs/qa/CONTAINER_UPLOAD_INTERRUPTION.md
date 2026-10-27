# 容器上传：部分接收后的取消、退出和真实断线

2026-10-27，在已部署 `fab6e2e6f2c358d4e8c83f90b78f536d8f87b43e` 上执行。未部署或重启产品组件，未修改 SQL、缓存或系统时间。

`validate_container_upload_interruption_bff.py` 通过真实 Authentik 管理员、原 BFF Cookie、CSRF/Origin、SQL、Worker、UDS 和 Root，使用独占普通容器。每项声明 67,108,871 字节，实际先发送 65,536 字节；触发前独立证明 SQL Receiving、Root Receiving、0600 非空 partial 的实际 inode 和请求大小一致，且 RootStarted/发布屏障尚未建立。

- 显式取消：原 HTTP 连接仍打开时，Worker 已完成 Canceled/Root Discarded；此后再尝试发送 32 KiB，原 Root 回执不变，暂存载荷不再出现。
- 正常双端退出：同样在连接仍打开时完成 Canceled/Discarded；原 Cookie 重放 401，同主体新登录仍是管理员。新的登录没有恢复原上传。
- 真实 TCP RST：不发送显式取消请求，Worker 最终 Failed/CONTAINER_UPLOAD_INTERRUPTED，Root Discarded。

三项均没有目标文件、PublishedVersion、发布事件或遗留 partial/data。永久 SQL 任务、审计和 Root journal 保留；自有容器和源目录按精确指纹清理，11 个核心容器不变。最终完整运行日志 `/tmp/wd-container-upload-interruption-bff-2.log` 保存通过结果。

首轮验证失败也保留：它错误地要求取消后未完成的 HTTP/1 请求立即返回响应。nginx 可继续等待未发完的请求体直到空闲预算结束，因此读取响应超时不证明取消失败或成功。修正后的门禁在原连接仍打开时直接观察 Worker/Root 的正向终态，再验证迟到字节无法恢复接收，最后关闭测试连接；没有以超时或关闭客户端充当后端已取消的证据。

这不是浏览器点击进行中取消，也不是进入发布阶段后的故障或 Unknown 接管。后两者、Worker/Agent 重启恢复、容器编辑及 HDD 历史恢复仍需独立验收。

## 两小时权限边界：自然到期验收通过

`validate_authentik_permission_expiry.py` 已启动真实自然到期验收：只移除本次独占临时账号的组关系，保留同一原登录，定期请求管理员入口。没有改缓存、SQL 或时钟；不能因两小时前仍返回 200 就声称失败，也不能据此声称到期后已降权。

严格门禁拒绝任何请求时间已超过原 PermissionExpiresAtUtc 的管理员成功授权，随后要求管理员入口 401/403 与退出或降权。2026-10-27 最终进程自然结束，退出码 0：246 次观察、实际持续 7203.941 秒，原登录在权限到期后首次观察返回 403，没有任何到期后成功授权。观察间隔导致首次拒绝记录比边界晚 4.562290 秒，不代表其间曾获准访问；没有延长原快照，也没有换登录或调整时钟、SQL、缓存。

私有证据目录为 `deploy/artifacts/permission-expiry-f5806324f4c445cf85c62ea89e9bf609/`，最终日志 `/tmp/wd-permission-expiry-20261027-strict.log`。原权限边界为 `2026-10-27T06:36:25.472654Z`，首次拒绝记录为 `06:36:30.034943Z`；独占临时账号已按准确身份删除，原进程已退出。此前较宽松观察器被主动终止后改为严格门禁，旧日志不计通过；最终结果来自上述完整自然两小时运行。
