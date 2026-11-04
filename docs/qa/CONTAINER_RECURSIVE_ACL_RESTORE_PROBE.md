# 递归 ACL 恢复：原生探针与独立证明候选

日期：2026-11-05。协议候选为 [e066ec1](CONTAINER_RECURSIVE_ACL_RESTORE_RPC.md)，本批最终工具源码为 `ae79e86c7f12491f8c9f000d58d9fef490bd8f61`（前序工具提交 `253f667`、顺序修正 `80eb887`）。**没有安装产品，没有调用新恢复 RPC，也没有真实 Authentik/Worker/SQL/Vue 恢复验收。** 59 项完整完成数不增加。

## 已实现的工具

- `TerminalProbe` 新增固定 `container-recursive-acl-restore` 模式，固定本机 UDS 和自有 `work/recursive-tree`，只接受有限行输入、64 个命令、12 分钟总时限。header 校验自身冻结源码版本及 fixture/core 区分；先要求协议 1.55 与独立恢复能力，再读取 HMAC 密钥，finally 清零。没有本地引擎写入回退、任意 hostPath 或进程启动入口。
- 原生模式支持小型来源引用 Plan 和 Apply/Inspect/Discard，保存原 operation 的完整计划及 context，禁止同 ID 换计划。预置 **22 项 Plan 拒绝**与 **23 项 operation 拒绝**，包含旧 ACL/递归 ACL/只读历史/通用签名域、错误方法、nonce 重放、来源/目标/上下文篡改和 frame presence。这是待安装后执行的矩阵，**本批没有取得这 45 项安装态 RPC 的通过结果**。
- 新独立 Python 证明库严格转换恢复专属 protobuf JSON；不调用旧普通 ACL 的 `proto_plan()`，不导入或执行恢复写入引擎。检查小型命令、来源摘要十字段、frame 九字段与零值 presence、规范整数和有界计数。
- 独立证明绑定实际 source metadata、来源文件 frame、当前根身份/版本与恢复 plan digest；检查新恢复 namespace 的 aggregate history / manifest / heterogeneous targets 摘要，逐路径原 ACL、非目标 ACL 前像、属性摘要和目标模式。
- 子计划还必须与外层 targetProof/uid/gid 一致；根项父身份/版本与外层一致，其余项与当前树父 inode/版本拓扑一致，拒绝 inode 别名；aggregate operationId/owner 必须与调用者提供的实际请求精确匹配。负测包含篡改后重算 child/manifest/outer/history 全部相关摘要，防止只检测“不自洽哈希”。

## 本批实际验证

| 检查 | 结果及边界 |
|---|---|
| 新证明专项 | **13 项通过**；9 项纯证明与范围检查、4 项真实临时 inode ACL 的证明/拒绝顺序检查；容器、HDD 与映射均为显式夹具 |
| 递归探针关联回归 | Linux-root **156 项通过，0 跳过**，包括上述 13 项，不重复相加 |
| C# 探针 Release | 零 warning / 零 error；最终冻结提交经仓库标准脚本、固定 SDK 镜像 digest、自包含 linux-x64 产物构建及 SHA256SUMS 核对通过 |
| 严格 Python 编译 / diff | 通过，新证明与专项已加入 CI |
| 当前目标范围 | **8 项通过**；保持 59 项及“17项移出不算完成” |
| 实机提前拒绝 | 最终冻结探针的 header 缺字段、错误探针版本、core 充当 fixture 三项 startup 拒绝；当前旧 Agent 缺少 1.55 能力，在 capabilities 阶段拒绝；共 **4 项预期 exit1**，stdout 均无 ready/写入结果 |
| 旧能力兼容 | 同一最终探针产物以旧普通递归 ACL 模式执行 **ready → quit**，实际取得 minor54 并正常 exit0；没有 Plan/Apply/捕获 |
| 当前服务 | 前后两次独立读取比较 11 核心容器 ID/镜像/启动信息及两个宿主 PID/来源保持，活动工作检查通过；宿主仍为 `390889d` / 1.54 |

最终自包含探针：私有 `deploy/artifacts/terminal-probe.SXxz3u/TerminalProbe`；源码是 ae79e86。最终提前拒绝证据为私有 `recursive-acl-restore-probe-final-54de13937b32465d9f8b6bc5b62fcb96`，11 份证据文件的 SHA 封存并复核。较早 80eb887 的能力拒绝和构建记录在 `recursive-acl-restore-probe-f4559e588f61406497543f59d7275b50` 原样保留，不能当作最终 Plan/Apply 实测。

这些实机结果只证明旧版本的安全能力拒绝与旧模式握手兼容；`productInstalled=false`、`restoreRpcExecuted=false`、`sourceCaptured=false`、`humanOrUi=false` 均明确保留。没有真实新恢复 HDD/子回执、旧结果不重放、故障状态或 UI 的新增签收。

## 修正与原失败

首轮新证明专项 10 项中 1 项失败：证明函数硬编码了未来实机 fixture 的普通属性，而临时夹具使用 `user.preserve`。改为由调用者显式提供读取实际普通属性得到的摘要，保留属性校验；复验与完整 156 项通过。首次正则的无效转义警告也改为 raw string，严格编译通过。没有产品写入失败或生产数据修复。

按真实内核执行顺序修正两个原生拒绝预期：不同路径先触发来源不匹配，得到 FailedPrecondition，并不证明只读父目录写保护；错误当前 expectedVersion 在构造计划校验处得到 PLAN_INVALID/InvalidArgument，不应预期 Aborted。两项均补真实临时 ACL 内核测试，未为匹配探针而修改产品错误语义。

复核补齐了子计划外层权限与父拓扑绑定及自洽重算摘要负测。上述修正均在冻结最终探针之前落盘；中间构建产物保留自己的源码身份，不冒充最终版本。

## 仍缺的安装态验收与业务

1. 接完整安装态验收脚本：先通过旧签名 RPC 在新独占容器产生真实 access/default 来源；再运行新签名恢复的完成、no-op、取消及全部拒绝矩阵。来源探针使用独立日志目录，避免覆盖恢复 transcript。
2. 在独立进程实际重开 Root/HDD/源文件：核对来源 frame、aggregate/child 原件及其独立域、操作/owner/context、计数/outcome 与 transcript。恢复后再产生另一笔合法变更，迟到 Apply 原操作必须只观察，不能改新树。当前证明库没有完整这些采集、child history/outcome 或 transcript 证明，不把库测试升级为安装验收。
3. 准确 CI 成功后标准宿主安装；分别保存安装 SHA、真实 RPC、清理前独立物理核验和清理后原件复核，继续维护入口连续性观测。协议提交 e066ec1 的 CI `41869390182` 在本批最后作业查询中仍有 `frontend-browser (1/6)` 运行，其余十作业成功，尚未取得全成功终态。
4. 再接独立永久意图/EF/共享保护/Worker/API/Vue 和实际 Authentik 恢复整链、故障矩阵、可恢复删除及其余 59 项。保留 user NOPASSWD 和 dev 覆盖发布，不自动回滚。

项目图谱已用稳定事件 `whale-deck.container-acl.ae79e86.recursive-restore-probe` 同步 active，projectId/key/eventId 回执一致，pending 已转 synced。首次工具提交远端推送遇 GnuTLS 握手中断，保留该失败并正常重试；未关闭 TLS 校验或修改全局代理。
