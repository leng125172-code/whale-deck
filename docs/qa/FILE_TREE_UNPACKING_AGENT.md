# 解压 Agent 1.31：已安装，真实大归档签收仍待修正探针

## 实际覆盖73513a4与首轮安装态失败

四CI39258251245全部成功；发布Job `0b89c3df-6e1f-424c-9748-f27f3df80b51` 只代表调度。实际systemd Invocation `68fedc54a7df4821bbd591f4b754d08d` 于2026-10-19 21:38:54至21:43:28 CST执行并 Deactivated successfully，墙钟4分33.801秒；没有自动回滚。Agent/API/Worker/Gateway/Maintenance/Migrator和实际HTTP均证明来源73513a40731de7c6060542c082664be2a646f9c6；11核心healthy、宿主验证通过。已安装Agent/Maintenance的.NET、ASP.NET与PKCS均10.0.13。

首轮真实探针在四种小归档组合RPC完成、取消栅栏执行之后，于 `unpacking-large-archive-and-full-materialization` 失败：`FILES_SIZE_LIMIT`/FailedPrecondition。自有夹具 `wdv-agent-unpack-19f59f2b3e0a4200` 保留；独立磁盘观察尚未执行，不能把小归档RPC成功冒称整轮或完整磁盘验收。未重启或回滚产品、未自动重做/清空现场。

原因是探针调用普通Inspect准备大归档版本；该接口复用普通文件1GiB检查，尚未进入大归档的Unpacking Plan。现有有界List已提供大文件版本，浏览器选中行也是此语义。修正客户端从List获取版本，额外断言普通Inspect仍拒绝大文件，不扩大普通传输预算；新增Linux元数据回归通过。产品模块没有改动。

验证专用修复可与已安装运行时分开冻结：仅当**全部**差异路径均属于validation/docs/项目非敏感元数据，才允许不同client/runtime修订；禁用Git重命名检测，产品源码移入docs也会以src删除被拒绝。真实临时Git正/负控制覆盖此边界，Windows清理短时锁不跳过断言或忽略清理失败。最终证据必须分别记录两个SHA，不以客户端修订冒称已安装版本。

修正客户端Release构建0警告0错误；1项Linux大文件列表/普通检查与下载限制回归通过，3项真实Git差异边界测试在Windows通过（8.536秒）。前两轮Windows测试出现临时目录清理锁，保留失败事实；关闭本测试仓库的后台监视/维护并增加最多5秒、最终仍须成功的清理重试，没有跳过或忽略失败。CI登记行与业务层草稿暂留下一批，验证修正提交只包含validation/docs。

下面“候选/尚未安装/未执行”为阶段历史；完整解压BFF/持久任务/Worker/UI仍未完成，全部76项继续。

## 候选73513a4的工作站准备结果

73513a40731de7c6060542c082664be2a646f9c6已推送dev并快进工作站干净源码；b12b8da的CI39255299138四作业全绿。候选CI39258251245的backend/native-postgres/installer已成功，完整浏览器仍运行，尚未安装或执行新UDS探针；实机Agent仍为abef670。不能把“准备完成”当作安装态解压签收。

