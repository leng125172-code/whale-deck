# 目录回收 Agent 1.32：源码候选，尚未安装

## 已实现

- 独立 `whaledeck.agent.v1.FileTreeRecyclingService`：Plan、Apply、Inspect、Discard，能力 `files.tree-trash-intents`，协议次版本 32。
- 每个方法受现有 UDS 调用方和短时单次方法 capability 约束；Plan/Apply/Discard 必须携带当前调用 Actor。历史 owner 是操作绑定，不是当前人类权限。API 的管理员复核和业务授权仍须在下一层完成，不能让浏览器直接访问 Agent。
- 强类型源位置/版本、源父设备与 inode、完整 manifest 摘要、数量/字节预算、Root 策略和计划指纹。所有 UInt64 无损传递；不接受任意命令、磁盘路径、属主或完整成员清单。
- 回执严格区分捕获已验证、可能保留、源可能变化、源路径移除和原件已保留。Completed 必须同时具备全部完成证明；Aborted 不得宣称已开始修改源。无已验证捕获时摘要/字节仅允许空/零，不能冒充成功的零字节归档。
- 固定 helper 新增四个 `tree-trash-*` 动作，沿用隔离 transient unit、调用实例取消和资源预算；归档文件预算为 17GiB，未扩大普通文件 1GiB 传输限制。
- 可信加载六个固定树模块和已有备份适配器；所有模块拒绝外来属主、可写、硬链接和符号链接。安装/校验新增目录备份与回收模块。
- 只对精确匹配的既有默认根授予 `files-tree-trash` 并保存独立 `.pre-tree-trash.json`；自定义/收紧的根保持原样，备份冲突失败关闭。没有顺带授予 `files-tree-restore`。

## 已观察测试与失败边界

- Release 单元测试 1265 通过；其中定向回收策略/传输 75 项、额外四个方法能力签名测试纳入最终全量。首次测试构建把协议 uint 与 int 比较写错，修为准确的 `32U` 后全量通过。
- Integration 139 通过、6 个本机原生依赖测试明确跳过；不能把跳过当成真实数据库验收。
- 工作站公开源码隔离副本 `tree-recycling-agent.NlurwL`：17 根升级与 23 回收合计 40 项通过，107.403 秒；包括全部六模块不可信变体、固定驱动四动作、取消先到/重放、尾随输入和未批准命令拒绝。三个 shell 文件语法通过。实际 Linux I/O，HDD mount/udev 身份明确模拟，不是安装态 UDS/HDD 证据。
- 前序已提交引擎 `cf7a6a33a3a341c4795a1a7ef632bd987d95980d` 的冻结副本 `tree-recycling-frozen.Ocy0Fa`：首次选择命令误写不存在的 `test_tree_archive`，190 个实际测试通过但总命令 ImportError 失败（191 含加载错误，325.314 秒）；之后正确的 `test_tree_archive_codec` 24 项通过，19.073 秒。同一冻结源码合计 214 个实际测试通过，不把失败命令声称为全绿，也不累计重复执行。

## 尚未完成

本批没有部署或执行真实已安装 Agent、签名 UDS、固定 helper 沙箱或物理 HDD 回收；线上仍 `adc6cae / RPC1.31`。本批提交后的完整 CI/冻结门禁仍需跟踪，不能拿前序提交的成功替代。

仍需永久业务模型及 EF migration、当前管理员资源授权与签名确认、共享层级锁、Job/Worker、事务审计、目录历史/回收目录索引、取消与 Unknown 当前管理员处置、HTTP 和 WebUI，然后覆盖发布并通过真实依赖/角色/页面验收。HDD 目录恢复仍为内部引擎，覆盖恢复/彻底删除/长期清理与容器文件未完成；P2-03/P2-04 和全部 76 项保持 active。

本轮 GLM `glm-5.3-flash` 只读机械定位一次、180606ms 超时失败，input637451/cached591424/output4564、零重试、费用未返回；不作为通过证据。GPT 自行核对源码、做架构/权限判断、修改并执行验证。未提供凭据。
