# P1-11宿主PTY基础：实现与验收记录

2026-10-16。全76项范围不变；这里只是主机/容器终端、Exec、相关传输会话中的宿主PTY基础。BFF一次性授权/持久会话审计/WebSocket Origin与撤权、前端终端、容器Exec及文件会话尚未实现，不能用本文件签完整P1-11。

## 已实现源码

- 独立 `whaledeck.agent.v1.HostTerminalService/Connect` 双向流，协议1.18/capability `host.terminal.pty`；UDS peer检查仍适用。补齐双向/客户端流的capability拦截与双向错误脱敏，不继承默认无鉴权continuation。
- 首帧UUID/主体/尺寸，随后仅input/resize/close；不接受UID、路径、环境或可执行文件参数。允许输入shell文本是PTY交互本身，不把它偷塞入旧通用命令RPC。
- Agent和root注册表都批准固定 `whaledeck.host/terminal-open`。实际shell为独立 `whaledeck-shell`，无sudo/Docker组、清环境/补充组、NoNewPrivileges与只读宿主系统；不会把临时user免密sudo或Agent的Docker权限传入shell。它提供受限默认主机终端，不是root控制台；容器终端另行实现，不能以此替代。
- 固定root helper创建独立transient systemd unit，完整cgroup清理、绑定Agent存活、660秒硬期限、driver600秒/空闲120秒/首帧10秒。最多4个活动会话、输入1MiB/单帧4KiB/速率32KiB每秒、输出16MiB、慢消费者5秒、只保存无正文的root私有UUID/boot/pid开始实例记录，24小时后有界清理，最多4096条。
- 会话ID root持久占位防重放；Agent拒绝活跃重复ID，清理必须带root返回的systemd InvocationID，不按裸PID或可被重用的名称停止别人的会话。
- shell输入输出仅走管道/PTY/gRPC，不进OperationStore、数据库、文件日志或journald；验证探针也只输出布尔结果/阶段/错误类型，密钥只从远端私有文件进内存。

进程清理语义按 [systemd官方KillMode文档](https://www.freedesktop.org/software/systemd/man/latest/systemd.kill.html) 和 [systemd执行配置](https://github.com/systemd/systemd/blob/main/man/systemd.exec.xml) 设计；实际单元/PTY/断线/强杀仍须原生验证，不能由文档或单元测试推断成功。

## 本地证据与待原生

610后端单元、86集成通过，6原生本机明确跳过；全方案及新增TerminalProbe Release构建0警告/错误。新增17个协议用例与双向能力/错误脱敏测试通过。初次编译发现StreamWriter返回Task不能AsTask，已修正再完整测试。

Python协议测试不创建PTY或更改宿主，原生探针将验证真实UDS、能力缺失/错方法/重放、身份/组/私钥不可读、resize/退出码、重复ID不误停原会话、断线和driver被杀后清理主动setsid的子进程。不信任手写客户端或Windows跳过；必须实际执行并记录。

GLM/glm-5.3-flash只读定位成功1次、重试0、163900ms，输入546218/缓存489216/输出4383 Tokens，费用及资源包扣减未知。其“没有peer guard”判断错误，GPT已按真实 `PeerCredentialGuard.cs` 纠正，未采纳；权限/生命周期/实际修改由GPT复核。未向外援传凭据或委派核心判断。
