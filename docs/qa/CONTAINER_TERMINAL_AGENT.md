# 容器终端 Agent 基础验收

本轮继续 P1-11，不缩小 P0–P5 的76项范围。先实现和验证真正 Docker Exec 的 Agent/固定宿主驱动；BFF目标绑定许可、持久模型与前端入口仍需后续接入，不能签为完整容器终端或完整P1-11。

## 契约与边界

- 专用 `ContainerTerminalService.Inspect/Connect`，UDS peer、方法capability和单次nonce；协议1.19声明 `docker.terminal.exec`。只接受完整小写64位容器ID、本次启动实例、固定sh/bash/ash枚举及既有有界二进制终端帧。
- C#和root所有的固定驱动分别检查核心注册表/保护标签、运行状态、启动实例；禁止host/shared命名空间、特权、额外capability/设备/不受限安全配置。bind只允许注册应用根下实际匹配的挂载对象，命名卷只允许平台应用命名；不返回或记录Env值。启动恢复仅在内存检查本次非敏感进程归属标记，不把整个进程环境存储或输出。
- Exec采用容器配置的用户、工作目录和隔离，不增加root/特权，不进入宿主命名空间；root驱动只通过固定Docker Unix API建立交互Shell，不能接收任意宿主命令/UID/路径。API/Worker仍不挂Docker Socket。
- 关闭仅终止本次交互Exec，通过记录的启动时间/PID命名空间和pidfd防止PID复用误杀；不会停止容器。主动脱离终端的后台服务属于容器生命周期，不能承诺像宿主PTY cgroup那样清理全部后台服务。临时验证容器最终整体精确删除。
- root元数据只含会话/启动/Exec实例和关闭状态；不写输入输出、密码或浏览器票据。root共享4会话预算、10分钟寿命、2分钟输入空闲、1MiB输入/16MiB输出及有界管道；驱动随Agent停止，StopPost只处理同一InvocationID。

## 验证清单（执行前）

| 场景 | 必需证据 |
| --- | --- |
| 缺失/错方法/重放capability、非canonical ID、旧启动实例 | gRPC拒绝，不能Ready或新建Shell |
| 核心注册表与保护标签分别生效 | C#/Python独立测试，实机核心Inspect拒绝 |
| 容器真实Exec、配置用户、尺寸、输出与退出码 | 独立无网络/无生产卷/只读非root临时容器，实际UID1000、40×120、exit7 |
| 重复活会话与已结束UUID | 原会话不被停止、root重放拒绝 |
| 断线、仅强杀验证驱动 | 精确Exec消失，容器启动实例/PID1不变，root元数据关闭 |
| 敏感/正文 | journal和root记录不含实际验证标记，不打印终端正文或凭据 |
| 发布 | Release/CI、宿主安装验证、原宿主终端与身份回归，全部临时容器精确清理 |

上述项目目前为待执行清单，不提前声称通过。前端这轮只做只读定位，不创建假可用按钮。

GLM Flash只读定位4个UI文件，一次成功无重试；输入145066、缓存100032、输出2832 Tokens，143587ms，费用未返回。其定位由GPT核对；权限、进程身份与实现未委派。最终结果和失败记录后续追加。

## 首轮失败与修正

6fcc560首次覆盖发布的实际unit于16:25:13 CST成功结束，但容器探针在真实Exec启动阶段失败，临时容器已删除；不能因此签收容器终端。22a9469只增加有界公开诊断后再次失败，root回执明确为`exec-inspect / EXEC_START_FAILED`，没有存储Docker错误响应、异常正文或终端内容。

Docker的HTTP升级响应先于实际进程PID发布，原实现错误地立即要求Running/PID已就绪。a0b0001增加3秒有界等待真实Running且PID有效；新测试覆盖尚未运行、Running但PID未发布、超时及目标变化，不把HTTP101或连接成功当成Ready，也没有放宽最终UID/尺寸/exit断言。根回执在输入开放前绑定实际进程启动身份，正常关闭/驱动被终止使用pidfd，不按任意PID或容器名称发信号。参考：[Python pidfd接口](https://docs.python.org/3.14/library/signal.html#signal.pidfd_send_signal)、[Linux pidfd_send_signal语义](https://man7.org/linux/man-pages/man2/pidfd_send_signal.2.html)。

## 实际签收范围：a0b0001 Agent基础

- [CI 38875103617](https://github.com/leng125172-code/whale-deck/actions/runs/38875103617)三作业全绿：Release、676 Unit/92 Integration（6原生明确跳过）、完整间接依赖审计、15项Linux驱动策略、171前端单元及3项原生产构建初始授权门禁通过。本轮未生成容器终端WebUI，不把既有前端门禁当作该页面验收。
- 同源码冻结原生PG报告`dispatch-cbf658f5d7eb4eeeb5f8f195b27f8087.json`于08:43:58–08:45:36 UTC执行1/通过1并清理，SDK10.0.402摘要`sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。这是既有持久化/通知流程回归，不证明尚未实现的容器终端BFF模型。
- 更新Job `859a9f7a-3c89-4ae8-aa6b-6e195a982789`只负责调度；实际覆盖更新unit于**16:45:10 CST**成功结束，inactive/dead、Result=success、ExecMainStatus=0。源码与安装驱动SHA-256均为`04b2e320079c7455d50a56b8d8698e7c01db686564f07d278312942f9e90a3ad`。
- 新探针从同一a0b0001冻结源码独立发布，输出目录`terminal-probe.diOaMl`。真实UDS调用验证缺失/错方法/重放capability、核心Inspect拒绝、旧启动实例、非法shell、活UUID重复、结束UUID重放。临时容器使用现有固定镜像ID、无网络/无生产卷/只读/全部cap移除/资源限制/配置用户1000:1000；实际交互返回UID1000、40×120及exit7。
- 断线、仅SIGKILL本次测试驱动均使本次交互Exec消失、root回执关闭，临时容器PID1/启动实例保持不变。实际journal和root回执不含终端标记，探针不打印正文；临时容器精确删除，独立Docker列表确认无该验证前缀容器遗留。更早`terminal-probe.SF89Y9`客户端曾首次复验通过，但最终证据使用上面的同源码新探针。
- 宿主运行库/权限/UDS/动态入口、真实OIDC→BFF→受限宿主PTY、原普通/匿名权限拒绝、CSRF/Origin/通知管理及8个HTTP资产匹配回归通过；外部通知发送0，临时配置/账号/会话清理。11核心healthy，user sudo未修改且sudo -n true通过。实际Agent/维护包及API共享/Worker PKCS和框架均保留10.0.13。

仍缺容器Exec BFF的目标绑定一次性票据/持久模型/人类主体审计与撤权、页面及真实浏览器验收、文件传输、团队隔离与长期元数据清理；未宣称Docker重启/更广启动故障、所有挂载组合或任意Linux发行版均已验证。Agent基础通过不等于完整P1-11完成，全部76项目标继续。
