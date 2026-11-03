# ACL恢复发布准备：CI失败修复和真实门禁候选

2026-11-03。本批修复实际CI暴露的历史读取身份检查缺口，补真实恢复主验/独立核验器及CI依赖、浏览器等待问题。**未覆盖发布、未迁移生产、未运行新恢复真实门禁。** 产品仍08b2bb8/1.52、Agent684268/MaintenanceHost684263，11核心保持原ID/镜像/StartedAt且healthy；源列不存在、17 CHECK、闲置预检全0，不能把脚本写完或临时目录测试记作真实恢复签收。

此前[管理员恢复API/Vue候选](CONTAINER_ACL_RESTORE_WEB.md)及[内部永久化候选](CONTAINER_ACL_RESTORE_PERSISTENCE.md)保持各自来源和失败记录，当前59项不提升整项完成率。

## 两次实际CI失败与修复

| CI | 实际结果与修正 |
| --- | --- |
| d183107，run41614833003，installer job125845555017 | `test_path_replacement_after_query_does_not_return_history`未抛出预期拒绝；本机曾通过的删除重建测试在CI暴露inode复用窗口。旧transport关掉原FD后只比dev/inode，不能可靠证明未替换。不是普通环境差异，可以返回已变路径的历史响应。 |
| 5423433，run41616271541 | 后端、真实PG、真实Valkey三路成功；installer继续走到Node测试才因无web/node_modules失败，frontend新联合81项为80通过/1登记丢响应用例失败。没有整条CI成功终态。 |

修复5423433：`whaledeck-container-acl-history-transport.py`把原文件FD保持到整个读取/namespace复核结束；对原FD和重新解析目标同时比较共享九字段指纹（含mode、UID/GID、link count、size、mtime/ctime）。inode在持有期间不能回收复用；同inode内容/mode/xattr变化也拒绝返回。所有成功/异常/第二次映射失败路径关闭原/新FD。仍是只读List/Read，未加写权限、RPC、minor、命令入口或任意路径；DAC_READ_SEARCH/SYS_PTRACE及原单读租约范围不变。

30项历史Linux-root专项（含新同inode变化、FD释放）、469项旧容器文件回归通过；实际文件在隔离临时目录，ContainerRoot/映射/HDD为明确夹具，不是安装态新签名RPC或HDD验收。原失败回执保留，不把后续Node失败误写为身份修复未生效。

修复cad6182：安装检查保留Node语法检查，把会导入Playwright的`test_acl_history_request_binding.mjs`执行放到frontend的固定依赖安装之后，仍执行原两项真实100/50响应判定测试，不伪造依赖或删测试。旧安装job在未安装前端包时引入helper顶层Playwright依赖是流程缺陷。

登记丢响应E2E此前等待泛化`role=alert`，误中原先就有的历史来源警告；网络请求尚未证明被夹具接收，测试便可能刷新。改为等待准确`.container-acl__error`，再证明夹具已接收规范原key/登记，然后重新加载查询。保持未确认时不自动重发产品语义，不延长超时或自动重试测试掩盖失败。生产构建预览同一用例5次连续通过，证明的是候选测试接口与等待边界，非真实Auth/磁盘。

## 新真实恢复门禁（尚未执行）

工具2fb2a40新增`validate_container_acl_restore_browser.py/.mjs`、`verify_container_acl_restore_browser.py`及`acl_restore_browser_proof.py`。cad6182额外严格区分普通/恢复意图，禁止同时调用只读和恢复验收模式；冻结来源继承原可信fixture、登录、原件、精确清理流程。所有实际写入均需显式运行新门禁，CI只编译/测试其证明逻辑，绝不在CI或导入时操作工作站身份/文件。

使用全新独占容器、确定完整内容/元数据和新的临时身份，通过原9个真实ACL场景生成8份本轮捕获，再从真实Vue选择其中准确capture/digest。新增6场景：恢复含named UID0/权限0的access、恢复基本access、无变化恢复、恢复目录default、明确清空default、预检后取消。预期仅5个新执行及5份写前当前ACL原件；不能复用所选捕获ID或旧执行ID。

