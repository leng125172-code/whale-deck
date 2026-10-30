# 容器普通权限：独立 Agent RPC 与安装态验收

2026-10-30 新增 Agent 1.47 候选，尚未安装时不签收真实 UDS。完整59项继续active，17项移出不算完成。

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
- TerminalProbe已编译，尚未真实执行时不能作为安装态证据。全新实际PG与浏览器验收不属于本RPC批次，也不引用旧原生结果冒充本轮执行。

## 接续

冻结源码发布安装宿主包，运行 validate_container_permission_agent.py 的签名UDS/固定helper/独立inode与完整字节/HDD元数据/准确清理；随后继续永久业务意图、共享保护、Worker/API/Vue与真实身份/故障验收。完整权限的chown/递归/ACL与容器删除等仍保留。
