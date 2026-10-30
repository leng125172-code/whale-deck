# 容器普通权限：独立 Agent RPC 与安装态验收

2026-10-30 冻结 `c1f3cd7ca55d8c9d96238850fcf013ce8656c061` 的 Agent 1.47 已安装，并通过真实签名 UDS/固定helper验收。完整59项继续active，17项移出不算完成。

## 实现

- 独立 ContainerPermissionService 的 Plan/Apply/Inspect/Discard、完整 protobuf 请求摘要和专用短时单次 capability 域。通用 capability 不能访问，其他文件授权域不能挪用。
- 冻结容器实例/原9项inode版本/父inode/内核FS身份/普通mode/属性摘要/noChange。命令不提供宿主路径、选择UID/GID、递归或ACL输入；10GiB普通文件的权限操作不套上传内容大小限制。
- SDK要求1.47及docker.files.permission-intents，缺少能力不回退通用入口。只有严格私有原回执缺失返回null，容器消失或HDD错误不等于操作不存在。
- 固定root模块与私有stdin、GUID+InvocationID停止、systemd沙箱和独立root状态0700；权限helper不授予CAP_CHOWN。HDD元数据域与内容备份分开。
- Completed/Aborted/Unknown 保留true/false/null模式事实；HDD可能存在、已验证与原摘要不能混为一项。原回执重放不重做chmod，目标消失只观察。
- 安装/校验和CI来源已接通。尚无正式权限业务SQL/共享屏障/Worker/HTTP/Vue，不开放公共写按钮。

## 已执行源码检查

- Release构建零警告错误；3280 Unit、209 Integration通过，7项默认原生依赖明确跳过。91项新RPC/SDK/模式与签名专项包含在全量中。
- 329容器文件Linux-root关联测试全执行通过，其中9项新私有传输边界；74安装来源测试、8范围基线通过。语法、diff检查通过。
- 首轮新传输测试1失败/2错误：误用context manager本体，未使用__enter__返回值，已修正；新目标不存在时显式拒绝SESSION_REQUIRED，既有原回执仍只观察。首轮日志保留/tmp/wd-permission-transport-tests.log，重验为同名前缀-second.log；未放宽范围或把失败改成成功。
- TerminalProbe已编译并完成下述实际执行。全新实际PG与浏览器验收不属于本RPC批次，也不引用旧原生结果冒充本轮执行。

## 实际安装与独立复核

- 冻结宿主包`deploy/artifacts/host-permission-c1f3cd7`与同源码探针`terminal-probe.jG4i1F`，包内.NET/ASP.NET与PKCS均10.0.13。安装前实际SQL任务/终端/路径锁/上传与目录屏障为0，记录11核心的ID/镜像/状态/StartedAt。第一次临时只读检查导入了错误模块，未进入安装，修正为已有owned.common入口。
- 10:55:30 CST Agent/MaintenanceHost正常覆盖重启，宿主校验通过：systemd自启、UDS权限、固定helper、动态网卡入口及运行库。两个实际/proc主程序inode与安装文件匹配；实际UDS能力为1.47。API/Worker/Gateway未重建，仍a59ec97。
- 实际证据目录`deploy/artifacts/container-permission-agent-0d71b6e1eb8b417fa0b6da2eab209981`（root私有）。完整真实签名Plan/Apply/Inspect/Discard，文件0600修改/无变化/恢复0640、目录0700修改/恢复0750、明确取消并拒绝迟到修改，共6操作5Completed/1Aborted。每操作Inspect/Apply/Discard重读原结果；原单次token重放、未签名、跨授权域、修改请求体/源ctime、特殊位、核心容器和只读层均拒绝。
- 文件完整字节、原inode/UID/GID/大小/mtime保持；目录原inode/UID/GID/mtime及子项完整9字段/字节不变。权限改变ctime不应假装保持原值；无变化操作则全9项与版本确实不变。恢复原mode使用新的独立受限操作，不是失败自动回滚。
- 五份HDD元数据均由FileBackups固定配置实际挂载/UUID/rotational证明，独立permission命名域、根权限与文件0600、原plan/operation/owner、资源类型及域摘要核对。第六份取消记录没有HDD写入。永久Root/HDD记录保留，不将元数据历史称为完整内容备份。
- `verify_container_permission_agent.py`第二进程只读复核6份Root原件、5份实际HDD文件、完整版本链、原inode/属性和准确CID/来源清理、零临时unit及11核心ID/镜像/状态/StartedAt不变。完整内容由前一验收进程在清理前检查，第二进程不会声称重新读取已删源文件。
- 安装后旧目录RPC独立实际回归通过：`container-directory-agent-9c1ae56b329f4c3aa66e366398bbbcc5`，原计划创建/原回执重读/永久取消与准确清理通过。
- 首次实际探针前置校验因Docker输出父目录仍属user而拒绝，在创建夹具前结束；仅精确构建目录改为root后重验通过。独立复核第一轮因内存tuple与JSON list直接比较失败，只作结构规范化后重验，ID/镜像/状态/StartedAt全部比较没有放宽。失败日志均保留。
- 日志：`/tmp/wd-permission-{host-publish,host-install,host-validate,probe-publish,agent-actual,agent-actual-second,agent-independent,agent-independent-second,existing-directory-regression}.log`。上一aa79e35 CI40988052464五项最终success；c1f3cd7 CI40994025505仍执行时不签终态。

## 接续与完整范围

永久业务意图、SQL迁移/不可变事实、六方共享发布保护、Worker/API/Vue、当前管理员Unknown处置及真实身份/故障验收仍待实施。完整权限的chown/递归/ACL、容器删除与其余59项保留；该RPC子范围的安装态证明不等于P1/P2整项完成，也不增加未确认的HTTP写入口。
