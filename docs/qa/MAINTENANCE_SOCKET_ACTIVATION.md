# 维护入口 socket 接管、真实隔离验证与标准升级

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

## CI 与首次标准部署

前一 QA 提交 4af208c60feff1390e7d1d88ce0e56794ad3c6ff 的准确 CI 42823961806 已 12/12 success。最终修正提交 c1374ef292ffcf1ea8ba05c29fb87573027941b1 的准确 CI [42828184656](https://github.com/leng125172-code/whale-deck/actions/runs/42828184656) 已 13/13 success；新增独立门禁实际取得 2830 HTTP、6 进程、12 类 FD 场景、零连接错误及资源收尾。下载的 CI 证据已校验，不用其他提交的成功代替。

da1d902f56d140128e0b05287c398d4530cd0c42 已推送；其 CI 42827404148 的新门禁在创建证据目录时失败：干净检出没有 deploy/artifacts 父目录。尚未运行 Kestrel，不能追认为通过。已改为显式创建父目录，并补 CI 成功/失败证据归档下载；原失败 job 129868207829 日志保留于 maintenance-socket-ci-fix-20261110-da1d902f/ci-failed-job.log。以同样没有 artifacts 父目录的全新私有副本实际重跑，12 类 FD 场景、6 进程、2771 HTTP 观察零错误通过；随后最终脚本再实测 2776 次零错误，生产保持与精确收尾均通过。最终证据 maintenance-socket-native-8df6360658d8460fbfe6c64150de7c38，干净副本证据已经移出临时目录并保留，副本已删除。当时修正提交尚待自身完整 CI 与部署；后续结果见下段。

上述修正随后完成准确 CI 和首次标准 dev 发布。2026-11-10，正式干净检出经固定维护 update 入口部署 c1374ef2，发布单元 Result=success、ExecMainStatus=0。六组件同源、23 宿主模块字节匹配、11 核心 healthy，8 个数据库依赖身份及 18219 条发布前记录保持；正式 host validate 的网卡入口、桥隔离、双端口及固定运行时检查通过。发布单元成功日志取证后精确退役，审计保留。

首次旧进程自持监听到 PID 1 socket 的受控切换共观察 3316 次 HTTP：3282 次 200、33 次真实维护 HTML 503、1 次 8081 根入口 ECONNREFUSED errno111（2026-11-10T14:15:23.384860Z）。激活后两个 listener inode 始终相同且 PID 1 持有。原始拒连保留，不能将首次迁移签为无断档；仍须随后一次真实新来源版本的标准发布，证明已活动 socket 不重启且 HTTP 无拒连。

安装态真实 Authentik 身份、Chrome、Vue/BFF 启停及退出回归通过，目标空间 Active epoch11→13；可信 root QA 客户端另外验证安装态 Agent Plan、签名拒绝、幂等和预取消。临时账号删除，4 个新增任务日志保留而执行 cgroup 为零；18214 条原不可变记录、其他 4 空间、18 原报告、源码、inode 与硬配额保持。新增 4 条审计、6 条任务事件、2 条任务及 2 条生命周期记录；最终活动任务和文件锁均为零，HDD 未产生 Capture。NVMe 120GB/HDD 300GB 完整预留强制限制、原配置、user NOPASSWD 及用户原未提交改动保持。真实身份仅证明启停流程；root Plan 不等于产品持久归档授权。

封存证据：deploy/artifacts/maintenance-socket-release-c1374ef2-20261110/，99 文件，SHA256-MANIFEST.json 摘要 44986acd8be8d30af6391d8a5f72cf4157ea7687586724571d60bb74d90b8b63；10 份前序封存清单重新逐文件核验通过。独立验收见 batch-acceptance.json、final-state.json、installed-socket.json、installed-plan/accepted.json；首次迁移拒连见 deployment-errors.json。检查旧证据时曾误用文本清单名，改按原 JSON 清单格式验证，未修改旧清单或原件，调整记录保留。

## 已活动 socket 的真实标准升级

2026-11-10，2e4709bd3eb163a1aa099141f84aa1050e3eba98 的准确 CI [42834959739](https://github.com/leng125172-code/whale-deck/actions/runs/42834959739) 最终 13/13 success。首次 installer 在受控退出观察夹具的 Node 子进程超过 10 秒处失败；原日志保留，不能推断产品身份根因。本机原夹具 90 次（最慢 0.066 秒）及原 869 项整套（98.624 秒）均通过。等其余 12 项成功且 run 结束后，仅原样复验失败 installer 一次并通过，没有改断言、超时或产品代码。workflow 仍运行时一次复验请求被 GitHub 拒绝，未启动重复 job；成功日志下载一次 EOF 后重取成功，保留脱敏记录，根因仍未确证。

正式干净仓库固定为 2e4709bd，经标准维护 update 入口发布成功；六组件/23 模块同源、11 核心健康、8 个原数据库依赖身份及 18233 条发布前记录保持，正式网卡/桥隔离与维护校验通过。独立开发仓库随后提交的 afcb11c2 不会改变本次固定本地 HEAD 的发布来源。

四端点升级观察共 2536 次：2505 次 200、31 次真实维护 HTML 503、零连接错误。维护 PID 从 513312 变为 635279，8080/8081 始终为 socket:[80823811]/socket:[80827796]，PID 1 持有，激活时间 466060830994 保持。监听、实际新进程 FD/CLOEXEC、模板与前后来源复核均通过；只签该次标准升级的实际观察窗口，首次旧自监听迁移的拒连不改写成通过。

真实 Authentik/Vue/BFF 启停、退出及安装态 Agent Plan 回归通过，目标 Active epoch13→15。18228 条原不可变记录、其他四空间、18 原报告、源码与硬配额保持；新增 4 审计、6 任务事件、2 任务和 2 生命周期记录。临时账号删除、四个新 Plan/预取消日志保留，执行 cgroup、活动任务及文件锁均为零；HDD 仍无 Capture。120GB/300GB 完整预留强制限制、原配置、user NOPASSWD 和原用户未提交差异保持。成功发布单元精确退役，journal 留存。

证据：deploy/artifacts/maintenance-socket-upgrade-2e4709bd-20261110/，120 文件，SHA256-MANIFEST.json 摘要 1f5215671df903613dcfe523c53a478ead0247f328983c962c5109e247399fa6；12 份前序清单及原件再核通过。batch-acceptance.json、upgrade-continuity.json、final-state.json 与 installed-plan/accepted.json 保存独立结果。一次普通用户读取私密 root QA JSON 被拒绝，改为授权 root 只读，保留权限且未放宽验收条件。

## 未完成边界

后续 afcb11c2 布局修复也已完成 13/13 准确 CI 和标准发布。1600 次四端点采样为 1568×200、32×维护 HTML503、零连接错误，维护 PID 更新而同一 PID 1 监听及激活时间保持；真实身份只读布局及正常退出通过，18247 原记录、5 空间、18 报告和 epoch15 不变。发布单元退役、源码配额及原配置保持；71 文件证据及首次观察器失败见[布局实机验收](DEVELOPMENT_WORKSPACE_DESCRIPTION_LAYOUT.md)。

活动 socket 的上述标准升级连续性分项已通过，不签 P0-05 整项完成。持久归档意图、授权确认、Worker/BFF/Vue、HDD 捕获校验和空间退役/预算释放闭环仍待；完整主任务仍 0/59，ST/PET/供应商/Live2D 全部门槛继续，不恢复 17 项移出范围。
