# 目录捕获清理 Agent 源码候选（2026-10-21）

状态：active；RPC 1.36 尚未安装。生产仍为 2c8d1bb / Agent 1.35。清理业务任务、数据库墓碑、管理员确认、页面和安装态签名 UDS 验收尚未完成，不计完整 P2-03/P2-04 完成。

## 本批实现

- `FileTreePurgingService` 提供独立 Plan、Apply、Inspect、Discard；新增 `files.tree-purge-intents` 能力和 minor 36，旧覆盖恢复最小 minor 35 要求不变。复用现有 UDS peer credential 和不超过 60 秒、方法绑定、单次 nonce 的签名 capability，不开放 TCP 或通用命令。
- 输入只接受登记根相对路径、来源捕获 ID/owner/类型、版本/清单及明确 Archive/Originals/All 范围。原捕获 ID 不能作为新清理操作 ID。当前 actor 形状校验不是 Authentik 管理员校验的替代品；当前管理员复核必须由后续 BFF/Worker 再接入。
- 固定驱动输入为 6 字段命令、18 字段计划、12 字段回执；拒绝额外、缺失、重复字段、错误 JSON 类型、错误 hash/范围、替换捕获和数量。64 位 inode/device 保留整数精度；Archive 的四个原件证明必须显式 JSON null，protobuf optional 空串不等于未设置。
- 最多 10,000 项、16 GiB 原件及 17 GiB 已压缩归档边界沿用独立目录策略；普通文件 1 GiB 限制不变。Completed/Aborted/Unknown 与删除事实严格对应，未选组件不能声称被删除；Unknown 重放不续删。
- helper 仅增加四个固定 `tree-purge-*` 动词及既有目录资源预算。可信加载器只使用固定、root 所有、单链接、不可组/全局写、非链接、长度受限的模块，组合真实回收/覆盖来源。EOF 在加载与变更前确认；安装器及元数据验证器包含新模块。
- `file-roots.v1.35.json` 冻结旧默认配置；仅完整匹配已知默认配置时增加清理 grant，原件保存为 `.pre-tree-purge.json`。自定义 owner/path/action 或混合新旧根不自动扩权，冲突备份/原子替换失败保留原配置。生产配置本批未改。

## 实际证据与限制

- Release 构建成功，0 警告、0 错误；`dotnet test --configuration Release --no-restore`：1856 Unit / 146 Integration 通过，6 项本机原生依赖测试明确跳过。无数据库 schema 修改，不宣称本批新增真实 PostgreSQL 验收。
- Linux 冻结源码包 SHA256 `553A6218573B48798FB49FEF8EC343F139BE3BE2831D8E5BA4C4596CEC18D048`，本机和工作站一致；`test_tree_purg*.py` 30 项通过（82.624 秒），`test_file*.py` 184 项通过（1.590 秒）。含真实临时目录、1 GiB+7 完整校验和独立 TAR 解码、6 个 SIGKILL、固定 loader 对 10 个依赖模块的真实权限/链接/类型/长度拒绝、固定动词、EOF、取消栅栏、Completed 重放和 Unknown 不续删。临时 HDD 挂载身份为测试夹具模拟，不冒充真实物理 HDD/安装态验收。
- capability 单元测试独立覆盖无签名、其他四类文件 service、其他动作、过期和 nonce 重放，均不能进入 continuation；这不是已安装 RPC 验收。三范围/两种来源、字段与 uint64 边界、执行前拒绝及单向删除事实有独立契约单元覆盖。
- 以上冻结包在最后新增安装器元数据断言之前生成；该断言只增加 root:root/0750/单链接/非符号链接验证，不声称它已通过实际安装。最终源码 CI 需按新提交运行另行记录。
- SSH 实际连通；Agent/维护入口 active，11 核心容器 healthy。无生产清理、无生产根配置迁移、无 API/Worker/前端部署。
- GPT 负责实际修改及高权限边界；协作代理只补独立只读契约/临时 I/O 测试；GLM 5.3 Flash 只读定位旧 protobuf/策略，由 GPT 复核，不接触凭据或执行高权限决策。

## 下一步

构建并安装 Agent 1.36，使用独立自有夹具完成签名 UDS/固定 helper/真实归档与精确清理验收；接当前管理员、短时确认、永久 Job/Outbox/资源锁、事务审计及数据库墓碑，再接页面。孤儿/Unknown 处置、长期元数据策略、容器文件及完整 76 项继续，不因本批通过而删减目标。
