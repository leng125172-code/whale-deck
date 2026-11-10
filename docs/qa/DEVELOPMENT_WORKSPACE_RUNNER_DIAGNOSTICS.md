# .NET Runner 并发预算与有界诊断

2026-11-10，修复提交 **ddc832df2e171a5ec3913d888a43b72e5f0d2c8e** 已完成标准 dev 覆盖部署和真实身份验收。准确 [CI 42792356973](https://github.com/leng125172-code/whale-deck/actions/runs/42792356973) 的 12 路全部成功；六组件实际来源、Agent/MaintenanceHost 运行进程及六个已安装宿主模块均核对。原失败任务、报告、审计和旧未提交源码合并预览保持；不计完整主任务或 master 完成。

## 已定位的故障路径

原 OCI 隔离只挂载 /proc、/dev、私有 /tmp 与 /work，不暴露宿主 cgroup 文件系统。实际固定 .NET SDK 10.0.402 在 CPU 1000 毫核、内存 2048 MiB、128 PID 限制下，Environment.ProcessorCount 返回 **32**。多进程的编译、测试平台和 CLR 会创建超过该预算适合数量的线程。

在与正式启动器相同的 systemd 属性、委派子组、能力边界及 OCI 限制下，首轮四次全新构建成功；故意制造的编译错误实际退出 1 并提取 CS0103；随后独立测试实际退出 1，内核 pids.events:max=1。这次失败及原件全部保留。

继续使用同一 SHA 锁定的公开验收源码、固定 SDK 和相同预算作对照。原配置三次全新 dotnet test 全部退出 1：分别记录 testhost 启动 Win32Exception (11)/Resource temporarily unavailable、Out of memory、Failed to create CoreCLR, HRESULT: 0x8007000E。前两次采到 PID 上限事件，采样内未发现 OOM/oom_kill，内存峰值约 304–333 MB。第三次未采到 PID 上限事件，因此不将它单独认定为内核 PID 拒绝；短任务末尾计数可能在采样间隔内消失。

仅向 .NET 进程环境增加 DOTNET_PROCESSOR_COUNT=1 后，三次全新测试均通过 **2/2、零跳过**，采到的 PID 上限/OOM 计数均为零；独立程序实测处理器数从 **32 变为 1**。生产配置现按批准 CPU 毫核预算向上取整生成该值，最小为 1；不扩大 PID、CPU、内存、网络或磁盘权限。Node 不注入该变量。环境提示用于避免运行时过度创建线程，内核配额仍是强制限制。

这定位并修复了可重复的 .NET 运行资源故障路径。此前真实构建 c0e64639-3b0a-45d0-be32-44d69d95ee91 没有保存输出，不能追溯证明该次退出 1 必然由同一原因引起；原记录保持失败，不改写历史。

## 诊断内容及边界

Runner 在内存中扫描最多 1 MiB 输出，持续排空后续输出以防管道堵塞，只保留最多 32 个固定格式错误码、固定枚举的故障类别、总字节数，以及该次固定 workload cgroup 的 PID/内存/CPU 数值。错误码和文本类别显式标为不可信摘要，不参与授权或任务成功判定。任意输出文本、路径、环境值、凭据和源码不进入产品诊断文件或服务日志。

diagnostics.json 为原运行私有目录中的 root 0600 文件，绑定原运行 ID 和完整请求摘要。它在执行资源清理、权威终态回执落盘后尝试写入；磁盘配额或其他文件写入故障不能把已经完成的任务改成宿主失败。诊断缺失或不完整表示不可用；资源数值只是采样期间观测到的最大值，缺失数据不作零。暂未新增 UI/API 下载诊断功能，不将 root 私有侧文件冒充用户报告完整功能。

公开固定夹具的隔离对照另外保留最多 64 KiB 原始输出，仅用于上述可复现取证，不是产品对任意用户输出的日志策略。候选原件：deploy/artifacts/development-runner-diagnostics-candidate-20261110-3d78d52a/，包括首轮真实失败、controlled/result.json、各轮配置与结果、公开夹具日志和原始/修正后的测试记录。

## 隔离验证

- Runner **16/16** 单元与真实子进程测试通过：3 MiB 输出排空、1 MiB 扫描上限、拆分错误码、32 码上限、真实超时杀停回收、原范围清理、私密文件权限/请求绑定、禁止覆盖及写入配额故障。
- 隔离配置 **3/3** 通过，包括真实私有挂载验证和不同 CPU 预算的处理器提示；Node 环境不变。
- 同款正式隔离内真实 runc 超时返回 TimedOut，容器状态目录为空、workload cgroup 已删除；未以 Python 子进程夹具替代该项验证。
- 上述固定 SDK 原配置与修正配置的真实 runc 对照通过，故障原件保留；不冒充签名 Agent、实际用户和标准部署验收。

## 标准部署与真实身份验收

标准入口 whaledeck-platform-maintenance update 的发布单元已实际结束，Result=success、ExecMainStatus=0，发布校验和 host validate 均退出 0；没有将启动过程中的默认成功值当作完成。账本 current=ddc832df、previous=4ad7669a、pending 为空。11 个核心健康，8 个数据库平台容器身份与私密配置、存储布局和已授权 sudoers 指纹保持。维护入口 98 次约 2 秒间隔采样全部 HTTP 200，其中 93 次 updating、5 次 healthy；这只是采样证据，不作零中断承诺。

真实 Authentik 管理员经 BFF 和已部署 Vue 登记 **bcaaa266-c9bb-46fc-83bc-1189cb5e82ad**，分配 8 GiB 硬配额，上传源码并显式确认发布。固定 Node 24.15.0 与 .NET SDK 10.0.402 在批准的 1 CPU/2048 MiB/128 PID 隔离内运行：

| 任务 | 原运行 ID | 实际结果 |
| --- | --- | --- |
| Node 测试 | 7baa2f20-1fc4-4af5-801c-745f76644e95 | 2/2，零跳过 |
| .NET 独立构建 | df6334f5-ec06-425e-9680-eed8fb15ab56 | exit 0；不算测试通过 |
| .NET 独立测试 | 9c602123-b590-41e3-8532-fa9b69d14f54 | 2/2，零跳过；源码内直接断言 Environment.ProcessorCount=1 |
| 恢复后新 Node 测试 | d2834714-546a-43cb-b449-6f46fada6d53 | 2/2，零跳过 |

JSON/Markdown 报告下载及响应摘要匹配，320px 暗色页面已实际查看且无横向溢出。新空间完成 Active1→Stopped2→Active3；两旧空间由当前管理员管理，按实际版本 3→4→5 推进，原所有者身份没有被冒用。停用时新运行和导入被拒绝，原请求可只读重放；普通用户资源边界、CSRF/Origin、正常退出及两个临时账号删除均通过。

4 个新运行的 diagnostics.json 均为 root 0600 的唯一普通文件，绑定原 runId 和完整请求摘要，扫描/码数有界且未保存任意文本。两次 .NET 运行分别取得 26/16 次资源采样和 292/498 字节输出计数；采到的 PID max、OOM、oom_kill 均为零。缺失采样仍不解释为零；CPU 节流存在且强制配额未放宽。

独立复核保留 3 个旧空间的物理树、inode、配额及原任务/导入，10 份原报告完整行摘要、8,309 条原审计完整行摘要保持；任务事件表只比较事件数量，不声称其内容逐行核验。新旧共 14 个运行均终态、报告阶段 Complete、无租约；新运行 bundle、原始测试工件和 workload cgroup 已收尾。空间及预算继续保留，等待受支持的产品退役，未用手工删除或 SQL 删除代替。

首轮证据复核脚本在汇总时因 runId 重复关键字而 TypeError，原脚本和失败记录保留；仅将展示字段改为 workspaceRunId，原诊断绑定检查不变，重跑通过。候选故障、首次无输出构建及一次 gh 观察超时证据均保留，不改写为成功。

私有证据：deploy/artifacts/development-runner-diagnostics-release-ddc832df-20261110/ 与 deploy/artifacts/development-installed-runtime-6216613214974cb09dde89bf24cc55e9/。前者包含准确 CI、标准发布 journal、维护采样、前后物理/数据库摘要、实际诊断和 accepted-final.json；后者保留真实 UI/API 验收脚本、源码归档、报告及截图。

两目录分别封存 41/32 个文件；SHA256-MANIFEST.json 的 SHA-256 分别为 64831c575ff06fd5b7851332032a2e38c966336d0655725ebee5b6e86861d0ab 和 9146231c230d8019acaea1884c337502a747bdc0e1868d1f446c66ddd2f4d739。旧候选证据未改动。

## 剩余边界

尚无用户 UI/API 诊断下载，不能将私密侧文件算作完整 P3-17。空间退役、HDD 留存及预算释放尚待实现；NVMe /dataNvme/DevSpace 的 120 GB 与 HDD /data/DevSpace 的 300 GB 强制限制保持。完整 59 项、ST272、PET6 及 16 个供应商/Live2D 子门槛继续按原目标执行，17 项移出任务不恢复、不计完成。
