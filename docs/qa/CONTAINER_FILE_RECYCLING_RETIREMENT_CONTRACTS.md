# 退役协议合同与签名绑定准备

2026-11-09，候选 `bcfef6c09012f44e0d94a772351a1b9d640789d9`。本批在自然老化等待期间独立增加C#协议合同；没有修改已冻结的Python宿主/验证闭包，也没有注册RPC、改运行版本或宣告能力。

## 独立类型与隔离

agent.proto仅追加独立的 RetirementSource/Command/Plan/PlanRequest/Request/Receipt 消息，没有修改原ArchiveCopy消息字段、服务或摘要。新command固定承载原归档引用、显式retainDays及mode；新plan投影对应原生留存计划的20个JSON键，两个身份用原九字段格式保留独立ctime纳秒，年龄、原claim、原记录摘要、当前绑定和资格诊断均保留。

独立 request 哈希绑定完整protobuf描述符及全部字节，包含Actor/trace/job/idempotency上下文和所有嵌套事实；HMAC载荷使用独立 retirement capability 域和方法路径，不复用ArchiveCopy签名域。该工具只生成绑定材料，不是已实现的服务端验证器；尚无密钥加载、nonce消费、实际超时/重放验证或RPC授权实现。

准备的能力判定同时要求major1、minor至少60和独立 `docker.files.recycle.archive.retire` 字符串；版本号或字符串单独出现均不足。当前Agent仍为59且不广告该能力；proto没有退役service声明，运行服务注册保持原状。服务路径常量仅为后续实现预留，不代表可调用。

## 三态与摘要

Wire投影与原生Python实际摘要算法对齐，原生模块仅作隔离纯摘要计算，测试保留纳秒和Unicode。递归未知字段不能在投影中隐去，完整protobuf哈希仍随其变化。投影/指纹只是完整性表达，不校验所有原生语义，更不是执行授权；后续SDK和Agent必须另行实现深层计划、原链、owner/ID、资源、期限、请求大小及权限政策校验。

回执严格关联请求ID、source/archive ID、指纹和notBefore：Reserved要求明确false与自身围栏；Aborted要求明确false且无自身围栏；Unknown保留缺席/null或false，但禁止true；Completed要求明确true且精确拥有来源保护。旧复制成功和当前绑定没有被复用为“退役成功”。

## 验证

新增26个合同用例：C#/真实Python摘要一致、13类内容/上下文变更影响请求哈希、HMAC域/方法隔离、4类版本/能力组合、null/false保留、Completed及调度状态反例、未知字段拒绝、未声明或注册服务。归档相关C#回归 **288项通过、零跳过**；全量C# Unit **5938项通过、零跳过，29秒**。NET10.0.402编译Contracts/Application/Infrastructure/Agent/Worker/MaintenanceHost及测试项目成功；14前端CI合同、8范围及Git diff检查通过。

独立只读复核未发现合同、三态或旧协议混淆阻断项。复核不代表实际验证器/HMAC时效或RPC执行验收；这些组件本批没有实现。

构建/测试后，原老化来源在干净环境独立观察仍通过：未到期、未执行、无自动删除，最早时间仍为2026-11-10 10:00:02.252+08:00。未创建新来源或备份历史，未改原策略、原Root/claim/HDD/实例、f318运行应用或原冻结文件。

证据 `retirement-contracts-9d929e9df3d04f6cbe53db0a81bc56cb`：6文件另加manifest，SHA256 `0347512a6b958da1473cb800212d5f13a40bb2421680b4f01c86cc2b72696c90`，Root:Root/0700目录及0600文件，包含4个提交源码、观察器实际输出与测试计数核验摘要，不重建未保存的原始测试日志。

## 续跑

本批待准确新CI完整终态；未部署、不暴露未实现能力。后续补服务端政策/签名时效与单次消费、Gateway、gRPC/native适配及独立安装探针；完成版本和标准安装后再签安装态与永久业务/API/Vue/Jobs。自然老化到期后仍须独立物理重核、显式执行和完成证据/失败恢复/收尾；其余59项、维护/大文件等继续，17项移出不算完成，整项0/59、阶段0/6保持。
