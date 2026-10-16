# 容器终端 Agent 基础验收

本轮继续 P1-11，不缩小 P0–P5 的76项范围。先实现和验证真正 Docker Exec 的 Agent/固定宿主驱动；BFF目标绑定许可、持久模型与前端入口仍需后续接入，不能签为完整容器终端或完整P1-11。

## 契约与边界

- 专用 `ContainerTerminalService.Inspect/Connect`，UDS peer、方法capability和单次nonce；协议1.19声明 `docker.terminal.exec`。只接受完整小写64位容器ID、本次启动实例、固定sh/bash/ash枚举及既有有界二进制终端帧。
- C#和root所有的固定驱动分别检查核心注册表/保护标签、运行状态、启动实例；禁止host/shared命名空间、特权、额外capability/设备/不受限安全配置。bind只允许注册应用根下实际匹配的挂载对象，命名卷只允许平台应用命名；不读取或返回Env值。
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
