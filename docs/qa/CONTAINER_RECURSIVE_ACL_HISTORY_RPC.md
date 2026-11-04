# 递归 ACL 历史读取协议与受限传输候选

日期：2026-11-04。冻结功能提交：`2b30e09a8268c00ab38c4bbe74845f0daca0e2ab`，分支 dev。属于 P1-11/P2-03 文件权限子项，59项主任务的完整完成数不增加。

## 本批实现

- 新增独立 `ContainerRecursiveAclHistoryService/List`、`Read`，候选协议1.54及 `docker.files.recursive-acl-history`；旧1.53或缺能力直接拒绝。完整 protobuf 请求（含实例、主体/Trace、捕获/摘要、游标/版本或 offset/take）使用独立域、短时 HMAC、一次性 nonce。普通历史/ACL写入/递归ACL写入授权不得互换，没有恢复或重放写动作。
- Application DTO/校验、类型化 SDK、Agent服务及 DI 完整接通候选链路。列表一次最多50项/扫描200项，延续游标绑定目录版本；逐项读取 offset<10000、take 1–50，绑定整份捕获摘要。返回完整聚合计划的 summary 和逐项异质前像，计划计数不冒充执行结果；不返回私有 owner、原始计划或 inode。
- Agent及SDK分别核验位置、原实例、完整计数、摘要、标量 presence、分页进度与最终根目录；检查逐项路径/深度、UTF-8深度优先顺序、模式与ACL一致性、命名ID 0、显式权限0及default清空。扁平helper JSON严格投影到协议，额外字段/缺字段/伪造计数拒绝；旧实例历史只能在当前已绑定容器/目录下读取。
- 固定stdin只读 helper，读取前后验证 namespace及FD非IDMAP证明，持有原目录FD，替换目录/同inode属性变化后丢弃结果。只允许list/read、固定模块白名单和root单链接安全元数据；HDD/base故障不能变成空历史或NotFound。
- 独立systemd unit/准确InvocationID停止分支：仅 `CAP_DAC_READ_SEARCH CAP_SYS_PTRACE`，无FOWNER/CHOWN/DAC_OVERRIDE/SYS_ADMIN；ProtectSystem=strict、唯一可写读取租约目录 `/var/lib/whaledeck-container-files`、35秒/256MiB/CPU/任务/FD限制、BindsTo Agent。不向递归ACL Root或HDD授予写路径。安装/元数据校验脚本及CI语法检查已接模块，但本轮没有执行系统安装。
- 新读取行与协议响应上限128KiB；旧驱动上限64KiB保持。实际驱动行读取器边界测试验证128KiB/64KiB恰好接受、分别多1字节拒绝，无子进程或sudo运行。

## 实际验证

- 整个解决方案Release构建通过，0警告/0错误；冻结提交后再次构建，Agent和MaintenanceHost通过现有 `dotnet <dll> --build-info` 返回准确2b30e09来源，没有启动宿主服务。
- 最新全量单元4744通过、0失败/0跳过；其中本批两个协议/SDK测试类93项。Release集成569通过、7默认原生测试跳过；本轮未单独重跑真实PG/Valkey门禁。
- Linux-root递归ACL工具组138项、历史工具组71项均通过且0跳过，两组有重叠，不累加；递归历史内核/传输小组26项包含在内。8范围、安装CLI5项（空PATH dry-run，无系统操作）、Python/Bash语法与diff检查通过。
- C# HTTP handler/密钥/主体是测试夹具；Python用真实隔离文件系统ACL syscall，但ContainerRoot、映射及HDD提供者含明确夹具。这些不算安装态签名RPC、Authentik/BFF、永久业务、Vue或HDD整链验收。上批内核4新捕获/16逐项页实盘证据原样保留，不重复执行、不挪作本批RPC通过。

## 失败与修正事实

首轮新增C#测试编译触发CA1806：误留未使用的LINQ `Reverse()`，删除该调用，保留后续显式交换的拒绝测试后通过。第一次尝试直接执行候选apphost返回131，原因是默认.NET路径没有运行时；既有dotnet SDK/运行时可用，改用既有 `dotnet <dll> --build-info` 成功核对来源，没有下载安装依赖。第一次推送GitHub返回Internal Server Error/500，原提交保留，远端同步结果单独核验，不因本地提交宣称远端或CI成功。

功能提交2b30e09已正常推送origin/dev；准确源码CI `41820072967` 当前仍运行，尚未取得成功终态。项目图谱container-acl阶段以事件 `whale-deck.container-acl.2b30e09.recursive-history-rpc` 同步成功，状态active，不记完整完成。上一批1e974b0内核CI `41816829582` 已另行核实completed/success，不能替代本批CI。

## 只读安装态核对与后续门禁

私有证据：`deploy/artifacts/recursive-acl-history-rpc-candidate-46ef933756a64360995a9b94e57e32cb/`，目录0700/文件0600，不入Git。对比前批成功验收保存的完整快照：591份配置/发布账本及普通/递归ACL Root/HDD文件SHA和九字段均保持；11核心容器身份/镜像/StartedAt保持且healthy，Agent4044173/MaintenanceHost3598736及实际b880208来源保持，所有活动任务/屏障/租约/终端/锁0。永久递归ACL仍30意图（9Succeeded/5Canceled/1Expired/3Failed/12Acknowledged）、28执行/58Job。两份新模块均未安装。本轮不新建工作站容器、真实身份或HDD捕获，不重启Agent/Worker、不修改发布账本或user NOPASSWD状态。上述比较针对已有验收基线，不冒称本轮开始前另做快照。

下一批补新协议原生探针、冻结依赖和真实受限单元边界，准确CI终态后标准宿主安装并做真实签名List/Read、异质前像/分页/拒绝/准确stop和旧协议兼容验收。之后才接永久管理员授权业务/API/Vue；历史恢复必须逐inode规划，不能用当前统一fileEntries/directoryEntries覆盖异质历史。递归恢复写入/故障矩阵、可恢复删除与长期清理、P0-05维护入口连续性及其余59项仍待完成，不自动回滚dev。[目标](../GOAL_P0_P5.md)、[内核及原生证据](CONTAINER_RECURSIVE_ACL_HISTORY_ENGINE.md)。
