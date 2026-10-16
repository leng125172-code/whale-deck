# P1-11宿主PTY基础：实现与验收记录

2026-10-16。全76项范围不变；这里只是主机/容器终端、Exec、相关传输会话中的宿主PTY基础。BFF一次性授权/持久会话审计/WebSocket Origin与撤权、前端终端、容器Exec及文件会话尚未实现，不能用本文件签完整P1-11。

## 已实现源码

- 独立 `whaledeck.agent.v1.HostTerminalService/Connect` 双向流，协议1.18/capability `host.terminal.pty`；UDS peer检查仍适用。补齐双向/客户端流的capability拦截与双向错误脱敏，不继承默认无鉴权continuation。
- 首帧UUID/主体/尺寸，随后仅input/resize/close；不接受UID、路径、环境或可执行文件参数。允许输入shell文本是PTY交互本身，不把它偷塞入旧通用命令RPC。
- Agent和root注册表都批准固定 `whaledeck.host/terminal-open`。实际shell为独立 `whaledeck-shell`，无sudo/Docker组、清环境/补充组、NoNewPrivileges与只读宿主系统；不会把临时user免密sudo或Agent的Docker权限传入shell。它提供受限默认主机终端，不是root控制台；容器终端另行实现，不能以此替代。
- 固定root helper创建独立transient systemd unit，PID1直接以低权账户启动PTY，capability集合为空。仅固定预处理/结束钩子以root身份登记/封存无正文的UUID/boot/InvocationID记录，不读取键盘输入；匹配InvocationID才能封存，不让被拒绝的重复会话修改原记录。完整cgroup清理、绑定Agent存活、660秒硬期限、driver600秒/空闲120秒/首帧10秒。最多4个活动会话、输入1MiB/单帧4KiB/速率32KiB每秒、输出16MiB、慢消费者5秒，root记录24小时后有界清理，最多4096条。
- 会话ID root持久占位防重放；Agent拒绝活跃重复ID，清理必须带root返回的systemd InvocationID，不按裸PID或可被重用的名称停止别人的会话。
- shell输入输出仅走管道/PTY/gRPC，不进OperationStore、数据库、文件日志或journald；验证探针也只输出布尔结果/阶段/错误类型，密钥只从远端私有文件进内存。

进程清理语义按 [systemd官方KillMode文档](https://www.freedesktop.org/software/systemd/man/latest/systemd.kill.html) 和 [systemd执行配置](https://github.com/systemd/systemd/blob/main/man/systemd.exec.xml) 设计；实际单元/PTY/断线/强杀仍须原生验证，不能由文档或单元测试推断成功。

## 本地证据与待原生

610后端单元、86集成通过，6原生本机明确跳过；全方案及新增TerminalProbe Release构建0警告/错误。新增17个协议用例与双向能力/错误脱敏测试通过。初次编译发现StreamWriter返回Task不能AsTask，已修正再完整测试。

Python协议测试不创建PTY或更改宿主，原生探针将验证真实UDS、能力缺失/错方法/重放、身份/组/私钥不可读、resize/退出码、重复ID不误停原会话、断线和driver被杀后清理主动setsid的子进程。不信任手写客户端或Windows跳过；必须实际执行并记录。

首版b07cf14已覆盖发布但尚未签收：真实探针在shell初始化退出126，独立临时单元复现setuid阶段EPERM，CapBnd=e0而实际CapEff/CapPrm=60，缺少手动降权需要的CAP_SETUID。没有因此扩权，而改由PID1直接启动低权PTY；固定root元数据钩子保留文件系统/NoNewPrivileges限制、不向PTY授予capability。冻结b07cf14原生PG回归实际执行1/通过1/清理，报告dispatch-9ece9cee4a5b47dfa26df29eeb65d546.json；它不证明PTY通过。期间诊断探针修正了setsid测试夹具（交互shell后台任务本身是组长，需fork后再setsid）并只报告固定断言码，不输出正文。新验证探针发布从git archive读取跟踪源码，不再把含私有env的完整工作区挂入SDK。

GLM/glm-5.3-flash只读定位成功1次、重试0、163900ms，输入546218/缓存489216/输出4383 Tokens，费用及资源包扣减未知。其“没有peer guard”判断错误，GPT已按真实 `PeerCredentialGuard.cs` 纠正，未采纳；权限/生命周期/实际修改由GPT复核。未向外援传凭据或委派核心判断。

## c9568e3 原生与工作站基础签收

- 冻结源码 `c9568e3c39a280aa9344560cd124cb7adec67084` 的独立 PostgreSQL dispatch 报告 `dispatch-f72b9660df6f42a28a8d67f0706ca13d.json`：2026-10-16 03:21:56–03:23:58 UTC，实际执行1/通过1、`cleanupConfirmed=true`。SDK镜像ID `sha256:c0790639332692a0d56cdd81ed581cfd24d040d9839764c138994866df89a3b6`。此回归不替代PTY测试。
- 开发覆盖更新Job `9b132496-bf42-4bd2-841c-7c33c63a8379` 只用于提交更新。实际 `whaledeck-platform-update.service` 在2026-10-16 11:30:52 CST成功结束：inactive/dead、Result=success、ExecMainStatus=0、journal记录Deactivated successfully，未请求回滚。
- 从同一冻结源码git archive构建的 `terminal-probe.XUevIV` 经SHA256SUMS检查后以root运行，实际UDS能力缺失/错方法/nonce重放拒绝、UUID重放拒绝、活跃重复ID不误停原会话、固定非特权UID/组/工作目录、私钥不可读、40×120 resize和exit 7全部通过。
- 实际断线、仅强杀本次测试driver两种情况下，主动fork/setsid脱离会话的子进程均被清理，root元数据active=false。探针检查journal与root记录均无终端正文。未停止Agent/Docker/生产容器来制造故障，尚不证明Agent重启或宿主掉电演练。
- Python原生9项协议/真实root临时元数据测试通过。宿主安装校验、真实OIDC/Gateway任务1006事件与续传/终态/错误/清理、通知私有值完整链路、管理权限/CSRF/Origin、8个HTTP资产及私有配置UDS回归通过，外部发送0。临时对象按验证脚本清理，所有终端transient unit均已退出；11核心容器healthy，Agent/维护入口/timer active，user开发sudo保留。

这只签收宿主PTY基础；完整P1-11仍在开发，不代表已有浏览器终端、BFF一次性票据/持久会话审计、容器Exec或文件传输。

后续BFF接入前由GLM/glm-5.3-flash机械只读定位源码1次成功、重试0：263107ms，输入587191/缓存545600/输出5676 Tokens，费用未返回。GPT核对现有AgentGateway、鉴权handler和持久化调用；不存在WebSocket传输，模型未作权限或核心架构裁决，未向其传递凭据。
