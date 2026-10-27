# 文件只读与宿主传输退出清理竞态

2026-10-27，真实浏览器部分上传中止后的下一次预检遇到间歇409。原只读 stat 的 CONTAINER_FILES_NOT_FOUND 已在Agent日志出现，但API得到通用 FailedPrecondition。检查发现容器写入的 --collect 清理竞态已有修复，只读容器及宿主文件的 stop 分支仍采用旧实现；清理异常能够覆盖原读取结果。

## 确定性复现及修复

将已有真实helper脚本/systemctl替身测试扩展到三种固定家族：宿主file-transport、容器只读container-file-transport、容器写入container-file-write。总计24项，覆盖正常停止、停止前已回收、show与stop之间回收、调用身份不符、存活/被替换的InvocationID，以及停止前后总线故障。

修复前24执行、10失败，原写入8项仍通过；只读与宿主分支复现竞态或错误地将总线失败当作“不存在”。修复后24项全部通过，bash语法检查通过。日志 `/tmp/wd-file-stop-race-before.log`、`/tmp/wd-file-stop-race-after.log` 保留。

只对原精确固定unit和InvocationID发一次stop。stop失败时再次show；仅在show成功且InvocationID确实为空时将系统自动回收视为成功。总线故障、仍存活或已替换的InvocationID继续失败关闭，不能再停止另一个实例。未增加任意unit、命令或路径权限。

## 无服务重启的安装

先证明安装文件仍逐字等于fab6e2e部署副本，再以同目录新inode/fsync/原子replace覆盖固定helper，保持root:root、0750、单硬链接与非符号链接。原文件和安装摘要保存于独占0700/0600私有证据；Agent身份通过既有受限sudo执行self-check成功，三个停止分支对新生成不存在的精确unit均成功。

已装helper SHA256：`12ecb9ed79002a46d559f3bd9825f909aab8ad395ecac36d1a366d5ee543e74e`。原SHA256：`9ad0f41263ebb03b58d2b03b79163168804b4643204ba080c57e7d69d6de1e7a`。

没有重启Agent、API、Worker、维护入口或任何核心容器；两小时自然权限观察的原PID继续运行。当前程序集/HTTP仍fab6e2e，helper单独覆盖，不能宣称整套运行时已变更到新提交。下一次完整覆盖必须使用包含此修复与文本编辑的后续HEAD，不能仅部署旧023f6d1而丢掉修复。

真实浏览器中止及跨页退出门禁正在重新执行，结果另行记录；不能仅用确定性竞态测试签浏览器流程或整个文件模块。
