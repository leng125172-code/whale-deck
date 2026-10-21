# 容器文件读取 Agent RPC 候选

日期：2026-10-21。单工作站59项范围；只记录源码候选，不是已安装或完整文件管理验收。

## 接入内容

- Agent minor38独立`docker.files.read`，`ContainerFileService`仅InspectTarget/List/Inspect/Download四方法；无上传/Exec/命令/宿主rootId，仍只走现有UDS、peer credentials和各方法短时签名。
- 精确容器ID、PID/纳秒启动实例及严格UTF-8相对路径验证；不截断原始实例标识。元数据分页核对条数、版本、游标及Unicode标量顺序。
- 已有有界进程桥增加明确的container读profile：固定container-file-transport/stop、32位随机unit ID、当前InvocationID停止；原宿主file-transport/stop参数保持不变。没有动态命令或可选宿主路径。
- 独立容器下载解码器验证元数据文件名/版本、连续块偏移/字节数和实际SHA256；仅在helper正常退出且无尾随输出后发送成功end。每帧/写入等待、总时长和匿名缓存继续有界。
- Python errno转换为固定路径拒绝/不存在/容量/超时代码，未知异常不回传诊断正文；不把敏感路径或环境放进RPC错误/日志。

## 当前门禁

- 本机完整Release测试：2264 Unit/159 Integration通过，6项需要真实PostgreSQL明确跳过；新Agent/旧宿主文件专项107项是前轮结果（包含旧测试，不全算新增）。
- Python当前引擎/观察器42项：Windows执行17项纯策略、25项Linux-root跳过。上一756f87e的31项与真实容器验证仍有效，但不冒充本轮新增断线处理的冻结原生结果。
- 新增四方法各自签名、跨服务/跨方法误用和重放拒绝；正常/空文件、错名/错版本/受保护/负大小/超限/目录/错偏移/截断/错误摘要/重复结束均有纯回归。不是安装态流传输证据。
- 首次编译因override参数名不一致被CA1725阻止，改为生成契约的responseStream后完整回归通过；没有降低分析器或告警级别。
- 后续完整门禁一次旧OperationCoordinator测试在Windows File.Replace瞬时缺名处失败；其有界持久化轮询仅对FileNotFound等待，最终仍要求完整实际磁盘终态，修正后完整2264/159再通过。没有改产品状态/取消规则或删除断言。
- 下载捕获/发送阶段增加实际管道断开检测，固定Cancelled错误；方法绑定测试在首次合法消费前验证跨方法拒绝，不靠已消费nonce间接证明。
- 原生验证工具增加可选`--installed-helper`，要求四个已装文件字节等于冻结源码、root权限与私有状态目录；每次通过真实Agent UID调用固定helper，并检查实际systemd沙箱、完整下载及有界输出/日志。Ready半行也有总截止时间，停止失败仍关闭流/等待；八项观察器纯回归通过。工具生成不等于安装态已通过。

## 未完成与正式运行边界

当前正式仍9ec8721/Agent1.37。helper未安装，minor38未发布，尚无此次安装态systemd/签名UDS/原生中断验收；Application管理员权限/审计、Infrastructure gateway、BFF及Vue容器文件页面未接。宿主/容器文件写入、HDD版本/恢复继续是完整目标，不能用只读RPC代替。

17:00–17:30用户固定例会离线窗口内仅本地实现和测试，没有安排新工作站安装。后续应在确认在线后冻结源码、验证root helper沙箱和确切停止/断线，再验证安装Agent及新RPC，最后接业务与页面。

GLM5.3 Flash仅定位三个文件中的可复用测试，GPT负责架构、权限、修改与验证。Router一次成功/零重试，174464ms；输入311512/cache276224/输出3527 tokens，实际费用未返回。其stop参数文字不准确，已按源码及新测试确认是id+InvocationID，不照搬为实现。
