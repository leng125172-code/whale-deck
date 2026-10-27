# 容器 HDD 历史校验与恢复来源基础

2026-10-27，内部候选 `whaledeck-container-history.py` 已实现。它只读取固定 `container-files-v1` 历史域，不提供CLI、目标写入或删除操作；尚未接独立签名RPC、业务目录/永久恢复意图、API/UI和安装清单。这里的恢复来源校验通过，不代表文件已经恢复或整个模块完成。

## 已实现的边界

仅接受容器schemaVersion2/ContainerFile/固定HDD storageId的精确元数据字段。普通root/0600单硬链接、完整CID/原始纳秒instance/path、原sourceVersion/parentVersion/targetProof/uploadFingerprint、原inode/UID/GID/大小/时间、SHA、状态和captureKind均严格验证；未知字段、重复JSON键、宿主rootId/schemaVersion1、畸形标识或数值不被接纳。

库存读取使用当前经保护校验的ContainerRoot，绑定CID/实例/路径过滤/当前targetProof/历史目录版本。最多50条、500次扫描、5秒、48KiB条目预算和稳定游标；游标必须携带原版本。隐藏敏感路径，损坏记录明确计数；公开项不含原owner、裸inode/UID/GID或宿主路径。`Present`只表示经过私有文件形态/大小检查的归档存在，不宣称已经验证其完整内容；Preparing、Missing、Invalid均单独表达。

`verified_source`必须再次匹配CID/path及完整captureHash，仅允许Verified捕获。它在经挂载/UUID/rotational HDD/文件系统/只读策略校验的固定目录，打开普通单链接0600数据，完整读取全部字节、核对大小/SHA及稳定inode；随后重查元数据、磁盘和当前容器证明，才返回回到开头的只读文件描述符。消费结束后再次核对来源，没有重新捕获、改写、删除或发布目标。空文件也须完整通过，不能把缺失或未完成捕获当作空文件成功。

允许同CID重启后的当前合法实例读取旧实例捕获，但拒绝将旧实例充作当前调用范围；另一CID、另一文件路径、修改的captureHash、宿主历史或无效HDD均不回退为其他来源。原捕获owner只保留在内部元数据，之后的业务恢复仍需当前管理员授权，不要求原账号继续存在。

固定命名空间缺失当前按存储前置条件失败，不在GET期间创建目录或把磁盘故障伪装成空历史；接入RPC和正式安装时必须补固定命名空间的受控初始化。它不是任意用户提供的宿主目录。

## 自动化和物理工作站证据

20项新增真实Linux-root测试通过：完整2MiB+7及空文件、保留源/描述符关闭、字段裁剪、重启旧捕获、原owner独立、跨CID/当前实例/敏感路径/namespace拒绝、游标/目录版本与500扫描预算、Preparing/Missing/Invalid、同大小内容损坏、消费期间变更、hash冲突、16种元数据负例、重复JSON键、symlink/hardlink、缺盘/SSD/缺namespace不回退及精确请求形状。磁盘/namespace权威在这些单元中明确为合成夹具，不冒充物理HDD证据。

首轮新测试有一个字典参数重复的测试编写错误，失败已在本次会话工具输出记录；该临时文件日志被后续18项复验覆盖，不把它声称为保留的原始失败日志。修正后完整通过，没有更改产品校验或放宽断言。已有214项容器文件联合回归先通过；新增两项空文件/敏感记录后又执行最终完整216项容器组，全部通过，结果单独保留。Python SyntaxWarning/ResourceWarning按错误检查。

冻结七个实际源码文件至root私有束后，`validate_container_file_history.py`在真实工作站创建独占只读普通容器，并将原备份政策复制到物理HDD独占沙箱。实际暂存/捕获2,097,159字节原件，历史库存及全部原字节/SHA读取通过；错误实际UUID拒绝。实际停止/重新启动该测试容器后，以新PID/纳秒实例读取同一旧捕获，完整内容再次通过；旧列表版本及把旧实例冒充当前调用被拒绝。原件版本/内容未改，真实捕获和当前实例未映射为宿主根别名。

冻结history引擎SHA256：`14b51fdbaa26696f5e769f93ad44a1c8084f1cefa47b0d5227ac82019fb3d7b9`。完整结果 `/tmp/wd-container-history-native.log`；18项修正后记录 `/tmp/wd-container-history-unit.log`，最终20项 `/tmp/wd-container-history-final-unit.log`；联合回归 `/tmp/wd-container-history-regression.log`、`/tmp/wd-container-history-final-regression.log`。私有基线/摘要在独占0700/0600的 `deploy/artifacts/container-history-native-*`。

原测试容器/源目录与精确HDD沙箱在内容、元数据和目录身份核验后清理；另一次独立只读检查证实容器不存在、两个目录均不存在。源与沙箱均为同HDD，仅证明版本来源与重启绑定，不声称异盘灾难恢复。未修改生产备份政策、业务SQL、既有永久历史或核心服务；真实两小时权限门禁仍持原PID继续。

## 接下来的完整链路

独立内容绑定的List/ReadSource RPC及受限helper/安装；HDD捕获DB目录和当前管理员查询；永久计划/二次确认、恢复暂存/原子发布、当前目标的覆盖前留存、共享发布保护与取消/Unknown接管；然后API、桌面/手机历史选择和真实恢复验收。恢复期间只能消费再次验证的来源，不能把旧上传的ObserveRecovery/SealRecovery当作内容恢复入口。文本编辑候选仍待权限门禁结束后的完整覆盖。
