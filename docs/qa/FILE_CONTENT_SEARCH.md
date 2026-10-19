# 登记根目录的 UTF-8 正文搜索

## 安装态与真实身份网页签收：5e131e9 / Agent RPC1.29

2026-10-19 已签收本节的正文搜索子范围。运行时完整提交为 `5e131e94da17f8336d170c226229b9ff7c9e3486`，产品构建版本仍为 0.1.0；RPC1.29 不等于产品版本。后续 `f475fda`、`d3e313b`、`1113092` 仅修正验收脚本，已快进工作站源码但没有重新构建或部署产品。以下“候选/尚未部署/尚未执行”为当时历史，不能覆盖本节。

- [CI39088548813](https://github.com/leng125172-code/whale-deck/actions/runs/39088548813) 与上述完整运行时提交一致，backend、installer、native-postgres、frontend 四作业全部 completed/success。1089 Unit/127 Integration、265 前端单元/213 CI 浏览器通过；6 个本地原生依赖明确 skip，不作为原生通过。既有 17 项正文真实 Linux、17 浏览和30传输旧回归及本地24联合浏览器证据保持，不把直接 Python 测试称为安装态 RPC。
- 工作站冻结同源码 PostgreSQL 报告 `dispatch-655f26fb4cc3458b8f6bcaa7f86d2cb2.json`：2026-10-18 02:01:46.696339–02:03:57.337117 UTC，实际执行1/通过1、清理确认；SDK image ID `sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`。SQL 套件中 Root double 的边界不变，安装态读取证明来自后述独立链路。
- 覆盖更新只执行一次：2026-10-19 08:10:57–08:13:27 CST，wall 2min29.899s，实际服务终态 success/`Deactivated successfully`，不是仅调度成功。基础数据库依赖未重建。六组件实际二进制/镜像元数据与回环 API/Gateway HTTP再次核对均为完整5e131e9。
- 实际运行镜像 ID：API `sha256:df619614178aeb889a7ddd4738de731dbc1be6574812f8375d1b49362dce874a`；Worker `sha256:634f7c0c2226f70afe2b18d8a7a57dceb25e0eeac1456d457138fe435a37220b`；Gateway `sha256:315ac8f4889280f77aeca5897e50e139fa43a7d4e0bf2a3567e065d1ed1ebb27`。该核对证明来源元数据对应，不是位级可复现或密码学供应链证明。
- 真实临时 Authentik 管理员 → Cookie/CSRF/Origin → API → 已安装 UDS/固定 helper → 登记根目录搜索通过。完整2MiB+7文本末尾命中、UTF-8 BOM/CRLF、字面斜线/正则字符、大小写、首次匹配行/有界片段、分页、实际读取字节/文件数、二进制/非法UTF-8/超限文件跳过计数均核验。1MiB上限整文件跳过，不用前缀搜索代替完整读取。
- 实际错误请求分别验证：非法游标字符400、合法字母表但MAC/绑定无效409、超限参数400、保护文件403、保持Origin但缺CSRF400、外源403、无会话且正确Origin的匿名请求401。独立真实普通用户确认 `isAdministrator=false`，带本人有效Cookie/CSRF仍403；没有借管理员凭据模拟普通用户。
- 真实 Chrome 网页通过名称搜索定位自有目录，再切换正文页签、搜索两个实际文件；`<script>`片段按文本展示，未执行。320/1440、light/dark无页面横向溢出；切换1MiB上限只返回一个命中并显示超大2。刷新后关键词为空、不自动重放正文查询。管理写请求0、脚本错误0。已目测320暗及1440明截图，未使用 route mock。
- 浏览器截图仅包含自有夹具，留在 ignored `deploy/artifacts/content-search-validation/actual-content-{320,1440}-{light,dark}.png`。Cookie只通过隐藏SSH/stdin/内存，未写文件、argv、日志或截图。完整SHA/原inode、uid/gid/mode、size、mtime/ctime核对不变；atime不作不变保证。退出后仅删除精确清单中自有文件、符号链接、FIFO及空目录，不递归清理业务根。
- 原 OIDC/Origin/CSRF、面板和Authentik完整退出、跳回/login通过；8个既有HTTP/活动Gateway资产字节一致。host validate再次通过systemd/权限/UDS/受限helper/HDD/动态入口，宿主.NET/ASP.NET/PKCS保持10.0.13。最终工作流、临时身份与容器健康以本批末尾检查记录为准。

### 失败记录与验收脚本边界

实机验收初轮失败并非一概视作产品通过。受限探针只记录阶段、源码行、HTTP数字，不输出Cookie/响应正文，逐项复核后修正以下脚本问题：

1. `invalid.cursor`是合法游标字母表，MAC/绑定失败沿用既有Agent FailedPrecondition→HTTP409；脚本误期望400。现分别验证非法字符400和绑定409，不放宽成任意非200。
2. 原缺CSRF测试同时移除了Origin，先被同源中间件403拒绝；现保留正确Origin单独验证CSRF400。
3. 普通用户调用共用登录帮助器时误用了默认管理员要求；现显式 `require_administrator=false` 并独立核对当前身份非管理员。
4. 普通权限策略已正确403，但其正文为空，脚本误按成功搜索页解析JSON；现仅200解析页结构，所有拒绝状态仍精确检查。空401/403的全平台Problem Details一致性仍是P0后续检查范围，不借脚本修正宣布全错误规范完成。

上述QA-only修正经最终真实链路/网页重跑通过，不冒称已包含在5e131e9原始CI中。没有修改产品权限、HTTP映射、Root策略或运行时。

最终只读复核：六类未结束文件工作流均0、路径锁0、未关闭终端0；四个验证身份前缀独立查询均0，自有正文搜索夹具0；11核心容器全部healthy。没有清除审计/永久任务/回执，也未修改sudo、网络或KDE。原生PG、主机校验和这些清理计数均在实际工作站执行，不以本地文件存在代替。

最终SHA256：工作站实际`validate_file_content_search.py`为`42e36749c98910439f4b0cc60eb5b5290dcabb1f8acdc6eee38c570c427bd931`；ignored `browser.mjs`为`3ae93e75ad9f913a298fcd5412b1aece31887c5907697384c3d8b7cea8390bd8`、`bridge.ps1`为`eb717a29a8ef0185596251b1db7827d8e5e92e6a5f598171de7408229a9e8cb6`、`regressions.py`为`838b81c5a8efa3c9a0c4069ca6c22f0efe44e6de3cdffed667a76e50d8b2f535`、`quiet.py`为`ecf751fbcb0a775fcc3252377ba906f2029a8d64c6bcdd7736875ef1d12551cb`。后二者隐藏捕获身份/异常输出，只公开固定结果或聚合计数。

GLM/glm-5.3-flash本轮仅只读定位普通文件回收、目录复制/移动的公开代码与测试，未接触配置/身份/工作站：666142输入、609408缓存、5838输出、209524ms、1成功/0模型重试，费用未返回。第一次路由命令的相对PromptFile解析在启动模型前失败，改绝对路径后成功。GPT负责实际诊断、修改、权限复核与实机验收；完整76项保持active。

## 新增候选，尚未部署

本批实现前端、API/Application/Infrastructure、专用gRPC与固定Root helper的正文搜索。现在线上仍b118454/Agent RPC1.28，不能把以下本地或直接Linux测试称为安装态/真实身份网页通过。

- 名称搜索与正文搜索分离页签、接口和游标域；正文关键词允许斜线、反斜线与正则符号，但全部按字面匹配。最多256 UTF-8字节，拒绝空白/控制字符/未配对代理字符，支持中文和Unicode casefold。
- 根目录的一次注册表快照必须同时有list+download授权，返回前复核该根配置未变化，扫描中撤销download也不返回片段；原权限不放宽。仍用Linux openat2 NO_XDEV/NO_SYMLINKS、单链接常规文件、相对路径和保护项排除。没有新增任意Shell/宿主路径、Docker Socket或端口。
- 单文件默认16MiB，前端可选1/4/16MiB，API接受1–16777216字节；超过上限整文件跳过，不只查前缀。严格UTF-8（可有BOM）、CR/LF/CRLF、不可打印二进制排除。每文件首个匹配行、最多320码点片段，HTML按文本转义，附读取时完整文件版本。
- 每页最多64MiB读取、10000枚举项、5秒、32层、100条命中；响应64KiB上限包含实际JSON转义及游标，片段预算纳入结果字节。单文件读取超时明确不完整且无无限重读游标；页字节/扫描限制可继续，目录过大/深度等限制明确显示。helper单独20秒systemd/384MiB/100%单核上限，保留原四共享槽位，不改变上传或其他文件操作的限制。
- 不声称全目录同一时刻快照。每文件读取前后fstat/版本核验，已保留祖先版本的游标继续前再次核对；已扫描并退出的子目录之后出现新文件不构成全树MVCC保证。跳过项和保护子树未搜索，页面不能把有限扫描说成全盘完成。
- 当前管理员在请求前及结果返回前复核；HTTP取消、页签/目录/条件变更、降权与迟到结果均不补回私有片段。查询POST需要CSRF/Origin，但不写Job或审计正文，不把关键词/游标放到URL、localStorage或日志。

## 当前证据

- Release全方案1089 Unit/127 Integration通过，6原生本机明确跳过；265前端单元、类型/lint/格式/生产构建通过。
- 当前源码文件在工作站独立公开源码副本直接执行：最终17项真实Linux正文测试、17浏览和30传输旧回归通过，包含完整16MiB末尾命中、长Unicode扩展折叠、完整字节计数/版本、实际目录/文件bind挂载排除、私有/链接/FIFO/硬链接、字节分页与时间终止/FD回收、扫描中根权限撤销。该证据是直接Python测试，不冒称已安装helper、BFF或网页实机。
- 新10项正文与旧14项浏览器最终24项联合回归全部通过（1.8m）。首次watch在roots初始化前求值导致页面错误，已移动到全部状态初始化后；下一轮23通过/1项Element Plus原生checkbox点击超时，改点可见标签并保持选中与请求体断言。一次后续联合末项挂起，确认进程仍存活后人工中断，未当作完成；隔离带trace验证通过（11.6s），最终完整24项通过。没有force或放宽产品权限。截图仍需最终独立目测与实机签收。
- 首轮编译protobuf目标推断歧义、RequestSizeLimit元数据访问和xUnit分析器错误已修复；未配对surrogate不能用会被属性编码替换的InlineData，改用运行时构造的Fact。前端控制字符检查改为码点检查以保持lint规则开启；保留这些失败事实，不把首次运行说成通过。
- 真实验证脚本`validate_file_content_search.py`已准备：真实OIDC/CSRF/Origin/普通用户、API→已装UDS→完整2MiB+7字节读与独立inode/元数据证明、精确自有夹具清理，以及可选隐藏私有浏览器管道。尚未执行，不以此脚本存在证明验收。
- GLM/glm-5.3-flash仅只读定位5个指定测试文件；132356输入/96512缓存/3046输出、96111ms、1成功/0重试，费用未返回。GPT负责权限/资源/游标设计、修改、测试与复核，未共享私有配置。

## 后续

固定提交的完整CI/实际PG、冻结镜像与宿主发布、真实身份/已安装UDS/浏览器和最后清理尚待完成。不由本子项签完整P2-03/P0或全部76项；目录回收/归档、容器文件、隔离/长期清理等继续。