每场景父进程在放行前验证实际全部文件字节、inode/UID/GID/mtime/xattr、非目标ACL和现有子项；恢复目标逐条等于选定旧原件，当前新备份计划逐条等于恢复前现场权限。SQL规范来源全文、原请求/计划/执行/回执、新Job安全摘要、23条专用写入审计及新Root/HDD摘要分别绑定；源码不读取能力密钥重签或篡改SQL/cache。旧捕获和所有已生成新捕获逐字不变，恢复来源hash不能拿旧历史digest替代。

真实页面检查包含新预检/明确确认、同key/确认幂等重试、刷新只查原引用、320×600暗色及1440亮色正文滚动/完整确认取消、默认空ACL/NoChange、正常原上下文双端退出；普通原上下文6路403、3路退出后401和UI零请求。独立核验器在准确清理后重读真实SQL/Root/attested HDD/来源/退出证明，明确不声称重读已删除文件内容；现场全字节由父进程逐轮核验。

复用原9个来源生成场景的Job安全摘要API核验，本模式不重复18个旧任务抽屉/旧空摘要翻页，以免把既有任务中心验收与本批恢复混算；此前真实任务抽屉记录保留。签收数量分原9场景/8捕获与恢复6场景/5新捕获，不能累加成59项完成数。

29项离线证明测试（19既有+10新增，内含多组反例）通过：替换来源/额外和重复字段/显式0类型、旧capture复作意图或执行、伪造NoChange、当前备份误用旧digest、审计缺失/重复/跨主体/跨Job/原文泄露均拒绝；另2项请求竞争、8项范围及Python/Node语法/CI YAML引用检查通过。它们是合成证明输入，不是新门禁实际通过。

## 发布与验收仍待

取得修复候选准确CI五路成功终态后，正常标准覆盖，核对六组件/实际宿主映射来源、受保护配置、可信发布账本和生产迁移。再冻结新验收器，对新独占资源执行主门禁和独立核验。当前没有发布unit、真实恢复意图或恢复Root/HDD操作，不重复之前已完成的只读/常规操作。

历史查询与恢复完整故障矩阵、递归ACL、可恢复删除/长期清理、维护安装态故障、其余59项/UI/C#酒馆/统一供应商/AI Live2D仍待。17项移出不算完成；user NOPASSWD按授权保留，Agent权限不扩大，凭据不入源码/日志，不自动回滚dev或重启主机。

## 本机完整安装门禁与收尾边界

从cad6182的CI YAML提取原样`Check shell syntax and side-effect-free preview`脚本，作为当前开发用户用`bash -e`实际运行（其中规定的隔离root测试仍由sudo执行），最终退出码0。日志54组unittest共1533项、安装CLI5项均通过，无跳过绿灯或删除失败测试；仅临时目录/夹具，不是生产安装、HDD恢复或断电演练。包含本批30历史/469文件/29证明/8范围等，不能再把这些子计数累加进1533形成虚增总量。

生产构建预览丢登记响应单例5/5通过、依赖安装后的请求判定2/2通过；未重跑全部81页面并冒称单轮成功。Python/Node语法、CI YAML依赖顺序与路径检查通过。目标第1节与此前3份QA全文保持，828条相关MD相对链接有效；私有原件保存于`deploy/artifacts/acl-restore-release-preparation/`，只包含冻结来源、失败摘录和检查/运行边界，不存环境文件或凭据。新真实恢复脚本未调用，生产版本/源列/核心与宿主PID均保持前批实际状态。

最后一次远端核查：cad6182/run41619582863的installer、backend、native-postgres、native-valkey四路均completed/success；frontend仍in_progress，故未启动发布。与本机全量安装检查分列，不提前签整条CI成功。