同源码工作站隔离PostgreSQL实际门禁 `dispatch-77aa606fda9d465c85c57f899684ba54.json`：UTC13:14:01.070702至13:16:22.868669，执行1/通过1/清理确认；固定SDK镜像身份 `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。临时库/容器已清理，外部投递0。此证据不覆盖下面未提交的业务层源码。

已由同一Git归档构建自包含Linux客户端，输出 `terminal-probe.XE03zA`；在root权限下核对完整SHA256SUMS通过。目录root/0700不向普通用户放宽，早先用普通用户检查可执行权限被拒后改由root进行只读核验，未修改权限。客户端尚未运行，不据构建通过声称真实RPC通过。

### 本地业务材料草稿，未提交/未部署

新增独立Domain记录类型、Application模型/接口/严格验证、签名封装和Agent网关映射。输入归档证明在各终态绑定，内容/格式/目标父身份/当前登录及独立计划/执行ID入签名；同一密钥且相同载荷下，压缩和解压确认签名不能互换。新增19项绑定测试通过，当前本地全量1215 Unit/137 Integration通过，6原生PG明确跳过。首轮CA1859分析器要求测试局部变量使用具体类型，已修正而未关闭规则。

这些类型没有注册为可用API，没有DbSet/EF迁移/持久仓库、Worker或页面，不能称持久解压业务已实现。为保持73513a4安装候选冻结，暂不提交/推送这些本地改动；同源码工作站PG证据仍只归属73513a4，不冒称覆盖草稿。接下来完成持久双任务/共享层级锁、取消/Unknown当前管理员恢复、人类审计、API和真实页面。

## 安装态探针已准备，尚未执行

新增 `TerminalProbe tree-unpack` 和 `validate_tree_unpacking_agent.py`：真实方法签名/nonce/owner/版本/计划/取消、PAX/GNU与TAR/TAR.GZ、恶意成员/已有目标和1GiB+7实际新目录发布；独立Root观察者逐成员完整哈希、登记归属/模式/纳秒mtime、源完整身份及回执核对，未知结果保留现场。成功才按确切成员/设备/inode验证清理自有夹具和6个测试回执，不触碰生产任务/审计。客户端Release编译0警告0错误、Python AST通过；这不是探针已经执行或RPC已签收的证据。

探针准备轮GLM/glm-5.3-flash仅读取3个公开发布/探针文件，1次成功/0重试、48267ms、62176输入/31808缓存/1318输出，费用未返回；GPT复核定位并承担实际发布、权限、清理与测试结论。

前序b12b8da的干净Git归档已真实复验：153文件树/147.248秒、173旧文件/1.546秒，共326项零跳过通过。0a53150的CI39252861305四作业已成功；本批最终安装态、同源码真实依赖及CI终态仍须另行记录。下面候选段保留当时的证据范围。

## 已实现的接点

- `FileTreeUnpackingService.Plan/Apply/Inspect/Discard` 为独立 RPC；能力 `files.tree-unpack-intents`，协议次版本1.31。既有复制/移动/压缩/文件回收协议、签名和回执不变，没有新增 TCP Agent 端口。
- 根内归档来源及新目录目标、显式格式、源版本、完整归档 SHA/字节、内容 SHA/条目/展开字节、双方策略、目标父设备/inode与计划指纹绑定。UInt64完整保留；完整成员清单不跨私有 JSON 帧。回执的归档 SHA/长度表示输入归档，因此 Aborted/Unknown 也保留输入证明，不误用压缩回执“只有成功才有输出归档”的语义。
- 每方法使用现有 UDS peer guard、最长60秒及一次 nonce capability；压缩/复制的 capability 不可用于解压。ActorSubject 不是人类登录授权证明，BFF/Worker 仍必须实现当前管理员、原登录确认、共享层级锁与审计。
- `tree-unpack-plan/apply/status/discard` 是固定 helper 白名单动作；固定 root-owned、单链接、非组/全局可写模块加载，安装及校验清单纳入新模块。helper 内存384MiB、CPU单核、128FD、600秒内部/620秒宿主上限、输出单文件16GiB，与既有普通传输1GiB预算分离；没有 shell、任意路径或 UID/GID 入参。
- 默认根显式新增 `files-tree-unpack`；只精确匹配已知历史默认时升级。新增1.30基线及 `.pre-tree-unpack.json` 原字节备份，属主/权限保持、自定义或收紧策略不扩张、冲突备份失败关闭，重复执行不换 inode。这里只修改仓库安装配置，工作站登记根未改。

## 本批实际测试

- 定向C#：98项通过，无跳过；包括新计划/回执/输入校验、错误方法和 nonce重放、普通管道EOF边界及既有相关回归。
- 全后端：Release构建0警告0错误，1196 Unit /137 Integration通过；6项本机原生PostgreSQL跳过，不能据此称真实PG门禁通过。无 schema 变更。
- 独立公开源码工作副本中，153项文件树原生Linux测试、147.767秒、零跳过通过，含23解压测试和可信加载器的owner/模式/硬链接/符号链接拒绝。另15项登记根升级测试通过；三份宿主shell语法通过。`ResourceWarning` 按错误处理。
- 扩展旧文件侧回归 `test_file_*.py`：173项、1.570秒、零跳过通过，包括前述15根升级（不能重复加总为额外15项）、普通传输/变更/浏览/正文搜索/历史备份等。与153文件树套件合计326项。
- 上一提交0a5315098334e9eebda7cb3f12a14f2229027275的干净Git归档另跑152项、146.517秒通过；这是前序内部引擎证据，不冒称本批Agent安装态。前序CI39252861305的backend/native-postgres/installer已成功，frontend仍执行，尚不称四作业全绿。

## 尚未验收与后续

**没有安装1.31，没有已安装UDS/helper实测，没有新的API/Worker持久任务或WebUI。** 线上保持abef670/RPC1.30。固定本批提交的原生门禁/CI、真实安装态签名探针仍须完成，不能用直接Python或C#策略测试替代完整RPC。

接下来按独立数据模型/EF迁移、持久计划及双任务、共享锁/取消/Unknown当前管理员恢复、Worker/人类审计、API与页面继续。安装态真实身份/磁盘和失败证据完成后才签解压子范围；目录回收恢复、ZIP等格式、容器文件、隔离/长期清理及全部76项仍在目标内。

GLM仅只读定位既有压缩接点，实际路由用量见[内部引擎账本](FILE_TREE_UNPACKING.md)。GPT完成全部修改、权限/安全边界复核和上述真实测试。
