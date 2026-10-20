# 目录覆盖恢复：业务绑定材料与调度基础（2026-10-20）

## 实现边界

新增独立 Application 模型、验证器、服务、Dispatcher、仓储抽象和 Domain 操作实体，以及 Infrastructure 的签名保护器、RPC Gateway。尚未接入数据库映射/EF migration、仓储实现、Worker 注册、Controller、当前管理员 Unknown 处置或 WebUI。因此本批不是可用的覆盖恢复入口，也没有部署这些未完成业务。完整 P0–P5 共76项继续 active，P2-03/P2-04仍部分实现。

- 公开创建请求仅允许捕获 ID、目标位置与目标版本，历史 owner、源归档和摘要必须由服务端历史解析，不接受客户端声明。
- 原输入捕获、目标双树计划、确认、执行和覆盖前备份使用分离的签名用途；捕获锁与原有不存在目标恢复共用命名空间，不能并行消费同一捕获。
- 输入/目标清单、内容与归档 SHA、大小、计数、策略、目标父目录和目标 inode 等完整绑定；UInt64 inode 和大字节数不做 Int32 截断。
- Completed 必须满足真实备份已校验、旧原件保留及目标已修改等回执条件；Unknown 不虚构目标版本或备份证明。备份捕获有独立签名，与输入捕获不能互换。
- 服务在读取/计划/确认/取消前验证当前权限；调度先 Inspect 后执行，已开始的宿主操作只对账、不盲目重做，并续租、检查权限和取消。尚无 SQL 实现，不把纯测试中的租约当作 PostgreSQL 锁验收。
- Gateway 要求 Agent 1.34 独立 capability/四方法前缀。只注册保护器和 Gateway，不注册不可运行的业务服务或 HTTP 入口。

## 验证与首次失败

候选 Release 构建零警告/错误，1678 Unit 通过、143 Integration 通过、6项本机原生依赖明确跳过。本机未运行 PostgreSQL migration，不用旧提交 CI 代替本批证明。新增183项纯测试：77签名/绑定、81 Gateway 映射、25服务/租约；Gateway 使用真实映射方法的反射测试，不是已安装 UDS、BFF 或文件系统执行证据。

完整回归首次暴露 Windows OperationStore 原子替换时的共享冲突：File.ReadAllText 与覆盖式 File.Move 组合在并发观察时失败。仅增加读取 FileShare.Delete 仍不足；保留失败事实，未用重试吞异常或禁用测试。

修复改为实例内序列化写入、独占新临时文件并 Flush 到磁盘、已有文件 File.Replace/首次文件无覆盖 File.Move；只清理自身临时路径。快照读取使用 ReadWrite|Delete 共享。新增持有旧快照时发布新快照及100轮屏障协调并发测试，原 Coordinator 观察器也实际读取完整磁盘 JSON。8项 Store/Coordinator 专项与上述全量1678项通过。该 Agent 修复仅源码候选，未安装。

## 协作与后续

两路 GPT 代理仅补绑定及 Gateway 测试，主代理审查并负责生产代码、架构与实际修改。GLM `glm-5.3-flash` 只读首轮盘点：1次成功、0重试，input332550/cached297472/output3328，179701ms；平台未返回费用。其推测的 DTO 名称经主代理纠正，未采用混入旧工作流的通用覆盖开关。

后续补不可变捕获类型/签名兼容、EF migration 与事务仓储、共享路径/捕获锁和预算、永久双 Job/Outbox/Worker、当前管理员 Unknown 对账及 API/WebUI。再做同源码真实 PG、安装态签名 UDS、完整独立磁盘/真实身份 BFF 与页面验收；不能跳过这些步骤签收覆盖恢复。
