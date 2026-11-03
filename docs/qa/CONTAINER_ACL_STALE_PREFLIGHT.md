# ACL旧版本预检拒绝与摘要文案：源码候选，真实门禁待运行

2026-11-03。固定源码候选`caa9c64`。**本批完成实现和本地候选验证，没有部署，也没有执行新的真实旧版本故障场景。** 运行仍447070e/协议1.51；Agent3947643、MaintenanceHost3947551，11核心healthy/所有活动计数0的只读复核通过。本批没有安装、EF/schema/依赖变更、服务重启、原生源文件变更、Root/HDD写入、回滚或sudo策略/密码修改。59主任务状态和现有25%–35%工作量粗估不因这些测试提高。

## 已复现并修正的问题

新增组件回归先在原代码明确失败：原planning/Failed、CONTAINER_ACL_VERSION_CONFLICT且未取得计划/回执时，摘要标题仍为“执行失败”。原实现11项通过/1项失败及完整断言保留。现在按已验证摘要的阶段区分：planning/Failed显示“预检失败，未进入 ACL 修改执行”，execution/Failed仍为“执行失败”；null/false/真实Completed/Aborted/Unknown等历史字段不被文案改写，未知或坏摘要继续失败关闭。版本冲突提示改为“对象版本已变化，请重新读取后预检”，不假定变化发生在预检之后。

生产预览增加真实形状的planning/Failed无计划无回执、execution/Failed/Aborted与Superseded/Completed绑定无效三分支，保留既有成功/Ready/Unknown/确认处置/旧任务空态。320/1440、明暗、600px高度四组合逐项核标题、三态、全部字段边界及无管理写请求，预检未证明字段完整入视口；不把Superseded说成未发生ACL修改。

## 新真实门禁已经实现，但本批没有执行

独立`validate_container_acl_stale.py/.mjs`及`verify_container_acl_stale.py`不改既有九场景或旧proofVersion。冻结干净Git来源与安装模块字节、实际产品来源/宿主及空闲后，使用新随机独占容器/源/内容/身份；非核心、非root1000:1000、无网络、只读根、准确HDD bind。正常可见Authentik→Vue先打开ACL对话框持有原版本，私有信号后主进程重新验证准确容器/旧完整清单/文件FD，**只将该自有文件mtime增加17ns**，不改全局时钟、SQL、缓存、Root、HDD或产品helper。

父进程核对完整2097159字节、原inode/mode/UID/GID/nlink/大小、两类ACL及固定普通xattr不变，除目标mtime和ctime外其余树保持；再允许原页面提交，强断言POST仍使用初始版本。预期实际Worker规划经已安装签名Agent/固定helper返回CONTAINER_ACL_VERSION_CONFLICT，原意图与规划Job Failed，无计划/确认/执行ID/Root开始时间/回执/发布版本/历史事实；不能将null写成aclConfirmed=false。SQL/API的15字段摘要严格同值同类型，原key重试只能返回原Failed意图。

正常任务中心320暗色与1440亮色的同一原Job详情必须显示新标题及未证明/未取得、无通用写按钮，并正常原上下文双端退出；5实际PNG才可签新门禁。另实际普通用户读/登记403、匿名读401及正常退出，原SQL不得变化。Root/HDD按意图ID及.NET准确字节顺序导出的执行ID检查不存在，不能以随机无关ID冒充。独立进程重新只读核对冻结来源、原SQL/摘要、关联历史缺席、真实页面/退出、准确清理、宿主与11核心；清理后不声称重读已删除源内容。

只在真实终态和完整已证明清单匹配时清理本案源；不明/未证明时保留数据。停止范围仅QA浏览器子进程，不杀产品helper、不重启宿主或产品服务，容器变化仅限准确自有fixture的创建/清理。原通用自有fixture仅在严格所有权核对后移除本案容器，证据永留。新脚本已加入CI离线语法及证明边界检查；它们的存在不代表实际门禁通过。

## 本批实际验证与已知失败

- 全部前端Unit：56文件、993项通过；新增组件负回归证明原文案错误，修复后通过。类型检查/生产构建、oxlint/eslint与格式通过。
- 冻结候选生产预览：4组ACL摘要＋4组本地HTTP EventSource续传共8项通过；使用测试身份/接口，不能证明Authentik、真实数据库、Agent、实际ACL或HDD。28预览PNG中新增失败/Superseded为12张，其他16张是旧摘要回归。
- 8新增证明边界＋11既有浏览器证明＋22 Unknown证明＋8范围＋40 Linux-root故障范围，共89项通过。临时文件FD测试证明只改准确mtime；错误容器、漂移ACL/非目标、虚假执行/计划/回执、错误主体/版本/摘要及模拟Root存在/临时目录中的关联HDD元数据均拒绝。固定执行ID向量对应前批真实.NET操作，不访问前批凭据。
- 首次`corepack pnpm`因本机缓存入口缺少bin/pnpm.cjs而MODULE_NOT_FOUND；使用现有用户固定pnpm12.10.1 native入口继续，没有安装/切换版本或修改全局配置。直接已安装Vitest复现原文案失败后，正常固定pnpm各门禁通过。首次e2e格式检查失败，经既定Prettier修正后通过。
- 新证明测试首次因直接导入unittest类额外发现11旧测试，共19执行；已改模块引用，最终新增只记8，89总数不重复计数。没有重新跑后端全量/.NET原生PG，不宣称新整条CI全绿。

本批预览/离线证据保存在忽略目录`deploy/artifacts/ui-candidates/acl-job-summary/acl-preflight-failed-caa9c64/`，包含候选/运行界限、原失败与修复日志、源码/构建和截图SHA索引。它不是部署或真实验收原件目录。

## 下一步与剩余范围

先正常冻结覆盖候选，核对六组件实际来源/安装helper/进程与数据库保护，再使用上述**全新**资源执行真实旧版本预检拒绝门禁和独立复核；不重放已签收的[同CID实例变化](CONTAINER_ACL_INSTANCE_CHANGE.md)或[目标消失](CONTAINER_ACL_POST_UNKNOWN.md)。新门禁目前未执行，Root/HDD为0只是预期断言，不是已取得实机结果。

无回执Unknown/不可信绑定/Superseded等真实摘要、其他共享写入/磁盘/路径组合、ACL递归/历史恢复、可恢复删除/长期清理、P0-05及其余59项/UI/C#酒馆/供应商/AI Live2D继续。生产预览中出现Superseded不表示真实该分支已验收。

文档复核：8份变动MD的778条相对链接无缺失，目标第1节与caa9c64逐字一致，前批实例变化QA原正文逐字保留。候选证据40文件SHA及28预览PNG保存，元数据明确candidateDeployed=false/newNativeStaleGateExecuted=false。项目图谱事件`whale-deck.container-acl.caa9c64.stale-preflight-candidate`已核对projectId/key/eventId回执并移入synced，阶段仍active。
