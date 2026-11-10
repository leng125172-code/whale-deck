# 维护入口 socket 接管候选与真实隔离验证

2026-11-10。本批修复安装态归档验收时取得的维护服务重启监听空档；只登记已实际发生的结果，不增加完整主任务计数。

## 问题与实现

已部署的 88f2df86 在重启直接持有 8080/8081 的 MaintenanceHost 时，HTTP 观察取得同轮四端点 ECONNREFUSED errno111。原证据保留于 development-archive-installed-88f2df86-20261110，不能按维护 HTML 503 解释。

新增单个 whaledeck-maintenance.socket，由 PID 1 持续持有两个 IPv4 wildcard TCP listener。维护服务只接收严格匹配自身 PID、两个 FD、固定名字、TCP/listening/AF_INET/0.0.0.0 及完整端口集合的激活信息；全组验证后才交给 Kestrel，不依赖 FD 顺序。生产使用 required，缺失或错误信息失败关闭，不回退直接绑定。开发/测试只有 disabled 且完全无 LISTEN_* 上下文时可直接绑定。

保留非 root 服务身份、AF_NETLINK 和原请求/本机网卡/容器桥策略；未扩大 Agent 的 systemd 资源或 sudo 权限。socket 保留默认早期启动顺序，网络就绪与控制器恢复排序放在 service，避免 sockets.target/basic.target 的启动循环。普通升级只重启 service，不重启活动 socket。validate 同时验证模板、激活关系、PID 1/活进程共享监听 inode 和两个实际 TCP 连接。

首次从旧进程自持监听迁移需要一次受控切换，安装器仅在 socket 未活动时执行。该迁移窗口不能签无断档；还须随后一次真实新版本标准部署，证明进程更新期间 socket 持续存在。

## 已执行验证

- Release 构建与 linux-x64 自包含发布成功，运行时固定 10.0.13。
- 维护相关 .NET 测试：57 passed、0 failed、0 skipped；维护控制 Python 测试：58 passed；installer CLI 5 项通过。
- 真实 PID 1 systemd、真实候选 Kestrel 与无外部接口/路由的私有 netns，只启用 lo。两个 QA unit 均通过 NetworkNamespacePath 指向固定的自有 namespace bind mount。生产服务、容器、配置及部署账本保持。
- 以非 root 身份反序 FD 正向启动，8080/8081 的根维护 HTML 和状态 JSON 均真实返回 503。11 类错误上下文/FD（含错误 PID/count/name、重复端口、UDP、未监听、loopback-only、缺 FD、缺环境及禁用/非法模式）均因 MAINTENANCE_SOCKET_ACTIVATION_INVALID 拒绝，未回退绑定。
- 正式 unit 模板衍生的隔离服务 5 次重启、6 个实际不同 PID，两个监听 inode 始终由 PID 1 与新进程共同持有。2779 次持续 HTTP 观察均为预期 503，错误 0；最长 768.71 ms。8080 根/状态各 675/714 次，8081 根/状态各 675/715 次。
- QA 服务、socket、namespace mount、冻结候选目录精确收尾；生产原 PID/InvocationID、容器身份与保护配置未变。

成功实测证据：deploy/artifacts/maintenance-socket-native-c5684a9b96814695b05a5c5f10432199/。汇总/测试日志及源码快照：deploy/artifacts/maintenance-socket-20261110-4af208c6/。

前置失败全部保留：夹具函数名遮蔽模块、执行路径权限、宿主 /run noexec、降权后的工作目录不可读，修正后进入真实 HTTP；随后过快第六次启动触发 start-limit-hit。最终测试拉开重启间隔，未减弱默认限流。各自唯一 maintenance-socket-native-* 目录记录失败与收尾，不能冒认通过。

## 部署和验收边界

前一 QA 提交 4af208c60feff1390e7d1d88ce0e56794ad3c6ff 的准确 CI 42823961806 已 12/12 success。本批 socket 候选仍需自身准确 CI；新增 CI 独立运行同一真实 systemd/netns 门禁。

当前实机仍为 88f2df86。本批尚未生产切换、未证明正式升级连续性，也未执行真实身份/BFF/UI 回归，不签 P0-05 完整验收。持久归档意图、授权确认、Worker/BFF/Vue、HDD 捕获校验和空间退役/预算释放闭环仍待；59 项与 ST/PET/供应商/Live2D 全部门槛继续，不恢复 17 项移出范围。
