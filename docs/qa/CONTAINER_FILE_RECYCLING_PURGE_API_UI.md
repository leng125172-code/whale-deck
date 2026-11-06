# 留存原件清理管理员 API 与 Vue 候选

2026-11-06，候选 e56cb48b003a2a993ba1680aacf38e66989a4df7。承接[真实 PG 持久化](CONTAINER_FILE_RECYCLING_PURGE_PERSISTENCE.md)，补齐来源目录、管理员 API、Vue 预检/不可逆确认/原任务查询/取消和独立 Unknown 处置。未部署生产，未签真实 Authentik、页面、Agent、文件及 HDD 清理整链；Jobs 完整投影和留存策略仍待实现。

## 输入、来源与权限

新增 containers/{CID}/files/recycle-purges 登记、sources、status/by-key、confirm/cancel 和 files/container-recycle-purges/recovery 清单/status/acknowledge。全部 Administrator、NoStore、65536 字节限体积，沿用 BFF/Cookie/CSRF/Origin。服务端每次动作仍重查当前权威管理员及原登录；跨管理员异常处置采用专用入口，不冒用原登录。

登记仅接受规范非零小写 D 格式 sourceIntentId 和单个规范幂等键，不接收客户端 owner、路径、原计划、属性或执行 ID。确认采用原预检签名、明确 confirmed 和非负 int64 version；修改键须匹配原意图。专用输入转换器/查询过滤器拒绝额外、重复、缺失、大小写别名、非规范 GUID 和类型强转，不把登记等同删除授权。

来源查询 take 1–50，可用规范 GUID 游标；SQL 只读同容器已完成 Trashed、历史验证/可用且原件保留未恢复的来源，排除活动、已清理或结果仍为 Unknown 的清理与恢复。服务再次验证原业务 MAC、来源资格、唯一 ID 和容器边界，并在读取前后重新授权。只返回有界元数据，不返回旧 owner、登录、属性字节或完整计划。目录是历史摘要；实际预检/确认仍重新核验当前实例、原件与 HDD。

## 页面与恢复引用

容器文件区增加“清理留存原件”，先选择原来源、生成预检，再单独确认。最终确认框明确删除不可撤销、保留 HDD 备份及当前尚无清理后的归档恢复入口；“暂不清理”不提交。Ready 过期、当前实例变化或响应未核实都禁止确认。现有写入器共享本页锁，独立清理锁不反向禁用自己的查询和确认。

客户端严格绑定意图/规划/执行/来源、位置、摘要、原身份、期限和版本；预留证明与 purgeApplied true/false/null 分开展示。Unknown 不等于原件仍在，不提供自动重做或取消按钮。只保存有界 subject/CID/sourceIntentId/原 key/id 引用，不保存 token、确认签名和完整计划；丢回执、重载、可见性恢复只查询原引用，显式重试登记仍用原键。退出、失权、主体/实例切换、卸载通过 AbortController/generation 清理与拒收晚到响应。服务端授权不由这些前端措施替代。

Files 增加独立异常处置：选择原 Unknown、可以暂不处置、明确确认后核对原回执。响应不明时只查询原处置，不自动重发。Acknowledged 保持历史 null 清理事实，结束业务保护不报告清理成功，也不删除 HDD 历史。

## 候选验证与失败事实

40 个 HTTP 检查通过：35 个输入/元数据/直接控制器合同，5 个真实 loopback Cookie/CSRF/Origin 拒绝用例。loopback 的身份/Agent/仓储是显式夹具，不是 Authentik/PG/实盘整链。69 个清理编排检查含 7 个新来源服务检查；完整 5623 Unit 零跳过、782 Integration 成功/7 原生依赖默认跳过（总 789）。前端 69 文件/1391 单元，类型、ESLint、Oxlint、格式与生产构建通过。

构建后的本机 5175 预览使用合成协议身份/API：8 个新场景全部通过，含 320/1440 宽且 600 高、明暗四组合、最终确认与暂不清理、断线查询原键、Unknown nullable 事实、退出晚到 GET 和人工 ACK；四张实际浏览器 PNG 保留并人工检查 320 暗色。原回收/独立恢复的 18 个页面回归也通过，不能转签真实业务验收。

新增场景已加入六分片 CI 清单；14 CI 合同/8 范围检查通过，实际收集原 753/现 786，新增 33、重复和遗漏均 0。这是收集证明，远端准确 CI 全终态另行核验。

保留失败：初次 TS 检查发现测试仍引用旧 sourceState，修正为 purgeApplied 后通过；首次浏览器默认 headless 缓存缺失、8 例未启动，改用已有 /opt/google/chrome/chrome，未安装新浏览器；第二轮 4 通过/4 失败是测试误用 messagebox role，实际 Element Plus 使用 dialog，修正后全 8 场景从头通过，产品检查未降级。证据权限整理遇到 root 所有的只读快照，限定本批目录用 sudo 设私有模式，未改所有者或放宽其他目录。

收尾范围检查曾发现新 nextStep 漏掉固定“17项移出不算完成”说明，恢复该说明后重新执行；没有改变59项或恢复移出任务。

## 原生目录检查与生产边界

标准冻结 Git archive 的真实隔离 PG 宏已实际 1 执行/1 通过、零跳过，来源目录 GUID 排序/游标、活动排除和取消释放及已清理/Unknown 来源不可选均被断言；原持久化/共享围栏回归也通过。状态文件 dispatch-62be6b46db19409e9eef0d02c10fe000.json 为 Passed、cleanupConfirmed=true，两个临时数据库和 SDK 容器已清理，外部投递 0。身份/计划/Root 回执在此宏中仍为夹具，不转签真实页面/实盘。生产前后完整只读基线及既有回收/恢复行逐值相等，活动工作 0、清理业务表 0；Root/HDD 75/83、独立恢复 Root 59、目录 Root 98、清理 Root 4 保持。宿主 857e018d/Agent1.58、四容器与运行 checkout 093f44a9，保护记录与 11 核心保持，没有覆盖发布或生产清理。

继续 Jobs 安全投影/完整结果摘要、准确 CI、标准部署及真实身份/来源选择/原件删除/HDD 保持整链，再适用故障矩阵、留存策略/归档恢复、维护连续性和其余 59 项。正式主项仍 0/59、阶段 0/6，工作量粗估 25%–35%，AI 子计划功能 0%；17 项移出不计完成。

证据目录 deploy/artifacts/container-recycle-purge-api-ui-cd5f7b8c854447729c42673a3799b599，59 文件/24 变更源码；EVIDENCE_SHA256.json 摘要 71812c35981c7b084455a8a1074b29fddf813c28c6a67a337e3d2cc416921757，所有条目重算通过。原失败、最终检查摘要、原生状态、三次生产快照、构建预览及冻结源码均保留于私有目录。
