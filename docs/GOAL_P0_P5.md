# Whale Deck 完整目标与 P0–P5 实施基线

当前修订：2026-11-04。以下正文和59项主表为当前交付目标，酒馆/供应商/Live2D及UI追加均已纳入；文档更新不代表功能完成。历史运行/测试记录移至附录，原始事实保留。

## 1. 可复制的新目标正文

完成Whale Deck单工作站实用版P0–P5全部59项保留主任务及其完整子功能，交付前后端、.NET10/C#宿主Agent/Worker/MaintenanceHost、Vue3/Element Plus、实际依赖配置、安装升级、自动化测试与真实工作站部署验收，最终交付可部署master。对标1Panel单机操作体验与Plesk实用网站/证书/运行环境/备份能力。17项已移出主任务不恢复、不计完成，稳定ID不重排；不建设多节点、企业租户、套餐计费、邮件/DNS托管、虚拟机或通用平台插件市场。

完整实现容器/镜像/网络/卷/Compose/应用，主机与容器终端和文件管理（包含权限故障矩阵、所有者、递归权限、ACL、可恢复删除与长期清理），六类数据库及隔离恢复、NVMe运行/HDD备份、本机防火墙/网络安全写入、门户/通知/GitLab、实用GPU工作负载、任务监控与审计。完成P3-13–P3-18多语言开发空间、Git/上传导入、浏览器/本地IDE远程编辑调试测试、私有预览、诊断报告、资源预算/隔离与生命周期；开发测试不自动发布或替换生产应用。

按固定酒馆功能清单完整C#重构服务端权威编排，Vue原生集成全部聊天与配置；272个ST条目（206核心/随包能力及66目录扩展）、6个PET父级及16个供应商/Live2D子门槛逐项实现与验收，14个随包模块不重复计数。涵盖角色/Persona、消息/群聊/分支、世界书、提示词/模板/分词/采样、宏/正则/STscript、RAG/总结/翻译、媒体/语音/立绘及酒馆范围内扩展迁移/隔离兼容；不能用Node反代、仅文本连通、目录或安装按钮代替。66扩展逐固定版本/源码/许可/依赖核实，保留必要差异与证据。Authentik替代酒馆原生人类密码体系。

看板娘所有对话、碎碎念、梦话、媒体及动作决策等AI功能统一接入Whale Deck酒馆，由参考Cindy配置机制的集中供应商/模型/能力/用途绑定提供能力，凭据服务端安全保存，不直接读取Cindy实际账户或密钥。保留首批Codex CLI待验证适配方向，按实际能力接入授权API/本地模型及媒体服务。看板娘对话页必须使用真正Live2D；AI通过受控动作目录和结构化指令选择动作/表情，经C#授权/范围/版本/预算校验后由Vue播放。完成动画、交互与物理、碎碎念与对话、真实余额/额度、三段睡眠、真实语音口型、停止/重连去重/用户优先级/减少动画/睡眠与晚到指令恢复；视频或静图回退不算正常Live2D验收。前端同步按用户明暗紧凑指标卡/变更摘要参考实施。

保持Authentik唯一身份源、BFF/CSRF/Origin与资源授权、核心保护、内部服务隔离、最小授权Agent、稳定维护入口和USB/动态网卡支持；HTTP/IP可用，合法域名/HTTPS可配置。不得以模拟数据、空接口、按钮、原型或有限测试替代完整功能。按阶段直接实现，每项须具权限/安全/失败恢复/实际依赖/持久化与适用UI/API/Agent/Worker证据，保留失败及修复记录，缺外部依赖不得记通过。开发期user NOPASSWD:ALL按授权保留，之后由用户自行撤销并改密码，不把自动撤销作为完成条件；凭据不入仓库或日志。持续开发、测试、修复和覆盖dev发布，不自动回滚dev；用户手动恢复、备份恢复与网络失联保护仍保留。

实施及验收以本文59项主表、[单工作站范围](SCOPE_SINGLE_WORKSTATION.md)、[酒馆完整方案](AI_TAVERN_MASCOT.md)、[逐项账本](AI_TAVERN_ACCEPTANCE.md)、[统一供应商与Live2D](AI_PROVIDERS_LIVE2D.md)和[视觉规范](UI_STYLE_GUIDE.md)为准。新增范围是工作量扩展，文档登记不算功能完成；旧70/76项及HOLD只作历史。

## 2. 已确认决定与边界

- 当前59项全部实施；17项移出清单见范围决策，不再因缺少第二主机、邮件托管、企业租户或权威DNS阻塞交付。文件/终端/恢复/本机网络/通知/GitLab/可选域名HTTPS仍是保留需求。
- 用户决定不增加独立 WebUI 原型阶段。按任务直接实现前后端、测试与问题修复；不把原型确认作为阻断开发的门禁。
- 开发空间与开发环境纳入完整实施：一个空间可组合多个语言/SDK/服务版本，支持 Git 和上传导入、浏览器 IDE/受控本地 IDE 接入、远程调试/测试、自动收集有界脱敏信息与可下载报告。仅作代码调试和测试，不直接发布或替换生产应用；详细页面、用例、RPC、权限、模型与验收见 [开发空间设计](DEVELOPMENT_WORKSPACES.md)。默认普通用户权限不变，开发权限显式授予。
- Plesk/1Panel 是功能与操作体验参照，不复制商业源码/商标，不把购买商业软件理解为第三方源码授权。
- 仅Precision-7920-Tower单工作站，不创建节点选择器、租户、托管套餐或订阅流程。
- 普通用户默认仅仪表盘与个人/公共门户；开发空间和AI用途按Subject/资源/模型显式授权。可聊天不等于可配置供应商或读取密钥，不自动提升容器/宿主管理权。
- Authentik是人类用户及组事实源；只引用Subject/稳定组ID。资源预算仍保留，不创建本地密码或重复用户资料；数据库等专用凭据不属于本地登录账号。
- 现有 dev HTTP/IP 入口继续可用；HTTPS/域名是新增可配置能力，不依赖修改公司路由器或每个客户端 hosts。DNS 服务存在不代表客户端已采用它；域名必须有合法委派或可控 DNS Provider。
- 双网卡/USB 网卡保持可达；网络与防火墙写入必须有连接保活/限时确认，不能把唯一 SSH/维护入口锁掉。
- 不自动填满生产 HDD、重启主机、断电、发真实群消息/邮件或修改企业 DNS 来取得测试证据。使用隔离资源验证，外部真实启用所需域名/账号/网络授权另行配置。
- dev 流程继续“开发 → 测试 → 修复 → 覆盖发布”；不自动回滚 dev 发布。备份恢复、网络失联保护与用户手动版本回退属于独立产品能力，不能据此删除。
- 首阶段一般自动更新仅允许 `autoupdate=true` 且非核心资源，需版本约束、窗口及预检；核心依赖只走专用维护流程。
- **sudo 状态：已按用户授权重新启用并保留 user NOPASSWD:ALL。** 固定文件 `/etc/sudoers.d/90-whaledeck-development-user` 为 root:root/0440，单文件与全量 sudoers 校验及 sudo -n true 通过。用户之后自行撤销并改密码。正式 Agent 的 sudo 规则未扩大，继续固定 helper/资源注册、短时 capability 与审计。
- 全阶段遵守数据库内部网络、API/Worker 不挂载 Docker Socket、日志轮转和无逐样本 Info 日志；长 Secret 使用主机私有权限文件或 Secret Provider，禁止真实凭据进入 Git/审计/示例。

## 3. 当前证据与暂停恢复点

2026-11-04 递归ACL Partial取消与不同管理员处置真实整链通过：冻结8e6e472，唯一Invocatione21c3f70ec344190aa84c8969ac6f163准确退出0。新资源真实首项ACL修改后取消，产品5.020秒结束helper，Partial/保护保持、五计数1/1/1/2/0；1完成/1中止/2未开始，Root与其余源保持，1聚合+1子HDD/3Root留存。另一真实管理员原owner404，先暂不处置ACK0且完整SQL/源/历史不变，再一次明确ACK→Acknowledged/执行Job Failed/保护释放；原执行/计数/Root/HDD字节保留，不回滚/续做。两正常双端退出/匿名401、5张320明暗真PNG、独立含唯一不同主体审计/准确清理通过。106工具/8范围通过；旧495文件/两宿主b880208保持，11核心healthy，18意图8S/5C/1E/3F/1A、执行16/Job34、活动0。产品仍1.53不发布；其他Partial/Unknown/跨写入故障、递归恢复/删除及59项继续，不升完整完成率。[实盘、处置及边界](qa/CONTAINER_RECURSIVE_ACL_PARTIAL_REAL_UI.md)。

2026-11-04 递归ACL Worker写前强杀新资源完整复验通过：冻结020e25f/已修记录器，唯一Invocation4c079324438a42e2a72900ff163b22b5准确退出0。真实身份/页面确认后准确强杀同Worker，结构化重启前后inspect/service及helper退出0.111秒已落盘；原租约自然等待86.457秒/41样本，原执行Failed/INTERRUPTED、Root Aborted、五计数0/0/0/4/0，全树保持/1聚合HDD、原页面正常双端退出与独立核验/准确清理通过。旧c78原总失败及缺失回执不补签，不重放旧任务。493旧文件及两宿主PID/来源保持，11核心healthy，17意图8S/5C/1E/3F、执行15/Job32、活动0，产品仍b880208/1.53。85c2228新增首项ACL已完成/其余原样的Partial观察器，仅5项离线候选、未运行实机cut或业务页面整链；102工具/8范围通过。继续Partial/写后/Unknown等故障、递归恢复/删除及59项，不升完整完成率。[新门禁、原失败与候选边界](qa/CONTAINER_RECURSIVE_ACL_WORKER_RECHECK.md)。

2026-11-04 递归ACL进程中断分项：b510cfb的Worker原租约自然等待86.610秒/41样本、原执行Failed/INTERRUPTED、Root Aborted和全树保持/页面退出原件已只读补核；最终记录器状态变量被集合覆盖而TypeError，原总门禁退出1保留，精确耗时/重启前inspect未保存，不补签或重发强杀。00ec3f7修复后新资源仅Agent重启完整实机及独立通过，Invocation1cc4eb470b764d01a7315018c31a2819准确退出0；Agent3598741→3795144、维护3598736保持，helper退出0.054秒，5维护样本全200不推断瞬时零中断。Failed/Aborted、五计数0/0/0/4/0、全树保持/1聚合HDD，正常双端退出/准确清理通过。97工具/8范围通过；起始489及Agent前491文件保持，11核心healthy，16意图8S/5C/1E/2F、执行14/Job30、活动0，产品仍b880208/1.53未发布。Worker结构化缺口、写后/Partial/Unknown等故障、递归恢复/删除及59项继续，不升完整完成率。[实机、失败与边界](qa/CONTAINER_RECURSIVE_ACL_RESTARTS.md)。

2026-11-04 递归ACL写前实时移组/账号停用两项实机通过：工具冻结5f5fabb，产品仍b880208/1.53不重发；唯一Invocatione8543e8311be46df85e22d617183be28准确退出0并归档后停止QA unit。新身份原正权限仍余7161.060/7160.533秒，HDD已验证且整树原样；产品4.984/4.510秒结束准确helper，Canceled/Aborted、五计数分别0/0/1/3/0与0/0/0/4/0，全树字节九字段/两ACL/普通属性保持。原GET均403，移组me200非管理员→403页、停用me403→登录，引用清空且取消POST0，不冒称正常退出。2聚合HDD/0子历史、1首项Aborted Root如实保留，4真PNG/独立只读/旧门禁兼容及准确清理通过。91递归/12身份/8范围通过；旧484文件保持，11核心healthy/两宿主PID来源保持，14意图8S/5C/1E、执行12/Job26、活动0。继续重启租约/写后/Partial/Unknown/其余故障、递归恢复/删除和59项，不升完整完成率。[实机事实与边界](qa/CONTAINER_RECURSIVE_ACL_REVOCATION.md)。

2026-11-04 递归ACL执行中写前取消/原上下文正常退出两项实机通过：冻结54ee000，产品仍b880208/1.53不重发，唯一Invocation72018fbf83e045388903440cf8f183e5。两套新资源实际Authentik→Vue→确认Pending→同Worker启动，Root Applying/HDD已验证且首项未开始；产品4.925/5.022秒结束准确FD绑定helper，SQL Canceled/Root Aborted、五计数0/0/0/4/0，全树字节九字段/两ACL/普通属性保持，2聚合HDD/0子历史留存。两原上下文正常双端退出/匿名401、3真PNG、独立只读/准确清理及89工具/8范围通过。旧480文件全部保持，11核心healthy/两宿主PID来源保持；12意图8S/3C/1E、执行10/Job22、活动0。Type=exec unit成功后回收，以同Invocation管理日志证明Deactivated successfully，不以默认show补签原退出元数据。仍需写后/撤权/重启租约/Partial/Unknown与其余故障、递归恢复/删除及59项，不升完整完成率。[实机证据及边界](qa/CONTAINER_RECURSIVE_ACL_PREWRITE_FAULTS.md)。

2026-11-04 b880208引用保护已标准覆盖：准确CI41774447299共12路全绿，首次镜像/宿主预构建TLS失败原件保留，错开重试并核验4镜像/2宿主后唯一发布Invocation657c3c07df6841cea48d56e07b1c5705成功（171秒）。六组件/HTTP/实际exe-DLL同源，Agent3598741/维护3598736、11核心健康、8数据库身份/7配置/153与98旧HDD保持，账本current=b880208/previous=c020c24/pending空，90维护样本全200但不推断零中断。新资源只复验取消：原key/id重载前后保持、Canceled且无执行/Root/HDD、物理完全不变；原总脚本因403页无用户菜单而普通退出失败，已只读补核并精确清理，不重发取消。f8e9f5a两个全新认证上下文实际完成普通4GET/4POST403、各4匿名401、正常双端退出且原SQL不变；不补签原普通上下文或旧丢引用根因。83工具/8范围通过。生产共10意图（8S/1E/1C）、执行仍8、活动0；仍未单批八场景全绿，继续完整故障/递归恢复/删除和其余59项。[覆盖、真实取消和认证补验及边界](qa/CONTAINER_RECURSIVE_ACL_AUTH_RELEASE.md)。

2026-11-04 递归ACL真实页面分项已保存：产品仍c020c24，不重发；default/access/noop/mask/ID0/default-noop/清除七类实际文件与SQL/Root/HDD已核对，但三个原总门禁均失败。前两次是manifest投影和mask预期工具错误，补核原件而未重发；第三次Ready重载丢原入口，未取消/未执行，实际自然Expired。共9意图（8Succeeded/1无执行Expired）、17Job、9/8规划确认审计、8聚合32子HDD留存；三套源/六身份准确清理，11核心/宿主/配置与旧153/58HDD、145/36Root原字节九字段保持，递归HDD现98、活动0。b880208修复已测试复现的首次身份暂不可用清opaque引用/403分类问题，1170前端/41生产预览配置浏览器（均测试接口）及82工具/8范围通过，未部署且不宣称它就是实机根因。取消/普通匿名矩阵/原上下文正常退出与完整故障/递归恢复/删除仍待，不签完整整链或提高59项完成率。[真实分项、三次失败与修复候选](qa/CONTAINER_RECURSIVE_ACL_REAL_UI_PARTIAL.md)。

2026-11-04 递归ACL业务/API/Vue的c020c24已标准覆盖：准确CI41762751191共12路全绿，唯一发布Invocation5739248fe78b4d26a20b2e59dd918081成功，六组件/HTTP及两宿主实际exe/DLL映射同源；Agent3295429/维护3295422。生产迁移20261103235841、18CHECK/11写入屏障触发器/1不可变保护已实查，新意图0/活动0；安装validator与动态入口通过。11核心healthy、8数据库容器完整身份保持、7配置及153/58旧HDD字节九字段保持，账本current=c020c24/previous=28062da/pending空。完整发布窗口157次维护采样全200，但不推断瞬时零中断；两匿名GET401不冒充真人页面。尚未执行新真实Authentik→Vue预检确认→实际文件/HDD整链；下一批全新资源8常规场景、原引用/退出/权限与独立精确清理，再故障/递归恢复/删除和其余59项，不提升整项完成率。[标准发布、生产迁移与未验边界](qa/CONTAINER_RECURSIVE_ACL_RELEASE.md)。

2026-11-04 递归ACL管理员API与Vue候选c020c24已提交推送：严格显式两目标/ID0/mask/null与default清除、原登录幂等预检/确认/取消及当前管理员异常处置；页面完整目标、根/执行身份、五计数和历史/观察分列，原引用恢复、退出清理与小屏滚动齐接。通用任务重试明确拒绝，两类待对账阶段不提供通用取消。4651后端单元、569集成（另7默认原生跳过）、1161前端单元及类型/构建/lint/格式通过；30新开发页面及170生产预览配置浏览器（30新+140关联）通过。真实回环身份/仓储/Agent及浏览器API均为夹具，不当作已上线或真人磁盘整链。安装仍7cb3935/1.53、运行应用/账本28062da；11核心/两宿主PID、7配置账本和全部153/58HDD与前批快照保持。尚需准确CI、标准覆盖与真实身份/文件/HDD，再完整故障/递归恢复/删除和其余59项，整项完成率不升。[候选、失败与未验边界](qa/CONTAINER_RECURSIVE_ACL_API_UI.md)。

2026-11-04 递归ACL持久化候选224f57c通过真实隔离PG：新增真实仓储/EF迁移20261103235841、18CHECK及不可变历史/回执MAC、双Job/outbox/审计原子事务、租约与原Started；9类旧容器写入+宿主锁对称共享fence/路径预算，DI/Worker与取消路由已接。新类型专属原生阶段实际执行，1宏门禁无跳过通过、两库/SDK清理成功、外部投递0；合成Agent回执/身份/测试时钟不冒充真实Root或进程重启。4649Unit/491Integration（7默认原生跳过）、8范围/3状态工具及Release零警告错误通过，34源码SHA与三候选CLI来源核对。运行前后11核心/两宿主7cb3935、7配置账本、153/58HDD字节九字段保持、活动0，生产新表实际缺席。尚无生产迁移/新Worker运行/公开API/Vue/真人整链；下一批管理员API与页面再CI/标准覆盖和实盘签收，故障/递归恢复/删除及其余59项继续。[真实DB分项与失败/未验边界](qa/CONTAINER_RECURSIVE_ACL_PERSISTENCE.md)。

2026-11-04 递归ACL内部编排候选0168d5b/4b01148完成：独立意图/规划与执行Job绑定、四业务MAC域及确认、首次await前命令/续租前计划深快照、SameCommand及null/empty/零值、有界列表先验证；当前权威身份/取消/租约/期限、原ID先查后封存不重放、Partial/Unknown明确处置及独立ReceiptHash保留实际事实。最终Release0警告错误、149新增专项/4649Unit及490Integration通过（7默认原生跳过）、8范围；11源码SHA与候选CLI来源4b01148核对。显式内存仓储/Gateway夹具，尚无真实Repo/迁移/共享路径保护/Worker/公开API/Vue或真人整链；不算永久业务已上线。只读前批安装基线保持：两宿主7cb3935/1.53、四容器/账本28062da、11核心healthy、7配置账本和旧145/23元数据保持、总153/58、活动0。上轮c05bae1已远端同步；下一批真实PG持久化/全写入屏障再页面整链，维护连续性/递归恢复/删除及其余59项继续。[代码、失败及准确边界](qa/CONTAINER_RECURSIVE_ACL_BUSINESS.md)。

2026-11-04 递归ACL安装态分项通过：7cb3935准确CI12路成功后标准构建/安装，两宿主实际7cb3935、minor1.53，四产品容器/账本仍28062da。31c2249冻结探针与独立验收源码，新独占实盘8场景7完成/1写前取消、18规划/8操作拒绝、7聚合/28子项HDD与43响应逐项核验；完整字节/inode/owner/mtime/普通属性/非目标ACL及迟到不重写、准确清理通过。旧单项ACL9场景8完成/1取消及独立兼容核验通过，普通HDD总153/原145保持，递归总13聚合45子项（含前批6/17）。73递归探针检查/8范围、Release零警告错误通过。11核心未重建且healthy、7配置与账本保持、NOPASSWD保留；维护安装会重启两宿主，无全窗口零中断签收。尚无永久业务/Worker/API/Vue/真人身份整链，Partial/Unknown等安装态故障、递归恢复/删除和其余59项继续。[实机、版本和未验边界](qa/CONTAINER_RECURSIVE_ACL_AGENT.md)。

2026-11-04 递归ACL专用协议/SDK/受限传输候选ae3cab5完成：新增四unary RPC、独立全消息/方法签名与单次短时能力、minor1.53/专属cap协商；wrapper保留default文件null/目录显式空、ID/权限presence，原计划/aclType/四状态计数严格验证。最终confined child/root逐项namespace/FD非IDMAP检查、固定stdin/unit/准确Invocation stop与仅4cap/3写目录、安装验证接入，无CHOWN/SYS_ADMIN。Release0警告错误、4500Unit/490Integration（7默认原生跳过）、506Linux-root零跳过、8范围/5CLI及语法通过；CA2208/4个旧argv预期/CS8602夹具失败修正保留。干净冻结重建CLI来源ae3核对，但未安装/签名实机/永久业务/API/Vue，运行仍28062da/1.52，11核心/两宿主/7配置账本/145旧普通ACL保持、活动0。下一批原生探针/标准安装与真实RPC-HDD兼容，再全业务与其余59项。[实现与未验边界](qa/CONTAINER_RECURSIVE_ACL_RPC.md)。

2026-11-04 递归ACL内部内核候选b62d099实盘通过：新增trusted ACL配置/独立域，不扩大旧chmod-chown输入或授权；access分文件/目录完整ACL，default仅目录、文件noChange验证。489 Linux-root含20新专项/0跳过、8范围/语法通过，首轮inode替换夹具ENODATA及修正保留。冻结56验证源码/11宿主模块，新独占真实容器/HDD六操作（4完成/1Partial/1Aborted）、两次候选SIGKILL、6聚合/17子项原件、完整字节/inode属主mtime属性/非目标ACL与迟到不续做、独立核验及准确清理通过。旧145普通ACL元数据九字段字节、11核心/两宿主/7配置账本和索引保持，运行仍28062da/1.52。尚未安装/RPC/永久业务/Worker/API/Vue或真人页面，不算异盘或完整递归ACL；后续专用协议SDK/受限传输与其余59项继续。[候选实际证据与边界](qa/CONTAINER_RECURSIVE_ACL_ENGINE.md)。

2026-11-04 异步预检前有效来源替换实机分项通过：bd72c45冻结102源码/13安装helper，新资源真实选源后只停原Worker，正常登记202/Queued后发布同capture独立inode时间+1/自洽digest替换；旧摘要409/新200、原key/完整Queued SQL保持。同Worker启动后原规划Failed/SOURCE_CHANGED，原来源/命令/期限不变，无计划/确认/执行/Root及当前恢复HDD；原件还原后仅查询原失败。0d44e15追加POST与SQL目标版本直接比较/1专项，冻结102源码仅独立只读通过，不重放。5真PNG/两主题、2审计、正常双端退出/HTTP拒绝及准确清理，旧144HDD字节九字段保持/总145、新原件ctime改变保留；11核心healthy/仅Worker StartedAt变、两宿主/7配置账本保持，最终163检查0跳过。四缺失/四同capture时间摘要替换各有证据，其它内容/依赖/期限/回执、递归ACL/删除和维护连续性及其余59项继续。[实机与版本边界](qa/CONTAINER_ACL_SOURCE_PLAN_REPLACED.md)。

2026-11-04 首次登记前有效来源替换实机分项通过：c0245ed冻结97源码/13安装helper，新资源真实选源后发布同capture独立inode的时间+1/自洽digest替换，旧摘要409/新摘要200；原POST仍原摘要/当前目标版本，登记409/同key及两个预期Job404，SQL意图/Job/outbox/事件/恢复审计五项0、Root/HDD缺席。原件还原后只查询仍无登记，3真PNG/两主题、正常双端退出/HTTP拒绝、独立只读与准确清理通过，旧143HDD字节九字段保持/总144、新原件ctime改变保留。11核心/两宿主/7配置账本与root只读Git索引保持，无发布启停；9202339另补异常请求查账与未写成日志stage保留告警，仅离线4新项，最终154检查0跳过，不重放。异步预检替换/依赖/期限/回执、递归ACL/删除和维护连续性及其余59项继续。[实机与工具修复边界](qa/CONTAINER_ACL_SOURCE_REGISTER_REPLACED.md)。

2026-11-04 首次Apply前有效来源替换实机分项通过：fd23d93冻结92源码/13安装helper，新资源Ready后仅停原Worker，确认202/Pending后发布同capture独立inode的时间+1/自洽digest替换；旧摘要409/新摘要200、原key及Pending SQL保持。同Worker启动后原执行Failed/SOURCE_CHANGED、Root Aborted/false、RootStarted空/无当前前像；原件还原后迟到原ID Root Apply仍Aborted。5真PNG/两主题任务详情、4审计、正常双端退出/HTTP拒绝、独立只读及准确清理通过，旧142HDD字节九字段保持/总143、新原件ctime改变保留。11核心healthy/同ID镜像保持、仅Worker StartedAt变，两宿主/7配置账本与root只读Git索引保持；142离线检查0跳过，无发布宿主重启。其它替换/依赖/期限/回执、递归ACL/删除、维护连续性及59项继续。[实机与验收边界](qa/CONTAINER_ACL_SOURCE_APPLY_REPLACED.md)。

2026-11-04 Ready后有效来源替换实机分项通过：6c5b331冻结87源码/13安装helper，新资源仅对新capture的另一个inode改时间+1/自洽digest，新摘要实际200、旧摘要409/AGENT_VERSION_CONFLICT、原确认409/原Ready SQL不变、执行0。原件精确还原后明确取消/正常退出，2真PNG/3审计/HTTP拒绝、旧141HDD字节九字段保持/总142及准确清理通过。独立首轮旧数据前缀错误失败，06f179b修正后冻结87源码仅只读复核通过，不重放、不称整批首轮通过；临时替换先归档再清理，新原件ctime变化保留。六组件28062da/两宿主/全部11核心/7配置账本保持、活动0，无发布启停；16+22+4+32+28+8离线通过。其它替换、依赖/期限/回执、递归ACL/删除及维护连续性和其余59项继续。[实机及修正边界](qa/CONTAINER_ACL_SOURCE_CONFIRM_REPLACED.md)。

2026-11-04 来源首次登记缺失分项实机通过：1537d6a冻结79源码闭包/13安装helper，新资源真实选源后仅隔离新原件，历史404、登记409/OPERATION_CONFLICT、同key/预期Job404，SQL意图/Job/outbox/事件/恢复审计五项0，Root/HDD缺席。原inode字节还原后仅查询仍无登记；3真PNG/两主题未找到、正常双端退出/引用清空、HTTP权限、实盘及独立核验/准确清理通过。派生ID不冒充实际任务，旧140HDD字节九字段保持/总141，新捕获ctime改变保留；六组件28062da/两宿主/全部11核心标识/7配置/账本保持、活动0，无服务启停发布。8+8+11+5+22+28+8离线通过；四个缺失来源边界分项已实机，替换/依赖中断/期限回执、递归ACL/删除及维护连续性和其余59项继续。[实机事实与边界](qa/CONTAINER_ACL_SOURCE_REGISTER_MISSING.md)。

2026-11-04 来源异步预检缺失分项实机通过：4d38a51冻结74源码闭包/13安装helper，新资源真实选源后仅停原Worker，来源可读时登记202/Queued；隔离新原件取得404/原key200后再启动同Worker，原规划Failed/RESTORE_SOURCE_CHANGED，无计划/确认/执行/Root和当前前像。原inode字节还原后只查询原失败任务，5真PNG/两主题“未证明”摘要、2审计、正常双端退出/HTTP拒绝、完整实盘及独立核验/准确清理通过。旧139HDD字节九字段保持/总140，新捕获ctime变化保留；六组件28062da/两宿主/11核心/7配置账本保持、仅Worker StartedAt改变、活动0。8+11+22+28+5+8离线通过；首次登记拒绝/替换/依赖中断、递归ACL/删除及维护连续性和其余59项继续。[实机事实与边界](qa/CONTAINER_ACL_SOURCE_PLAN_MISSING.md)。

2026-11-04 来源首次Apply缺失分项实机通过：bbd1b52运行前冻结69源码闭包/13安装helper，新资源真实捕获/选源/Ready后仅停原Worker，原确认202→Pending；隔离新原件取得404/原key200后再启动同Worker，原执行Failed/RESTORE_SOURCE_CHANGED、Root Aborted/false、RootStarted空/无当前前像。原inode字节还原后迟到同ID Root Apply仍Aborted；5真PNG/两主题执行摘要、4审计、正常双端退出/HTTP拒绝、实盘及独立核验/准确清理通过。旧138HDD字节九字段保持/总139，新捕获ctime改变保留；六组件28062da/两宿主/11核心/7配置账本保持，只有Worker StartedAt改变、活动0。11+5+22+28+8离线通过；登记/预检/替换/依赖中断、递归ACL/删除及维护连续性和其余59项继续。[实机事实与边界](qa/CONTAINER_ACL_SOURCE_APPLY_MISSING.md)。

2026-11-04 来源确认缺失分项实机通过：c3dca1f冻结42入口文件/13安装helper，新资源正常Authentik→Vue捕获/选源/Ready，仅隔离新历史原件；真实历史404、原确认409/OPERATION_CONFLICT、原key200及完整SQL保持、执行/新Root/HDD为0。原inode/字节还原后真实读取200，原页面明确取消且正常双端退出；2个真实320暗色PNG、3审计、普通HTTP403/匿名401、独立核验及准确清理通过。137旧元数据字节九字段不变/总138，新捕获ctime改变明确保留；六组件28062da/两宿主/11核心/7配置/账本保持、活动0，无发布重启。5+22+28+8离线通过；只签此确认缺失组合，登记/预检/Apply/替换/依赖中断、递归ACL/删除及维护连续性和其余59项继续。[实机事实与边界](qa/CONTAINER_ACL_SOURCE_CONFIRM_MISSING.md)。

2026-11-04 来源丢失验收工具候选：仅新普通捕获可隔离，绑定SQL/Root/HDD/捕获前清单、NOREPLACE/私有持久日志/目录inode及准确还原；22项隔离文件测试（含准确子进程SIGKILL后新进程恢复）通过，CI已接入但新终态待取。实际安装态仅只读预检，137旧元数据字节九字段、六组件28062da/两宿主/11核心/7配置/完整账本及活动0保持；未调用生产隔离、未新建身份/发布/重启，不补签来源故障。目录ctime及未证明状态保留边界、两项探针/测试失败与修正明确记录；真实页面各切点、替换/依赖中断、递归ACL/删除及维护连续性继续，59项不升完成。[候选与未验边界](qa/CONTAINER_ACL_SOURCE_QUARANTINE_CANDIDATE.md)。

2026-11-04 28062da唯一标准覆盖成功，准确CI五路/六组件/安装/18CHECK和账本核验通过；Agent2178169/维护2178164。新资源真实Authentik→Vue→Worker/Agent→文件HDD来源卡片、3次200/no-store及8事件、401/403/owner404、明确ack与目录4轮延期、11PNG/独立复核/准确清理通过；旧135HDD九字段字节保持、总137，11核心healthy/7配置账本保持/活动0。维护入口159样本有1次连接失败，P0-05全窗口连续未签；本地409预览全过仍非实机，后续6片CI亦12路成功/749执行通过，前端本次对比71分→16分58秒。来源故障/递归ACL/删除及59项继续。[真实签收与剩余边界](qa/CONTAINER_ACL_RECOVERY_SOURCE_REAL_UI.md)。

2026-11-04 准确产品28062da的CI41681017703五路全部completed/success，回执逐SHA/名称核对；六候选产物保持冻结，尚未启动发布。后续前端CI候选保留41文件和原检查，6路独立分片/单一frontend失败门禁，14离线检查及749项只收集无漏无重通过；实际分片尚未运行。本地原409末组额外回归进行中；来源期限2项新增、相关业务123通过，均不当实机。[发布接续](qa/CONTAINER_ACL_RECOVERY_SOURCE_UI.md)；[CI候选边界](qa/FRONTEND_CI_SHARDS.md)。

2026-11-04 28062da两宿主标准自包含预构建退出0，实际CLI来源/完整SHA清单及.NET/PKCS10.0.13通过，未安装；六候选产物分列，运行仍b69302b。来源故障矩阵已明确真实缺口，新增替换/暂不可用续做/原三态回执用例，相关业务121通过，均非真实HDD来源故障。11核心/两宿主/7配置/账本/135HDD保持、活动0，产品CI前端进入最后组仍待终态，未启动发布。[预构建与边界](qa/CONTAINER_ACL_RECOVERY_SOURCE_UI.md)；[来源故障矩阵](qa/CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)。

2026-11-04 来源UI实机前补强：新增实际网络8事件顺序，严格证明第二次摘要GET响应在ack请求前、第三次GET在ack成功响应后；128相关检查无跳过与语法通过。只读发布观察器9分类检查及真实200/Ready预检通过，但尚无发布窗口/来源UI实机签收。产品28062da CI四路成功/frontend布局组仍进行，运行仍b69302b，未启动发布unit。[候选、证据与未验边界](qa/CONTAINER_ACL_RECOVERY_SOURCE_UI.md)。

2026-11-04 恢复来源UI候选28062da四镜像标准预构建完成，实际ID/OCI/内部版本一致但未发布；运行五组件仍b69302b，11核心及宿主1772448/1688513、7配置/账本/135份HDD九字段字节保持、活动0。六组件总probe因旧镜像来源查询与候选Migrator阶段差异拒绝，未伪签一致。新增显式source-ui/proof7真实页面来源三次GET/卡片/确认/401-403门禁候选，126相关检查与语法通过，尚未实机；产品CI四路成功/frontend待。下一步准确CI成功后标准覆盖、新资源真实整链，59项不升完成。[准备、失败与边界](qa/CONTAINER_ACL_RECOVERY_SOURCE_UI.md)。

2026-11-04 管理员恢复来源摘要API/Vue候选已实现，尚未部署：新增只读restore-source白名单投影、实时管理员双检/原MAC校验，普通ACL明确JSON null；原owner恢复接口和普通Job/Intent不扩字段。Vue四并发有界加载，来源失败不当普通任务，确认前后/原处置查询逐字段比对；4368后端单元/490集成（7默认原生跳过）/1087前端单元/68生产预览页面及8范围检查通过，含明暗320×600和来源异常；不代表真实Authentik/文件HDD整链。运行只读六组件probe仍b69302b/1.52、Agent1772448/维护1688513；ACL递归/删除和59项继续。[实现、测试与未验边界](qa/CONTAINER_ACL_RECOVERY_SOURCE_UI.md)。

2026-11-04 恢复写后同CID实例变化Unknown与目录保护实机通过：b33ad69/profile8/proof6冻结51依赖、121检查后新资源真实选源/恢复，准确CID一次重启、镜像挂载保持/PID1886629→1890996和StartedAt改变，两命名空间完整清单保持；原正常退出组合由产品4.661秒停helper，Root Unknown/null确认/旧实例及来源绑定不改。不同管理员320暗色暂不处置再明确ack，目录版本2/4/6/8自然延期，00:35:14.582729处置后00:35:18.063014才Root写入；9真PNG/5恢复审计/正常退出/独立核验/准确清理通过。133旧元数据保持、总135/三失败树保留，11核心healthy/宿主1772448与1688513/7配置账本保持/活动0。来源专属UI与其它故障/递归ACL/删除仍待。[实际事实与范围](qa/CONTAINER_ACL_RESTORE_INSTANCE_CHANGE.md)。

2026-11-04 历史恢复写后目标消失Unknown及跨容器目录保护实机通过：f0226dd/profile7/proofVersion5冻结51依赖，118检查后全新真实选源/恢复写入，移除仅本轮容器并正常退出，产品4.811秒结束helper；不同管理员320暗色先暂不处置再明确ack，原执行Failed/Root Unknown/null确认及来源/原件/迟到不改，等待目录版本2/4/6/8自然延期，00:09:56.537648处置后00:09:59.930647才Root写入并Vue可见。9真PNG/5恢复审计、正常双端退出/独立核验/准确清理通过；131旧元数据字节九字段保持、总133，三失败树保留，11核心healthy/两宿主及7配置账本保持/活动0。仅此写后目标消失组合，实例/来源/管理员来源UI/递归ACL/删除及59项继续。[事实与未验范围](qa/CONTAINER_ACL_RESTORE_UNKNOWN.md)。

2026-11-03 ACL恢复写后Agent重启真实整链及双Job页面通过：首轮44a60de因来源Job回执缺失失败、仅普通捕获1/恢复0/未重启，源保留；96b6bda补真实API/SQL摘要和独立原目录读取，90检查通过后用全新资源实测。产品仍b69302b/1.52，helper0.010秒结束、Agent完整重启30.622秒，PID1688516→1772448/维护1688513保持；152次维护200/Ready、Succeeded/Completed/迟到不改、来源+当前HDD、双原Job真实320暗色/正常退出/独立复核及准确清理通过。128旧元数据字节九字段保持、总131/三旧失败树留存，11核心healthy/7配置账本保持/活动0。Unknown、递归ACL、删除及59项继续。[实机、失败与修正](qa/CONTAINER_ACL_RESTORE_POST_AGENT_RESTART.md)。

2026-11-03 b69302b历史读取超时修复已部署并完成真实写后撤权复验：准确CI五路成功、唯一Invocation c7f46555dedd4db2a84c2e8e8fa46071退出0，六组件/16helper同源，Agent1688516/维护1688513，11核心healthy/8数据库/7配置保持、账本current b693/previous cad/pending空。公共8080真实签名读取128份两页3.597/2.368秒原件不改；全新Vue写后移组/停用分别4.786/4.736秒停helper、Succeeded/Completed，原403/引用清空/取消0、当前管理员Recovery200/Owner404，独立复核及准确清理通过。两探测脚本失败、一次并行账号检查失败及只读修正保留，不重放写入。恢复写后Agent候选/Unknown/递归ACL/删除和59项继续。[发布、实机与失败边界](qa/CONTAINER_ACL_HISTORY_TIMEOUT_RELEASE.md)。

2026-11-03 ACL恢复写后Agent重启门禁候选已验证、未实机运行：新增profile6/schema12仅单场post-acl-agent-restart，绑定原恢复wrapper/来源与Succeeded审计，沿用准确切点、固定Agent重启、维护连续性、双原Job安全结果/正常退出/失败保留；旧profile及普通schema6兼容。25恢复证明+44Root范围+10浏览器证明+8范围共87通过及语法通过，均非新的实际重启或恢复整链。76cb825已推送、前批图谱重试成功；b69302b仍待frontend终态及标准覆盖，实际运行cad6182。[候选和未验边界](qa/CONTAINER_ACL_RESTORE_AGENT_RESTART_CANDIDATE.md)。

**2026-11-03 历史超时修复四镜像预构建完成、未发布**：标准构建退出0，API/Worker/Migrator/Gateway实际ID、OCI revision与内部版本均b69302b；11核心healthy、两宿主/7配置/账本不变、活动预检0，公共8080及实际运行仍cad6182。准确CI四路成功，frontend在最后页面组；待成功终态后标准覆盖，再真实8080完整撤权复验。[本批基线与边界](qa/CONTAINER_ACL_HISTORY_RELEASE_PREPARATION.md)。

**2026-11-03 历史读取维护代理超时已定位，b69302b修复候选未部署**：写后移组分项dc243实机Succeeded/Completed、原403/引用清空/当前管理员Recovery200而原Owner接口404通过；同批停用及新批ea30重读均在历史列表503前置失败，未登记恢复/撤权，两失败树保留，不签整批。实际同请求内部Gateway4.972秒200/124份、维护入口5.007秒503维护HTML，定位普通5秒代理预算。候选新增仅规范历史GET的35秒路由及保留5秒硬截止的3秒软分页，Release零警告错误/4363Unit/488Integration（7原生跳过）/32Root通过，真实HDD候选82/42两页3.085/1.604秒读完124份不改原件；不是安装态RPC或UI签收。CI四路成功/frontend待，生产仍cad6182/1.52、1399935/1214273、11核心healthy/8数据库/7配置及账本保持；须标准覆盖与8080整链复验，59项不升完成率。[失败、根因、候选与未验](qa/CONTAINER_ACL_HISTORY_TIMEOUT.md)。

**2026-11-03 ACL恢复写后取消/Worker回执中断实机通过**：产品仍cad6182/1.52不重发，工具3957cd7/schema10/version4两新资源在目标基本ACL已写/Root Applying时真实页面取消或准确原Worker SIGKILL。产品5.278/0.114秒结束helper，强杀原租约87.034秒/41采样后以原执行ID对账；两原任务Succeeded/Completed、aclConfirmed=true及新版本保持，取消请求未冒充已撤回。2旧来源+2当前HDD/9恢复审计/8Job、完整已写清单及迟到原Apply不变，正确wrapper/原上下文退出/独立核验/准确清理/4真PNG及80检查通过；旧schema9只读92原件保持。11核心healthy/8数据库/两宿主/7配置保持，395CI四成功/frontend待；写后撤权/Agent、Unknown/来源、递归ACL/删除及59项继续。[证据与边界](qa/CONTAINER_ACL_RESTORE_POST_CHANGE.md)。

**2026-11-03 ACL恢复写入前Worker强杀/Agent重启实机通过**：产品仍cad6182/1.52不重发，工具12c789c/schema9/version3两新资源真实捕获后恢复至当前ACL原件HDD已验证/目标未写切点。Worker实际SIGKILL后产品0.109秒停helper，原租约86.846秒/41采样保留token/expiry/来源及权威hash；Agent重启0.012秒停helper，PID1214295→1399935、维护1214273保持，重启窗口153次200/healthy-Ready。两原任务Failed/Interrupted、Root Aborted，全源/两类ACL/2旧来源+2当前HDD/8恢复审计/8Job保持，正确恢复wrapper、原上下文退出/独立核验/准确清理/4真PNG及77检查通过；旧schema8只读兼容87原件保持。11核心healthy/8数据库/7配置保持，12cCI四成功/frontend待；写后/Unknown/来源等故障、递归ACL/删除与59项继续。[实机证据与边界](qa/CONTAINER_ACL_RESTORE_RESTARTS.md)。

**2026-11-03 ACL恢复实时移组/账号停用实机通过**：产品仍cad6182/1.52不重发，工具e77c1a6/schema8/version2在两套新资源真实页面先留自有捕获再恢复，当前ACL原件HDD已验证且未写目标时仅撤销准确临时身份。旧正权限仍余7138.461/7137.025秒，产品4.938/4.858秒结束helper，Canceled/Aborted、完整源/两类ACL保持；原恢复GET均403，移组me200非管理员→forbidden、停用me403→login，引用null/取消POST0，不冒称正常退出。2旧来源+2当前HDD/8恢复审计/8Job、独立Passed/准确清理/4真PNG及73检查/语法通过；旧schema7只读兼容与81份原件保持。11核心healthy/8数据库/两宿主/7配置保持，e77CI四路成功/frontend待；其余恢复故障/递归ACL/删除与59项继续。[实际证据](qa/CONTAINER_ACL_RESTORE_REVOCATION.md)。

**2026-11-03 ACL恢复写入前取消/正常退出实机严格复验通过**：产品仍cad6182/1.52，无新发布；工具7990b84/40份来源在两套新资源先真实页面产生自有旧ACL捕获，再经历史恢复确认到Applying/Root Preparing且当前ACL原件HDD已验证。取消/原上下文正常双端退出由产品5.434/5.575秒结束准确helper，原任务Canceled/Root Aborted、完整源/两类ACL保持；严格轮2旧来源+2新当前HDD、9恢复审计/8Job、独立Passed/准确清理/3真实PNG通过。初轮物理与独立已过但状态出截图范围的边界保留，新增滚动/视口断言后用新资源复验，未重放旧操作。68相关检查与语法通过，11核心healthy/8数据库/两宿主/7配置保持；799 CI四路成功/frontend待，剩余恢复故障/递归ACL/删除和59项继续。[实盘事实与边界](qa/CONTAINER_ACL_RESTORE_FAULTS.md)。

**2026-11-03 cad6182 ACL历史恢复已部署且真实常规整链通过**：准确候选CI五路成功，唯一标准update Invocation0efd3714f17b44209562fc73c12e8ffd成功；六组件同源cad6182/1.52，Agent1214295/维护1214273，生产来源迁移入库/18CHECK，currentcad/previous08/pending空。工具冻结2880f7b的新独占资源完成9基础场景及6恢复（5执行/1预检取消），新增5当前ACL原件HDD、23恢复审计，所选旧Root/HDD保持；真实Authentik→Vue→永久PG/Worker→Agent→实际文件、普通/匿名拒绝、两原上下文正常双端退出和准确清理通过。独立只读Passed、22真实PNG、11核心healthy/8数据库与7受保护配置保持；不重跑旧任务抽屉、不算异盘，恢复故障/递归ACL/删除与其余59项继续，整项完成率不升。[实际发布、证据与未验边界](qa/CONTAINER_ACL_RESTORE_REAL_UI.md)。

以下旧日期条目保留各自当时的版本与“未部署/未验”边界，按时间顺序阅读，由上条本批记录接续，不作为当前运行状态。

**2026-11-03 ACL恢复四镜像预构建已完成，未部署**：运行源码以user从08b2bb8快进冻结cad6182，标准Git archive构建四镜像成功；各实际ID的CLI/build-info及label同源cad6182，Ubuntu慢源依赖正常安装，未删依赖。11运行核心ID/镜像/StartedAt/healthy、Agent684268/维护684263与实际08b2bb8/1.52、账本及7份私密/权限文件摘要保持，活动0、源列不存在/17CHECK。CI四路成功，前端通过新ACL与容器布局组、仍在最后授权/文件/终端组；未启动发布unit/宿主安装/生产迁移或真实恢复。待五路终态再标准覆盖及新独占真实恢复整链，59项整项完成率不升。[基线、构建、失败修正与未验边界](qa/CONTAINER_ACL_RESTORE_PREBUILT.md)。

**2026-11-03 ACL恢复发布准备：CI身份检查缺口与门禁候选已修，未部署**：d183 CI实际暴露历史helper关闭原FD后仅比dev/inode可被inode复用绕过；5423433改为读取期间pin原FD并对原/新路径完整九字段复核，30历史专项/469旧文件回归通过。542 CI后端/真实PG/Valkey成功，但安装Node测试缺前端依赖、页面80/81误等来源warning后过早刷新；cad6182移至依赖安装后并等待明确错误/原key，2请求测试及5次丢响应复验通过。2fb2a40新增六场景真实恢复主验、独立SQL/Root/HDD和精确清理工具，29证明/8范围/语法通过，另原样本机CI安装门禁54组1533项及5CLI检查通过，**尚未运行真实恢复**。生产仍08b2bb8/1.52及原PID/11核心healthy、来源列不存在/17CHECK；待CI五路成功再覆盖及真实恢复验收，完整故障/递归ACL/删除等59项继续，不升整项完成率。[CI失败、修复及真实门禁边界](qa/CONTAINER_ACL_RESTORE_RELEASE_PREPARATION.md)。

**2026-11-03 ACL历史恢复管理员API/Vue候选3fd7f5f已接入，未部署**：新增严格五字段来源选择登记及原ID/key查询，复用原ACL确认/取消与永久Worker；Vue从历史快照进入新预检，完整来源与当前/目标ACL、空default、显式确认、原key刷新、迟到/退出/源变化拒绝及共享写入锁已接。冻结Release零警告错误/来源3fd7f5f、4361 Unit/488 Integration（7默认原生跳过）、1059前端Unit及类型/构建/lint/格式通过；HTTP身份/Agent/仓储和页面API为夹具，不记真实恢复。16项恢复与65项关联页面分批通过，联合轮79/81及两脚本修复原件保留，不签单次81全绿；生产仍08b2bb8/1.52与Agent684268/维护684263、11核心healthy不变，新来源列不存在/17 CHECK。尚需正常覆盖与真实身份/文件ACL/当前原件HDD恢复整链、完整故障/递归ACL/删除及其余59项，整项完成率不升。[候选、失败与未验边界](qa/CONTAINER_ACL_RESTORE_WEB.md)。

**2026-11-03 ACL历史恢复内部业务/永久化候选ffd08a8、冻结47e3ba1通过，未部署**：仅用所选capture/digest派生新的单类型access/default ACL计划与独立执行，完整来源快照纳入请求MAC、18个CHECK及来源不可变触发器，保留原普通MAC/共享锁和写前当前ACL备份语义；规划/确认/首次Apply前复核来源，原Root回执优先对账，来源存在即拒绝破坏性降级。冻结Release零警告错误/来源47e3ba1、4361 Unit/463 Integration（7默认原生跳过）、双隔离真实PG完整1执行/1通过/0跳过及准确清理通过；快照/Root/身份为合成，不算实际恢复HDD。首轮PG已过但记录器拒绝新字段的整体Failed保留，修复3项记录器测试/8范围通过后重跑取得Passed。运行六组件仍08b2bb8/1.52、Agent684268/维护684263与11核心healthy不变，生产新列不存在、仍17 CHECK。管理员恢复API/Vue、部署及真实身份/当前原件HDD恢复整链、故障矩阵/递归ACL/删除和其余59项仍待，整项完成率不升。[实现、失败、证据与未验边界](qa/CONTAINER_ACL_RESTORE_PERSISTENCE.md)。

**2026-11-03 ACL历史只读常规整链已真实部署验收**：唯一正常覆盖Invocation91e75020130045d58a072f1c360d3c50成功，六组件08b2bb8/1.52同源，Agent684268/维护684263；11核心healthy/8数据库平台及私密配置不变，账本current08b2bb8/previous0478262/pending空。工具17590e8新独占严格轮真实Authentik→Vue→API/10持久PG审计→Agent/HDD完成2页/8快照，源/Root/HDD保持、普通/匿名两历史GET各403/401、原两上下文正常双端退出及独立Passed；36真实PNG和准确清理通过。前两轮工具JSONB类型/50与100响应竞争失败及已完成分项保留，不记整体首轮通过；19证明/2竞争回归/8范围/语法通过。历史恢复、查询中故障、递归ACL、删除与其余59项继续，整项完成率不提升。[部署、失败、严格原件与边界](qa/CONTAINER_ACL_HISTORY_REAL_UI.md)。

**2026-11-03 ACL历史管理员API/Vue只读候选e83bad0已验证并推送，未部署**：新增准确容器/实例/路径的历史List/Read两GET，实时管理员与原登录在RPC前/输出后/持久审计后复核，严格query/NoStore/数组冻结；复用现有审计表，仅记录路径与实例hash/元数据摘要，无新schema或恢复入口。Vue完整原两类ACL/显式0/mask有效权限、版本分页/空部分页、错误/迟到/退出清理及320小屏明暗滚动齐接。冻结Release零警告错误/API来源e83、4343 Unit/463 Integration（7默认原生跳过）、1042前端Unit、89生产预览（22新+67关联）及类型/构建/lint/格式通过；测试目录/Agent/审计和页面API均为夹具，不算实际Authentik/PG/HDD页面。应用仍0478262、宿主2ef06f0/1.52及561098/561094，11核心healthy不变；下一步正常覆盖与真实业务整链，再历史恢复/递归ACL/删除及其余59项，不提升整项完成率。[候选、失败与验收边界](qa/CONTAINER_ACL_HISTORY_WEB.md)。

**2026-11-03 ACL历史1.52安装态已通过真实签名RPC/HDD与独立核验**：宿主及原生探针冻结2ef06f0，唯一正常安装Invocation0552122d971a42f8a925b5455217fa89成功，Agent561098/维护561094及映射来源同源；应用仍0478262、11核心ID/镜像/StartedAt不变且healthy，无迁移/全栈发布或回滚。新独占9旧ACL操作留8份HDD，真实List10页/Read8份、29边界拒绝、源/Root/HDD全字节与身份保持、精确清理及独立Passed；实际只读unit仅DAC_READ_SEARCH/SYS_PTRACE与读租约目录可写。错Invocation拒绝/准确停止及stdin EOF两控制分项、旧所有者/两递归签名能力主与独立兼容通过；58 C#专项/8探针proof/构建语法通过，无新前端/Authentik页面。HDD同device非异盘，控制前置不冒充进行中RPC取消；历史业务/API/Vue/恢复及其余59项继续，整项完成率不提升。[真实来源、原件与未验边界](qa/CONTAINER_ACL_HISTORY_INSTALLED.md)。

**2026-11-03 ACL历史1.52签名RPC/SDK与受限只读传输候选6013aa1通过，未安装**：新增独立List/Read、全请求/方法/Subject/Trace与分页/捕获绑定、一次性短时能力；显式0/false存在位及严格输出校验，SDK必须minor≥52/专属能力，无写入降级。固定helper仅两读取动作，只有读租约目录可写、cap仅DAC_READ_SEARCH/SYS_PTRACE，安装/校验源码接入但生产未运行。冻结Release零警告错误/来源6013、4343 Unit/432 Integration（7默认原生跳过）、58新C#专项/28历史传输root/431旧文件/57维护/5安装预览通过，初编译/旧51断言/无匹配及收集器PATH失败保留。运行仍0478262/1.51、315250/315245及11核心healthy；无新实际RPC/HDD/业务/UI，不复用前批实盘补签。[候选、安全与后续](qa/CONTAINER_ACL_HISTORY_RPC.md)。

**2026-11-03 ACL历史只读内核候选818396e/891acf4通过，未安装/未恢复**：复用独立container-acl-v1真实HDD原计划，新增完整校验/预期摘要/当前容器路径绑定及有界版本分页，隐藏owner/raw plan/原inode与属主字段；无CLI/写入/恢复或权限扩展。15 Linux-root新专项及431旧容器文件回归通过；冻结候选实际新独占容器5捕获/5查询、7页/独立5HDD-Root及准确清理通过，11核心/宿主0478262/1.51与315250/315245不变，源与HDD同device不算跨盘。库存计数/版本全局，只有条目正文按CID路径过滤，不签多租户统计隔离；签名RPC/C#目录/管理员API/Vue及历史恢复仍待，不把root候选当人类页面验收。[来源、安全、实机分项与剩余](qa/CONTAINER_ACL_HISTORY_READER.md)。

**2026-11-03 0478262维护确认框修复已部署，真实Auth页面严格复验通过**：真实134fd25确认框实际是英文Cancel/OK（按钮在视口内），修复为中文取消/确认更新/确认回退、取消关闭正常返回及长计划滚动，14生产构建预览/993前端单元/类型构建lint通过；唯一标准覆盖Invocation8c8978188447476e9dd888cef34b1f79成功，六组件0478262/1.51，Agent315250/维护315245，current0478262/previous134fd25/pending空、11核心healthy/8数据库与配置sudo保持。QA37dbe58新账号真实管理员4版本卡/4预检取消、Agent页、普通5路403/两页隔离、两原上下文正常双端退出及6匿名401、11真PNG与独立指纹/准确清理通过，执行POST0；验收器错误role及早期失败原件保留，最终57离线门禁通过。未验安装态强杀/真实应用回退/物理冷启动/新文件HDD写入，P0-05/59项不升整项完成。[修复、真实页面与边界](qa/PLATFORM_MAINTENANCE_REAL_UI.md)。

**2026-11-03 P0-05新维护工具已安装，134fd25真实正常覆盖及权威历史通过**：七固定来源受控交接不重启原宿主/容器后，只启动一次标准update（Invocation9e1f5d033b5f4538843bcb7a76711663，12:55:03–12:59:31/268秒），六组件及实际进程映射/HTTP均134fd25/1.51，Agent253100/维护253095；真实账本current=134fd25、previous=306f646、pending=null，原错误previous字节留legacy，完成Root回执正确。11核心healthy/8数据库平台ID-镜像-StartedAt不变，配置/能力/sudo保持；47相关测试、安装前后validator、真实锁忙/Agent权限隔离及已安装维护二进制隔离状态验证通过。216实际维护HTTP样本均200但观察器超时空窗保留；**没有新Authentik/文件-HDD页面或物理冷启动/真实手动回退验收**。P0-05和59项仍部分实现，不自动回滚/重启。[安装、真实历史与未验范围](qa/PLATFORM_CONTROL_INSTALLATION.md)。

**2026-11-03 P0-05维护工具强杀恢复候选1176084/4eb4d15已验证，尚未安装**：新增root私有持久检查点、三固定工具完整核验/原子恢复、独立boot/timer与ExecStopPost，installer共用发布锁且手动恢复保留当前runner；恢复不执行Git/Docker/安装器或应用，不自动回滚。47相关测试、11构建回归、5安装CLI及语法通过；原样模块在两场景隔离systemd沙箱验证强杀退出钩子、锁忙跳过和影子/run丢失后恢复，最终11核心完整身份/StartedAt及两服务PID不变。**合成账本和影子目录不等于生产升级/恢复或物理冷启动验收**；生产仍306f646/1.51、Agent4135740/维护4135726，新工具/账本未安装或创建，旧错误记录保持，P0-05/59项不提升完成。[实现、原件与未验边界](qa/PLATFORM_CONTROL_RECOVERY.md)。

**2026-11-03 P0-05可信发布记录候选463e19e/1811bd3已验证，尚未安装**：不再把checkout HEAD当上一版，新增root私有原子账本，从六组件实际来源初始化；只在发布完成且实际来源一致时晋升，失败/重试/同版重发保留原记录，手动恢复选择可信记录且不降级数据库。旧previous保留为legacy证据，兼容文件不再是权威；状态原子写入且不跟随目标符号链接，前置拒绝不改已有健康状态。30新隔离测试、11冻结构建/来源回归、5安装CLI及语法通过；包含两个真实临时子进程强杀，不是生产恢复演练。实际只读六组件探测通过，运行仍306f646/1.51、Agent4135740/维护4135726及11核心healthy，生产账本未创建/工具未安装/原记录未改。真实升级、手动恢复强杀/冷启动控制入口及完整P0-05仍待闭环。[实现、失败与真实验收边界](qa/PLATFORM_REVISION_HISTORY.md)。

**2026-11-03 306f646覆盖及ACL旧版本预检拒绝已实机通过**：原唯一Invocation d38034cfaba24730ac1fe1acae325829在10:50:22成功结束（约1690秒），下载EOF/软件源超时后同次构建完成，无重复发布或回滚；六组件CLI/镜像/HTTP与实际宿主exe/DLL映射一致，Agent4135740/维护4135726、协议1.51，11核心healthy/8数据库不重建/无新迁移，配置和开发sudo保持。冻结2435d44/proofVersion1新资源只改准确FD的mtime，原Vue旧版本被真实预检拒绝：Failed/VERSION_CONFLICT、1规划Job/2审计，执行Job/Root/HDD均0，不能把null说成已修改或未修改的执行回执。15字段SQL/API、320暗色/1440亮色详情、5真PNG、正常双端退出、普通3路403/匿名2路401、独立核验及准确清理通过；89离线本轮通过。剩余执行摘要/共享写入/磁盘路径、ACL递归恢复/删除与59项继续，P0-05旧previous-revision缺陷未修。[部署、真实证据与范围](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

**2026-11-03 10:44标准覆盖发布进行中，尚未签收**：运行checkout已由user干净快进至306f646，仅启动一次`whaledeck-dev-publish-306f646.service`，Invocation`d38034cfaba24730ac1fe1acae325829`。宿主已为306f646（Agent4135740/维护4135726），API/Gateway仍447070e，11核心容器原ID/镜像/StartedAt保持且healthy，8数据库不变；私密配置与开发sudo摘要保持。容器镜像构建遭遇NuGet EOF与Ubuntu源超时，软件源安装已完成、NuGet仍重试；unit仍activating，运行中的Result=success/ExecMainStatus=0不等于发布成功。正确8080维护入口本段1052次采样均HTTP200/updating，未覆盖整个发布窗口。**无迁移/容器替换成功回执，新真实旧版本预检门禁未开始，不计部署或功能通过**。89离线边界/范围本轮重跑通过；目标59项不变。[唯一发布、原件和接续约束](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

**2026-11-03 ACL预检失败摘要候选caa9c64已验证，尚未部署**：修正planning/Failed误显示“执行失败”，改为“预检失败，未进入 ACL 修改执行”，执行失败仍保留原语义；版本冲突文案不再假定已完成预检。993前端单元、8生产预览浏览器、89相关离线门禁及类型/构建/lint/格式通过。新增独占文件FD仅改mtime→原Vue旧版本预检拒绝的真实门禁、无执行/Root/HDD断言与独立只读核验，已接CI；**新真实门禁未运行，测试接口/临时文件不能替代Authentik/实际Worker与磁盘整链**。运行仍447070e/1.51、Agent3947643/维护3947551及11核心healthy，无部署/迁移/服务重启；59主项和25%–35%粗估不变。[候选、失败与真实验收待办](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)。

**2026-11-03 ACL写后同CID实例变化已实机签收**：QA冻结d2ca69d/proofVersion4，产品仍447070e/1.51，无新增部署。仅重启本案容器一次，CID/镜像/绑定保持、PID4044343→4047348及StartedAt改变；旧计划/回执仍绑定旧实例、Root/SQL Unknown且aclConfirmed=null，实际已写ACL不回滚或续做。不同当前管理员真实320×600暗色页面先暂不处置再明确确认，两次任务摘要“未证明”完整入视口；跨容器目录四次自然延期[2,4,6,8]后同ID才发布。原Root/HDD/迟到Apply不变、3Job/7审计/9真实PNG、独立原件与准确清理、81相关门禁及11核心healthy通过。首轮Docker弃用提示导致断言失败和降级确认已保留，不计整体首轮通过；其他共享写入/磁盘/路径与摘要分支、ACL递归恢复/删除及59项继续。[实际失败、实例绑定与验收边界](qa/CONTAINER_ACL_INSTANCE_CHANGE.md)。

**2026-11-03 写后目标消失Unknown/处置摘要与447070e小屏修复已实机通过**：前两新资源在612f77c真实写后/Unknown/明确处置已发生，但Ack字段完整视口失败，原件与准确清理保留、不记通过。已补真实长错误码复现，447070e固定详情表格与长值换行并正常覆盖，六组件/HTTP/实际进程映射一致，Agent3947643/维护3947551、协议1.51，无新迁移。第三套全新资源严格门禁及独立复核通过：ACL已变后原容器移除，原Unknown/null/Root/HDD保持，另一管理员先暂不处置再确认，原Job Failed/Acknowledged，Unknown→Ack两任务摘要及“未证明”完整入视口，另一容器目录四次真实延期后同ID发布。75边界、27前端单元与8浏览器、9实际PNG/准确清理/11核心healthy通过；不记整体首轮通过。其他实例/磁盘/共享写入与摘要分支、ACL递归恢复/删除和59项继续。[失败、修复与真实证据](qa/CONTAINER_ACL_POST_UNKNOWN.md)。

**2026-11-03 ACL写后Agent重启及原任务摘要实机通过**：运行612f77c/1.51不重发，冻结47a1769/schema6的新独占资源在目标ACL已写/Root Applying且未回执时实际重启Agent；产品0.013秒停原helper，Agent3747527→3835700，维护3747520不变/153次HTTP200 healthy-Ready。原任务Succeeded/Completed、完整写后清单/1HDD与迟到同操作Apply不再变化；原预检Ready及执行Succeeded两份SQL/API摘要、320×600暗色真实详情和同上下文正常双端退出通过。独立原件/准确清理、48边界/范围与语法通过，11核心healthy。其余Unknown/实例/共享写入/摘要分支、递归恢复/删除与59项继续。[实机证据与界限](qa/CONTAINER_ACL_POST_AGENT_RESTART.md)。

**2026-11-03 612f77c已覆盖，ACL任务摘要真实常规整链通过**：标准update唯一Invocation689e10e7f8ba479ba2b0e97de386f33c成功，六组件/HTTP/实际宿主进程映射同源，11核心healthy、8数据库平台不重建；Agent3747527/维护3747520，协议1.51不变，无新迁移。冻结8c39d09的新独占资源九场景8完成/1预检后取消，17新摘要SQL/API/真实任务详情及1旧任务空态通过，原字节/两类ACL/xattr/8HDD、同上下文双端退出与准确清理通过。首轮独立观察器误读输出目录的FileNotFoundError保留，64f6d8b修正证据输入路径后只读复核通过，未重做真实写入。19边界/范围与语法通过；Unknown/处置等摘要分支及写后Agent/实例/跨写入剩余组合、ACL递归恢复/删除与59项继续。[实机证据与界限](qa/CONTAINER_ACL_JOB_RESULTS_REAL_UI.md)。

**2026-11-03 ACL任务安全摘要候选2491add/9b419c2**：新增预检/执行/Unknown/明确处置的15字段受限摘要，与任务/事件同事务保存；读取校验原意图/执行ID、阶段、状态和严格字段，未知/错误数据不原样返回。Vue展示真实三态、普通模式与历史回执，预检不冒充已修改，处置不冒充恢复；旧任务不补造历史，成功/失败空文案已区分。摘要源码候选与回归已验证，运行仍633a2ca/1.51，未部署、未签收真实身份/文件/HDD页面整链。[候选与验收边界](qa/CONTAINER_ACL_JOB_RESULTS.md)。

**2026-11-03 ACL写后实时移组/停用实机及独立核验通过**：运行633a2ca/1.51不重发，冻结8d93fbe/schema5/27依赖，两新资源目标ACL已写/Root仍Applying时准确移组或停用原身份；原正快照余7163秒，产品4.806/4.834秒停helper，原任务GET403、移组/me200非管理员并/forbidden、停用/me403并/login，引用键null/取消POST0。原任务按实际事实Succeeded/Completed，原字节/属主/mtime/另一ACL/xattr保持，写后清单/版本及迟到Apply不再变化，2HDD/4Job/8原主体审计与独立准确清理通过。不同当前管理员正常BFF只读GET200核对原终态并HTTP双端退出；后续0b7d43c真实任务中心原执行ID/Succeeded只读页面及正常退出通过，管理写0/原SQL不变；其完整ACL结果摘要仍待。6有效PNG/56边界/语法、11核心healthy通过；连接中断后未重做已完成操作。写后Agent/Unknown/其余组合、递归恢复/删除及59项继续。[实际角色与证据边界](qa/CONTAINER_ACL_POST_REVOCATION.md)。

**2026-11-03 ACL目标消失Unknown/不同管理员处置与跨容器目录保护严格复验通过**：运行633a2ca/1.51不重发，d977c7a/proofVersion2/33依赖使用两新独占资源，Preparing/HistoryVerified后准确移除原容器、原上下文正常退出，产品4.981秒停helper；原ACL Unknown/null与原HDD/回执MAC/全源保持。另一容器原目录任务四次真实自然延期版本[2,4,6,8]，当前不同管理员320×600先暂不处置再唯一ACK后，原Job Failed/Acknowledged、原Root/HDD及迟到Apply仍Unknown；目录同一ID才实际发布并在Vue视口可见。3Job/7审计/5真实PNG、独立原件/准确清理、52边界/语法及11核心healthy通过。首轮cab4dbc把初始排队误计第四次且目录截图未入视口，已明确拒绝旧Passed、原件留存，非整体首轮通过。其余组合/递归恢复/删除及59项继续。[失败、严格复验和边界](qa/CONTAINER_ACL_UNKNOWN.md)。

**2026-11-03 ACL写后取消/Worker回执中断实机通过**：运行633a2ca/1.51不重发；冻结517c42a，两新独占资源准确抓到目标ACL已写、Root Applying/尚无完成回执，取消后产品5.313秒停helper，Worker实际强杀后0.123秒停helper/42采样87.079秒等原自然租约。两案原任务最终Succeeded/Completed，不误报未修改或恢复旧ACL；目标ACL与新增ctime实际证明，原字节/inode/属主/mtime/另一ACL/xattr不变，写后清单/版本在迟到同操作Apply重放前后完全保持。2意图/4Job/9原主体审计/2HDD、原上下文正常退出、4真实PNG及独立原件/准确清理与v3兼容通过，54边界/语法通过、11核心healthy。剩余写后撤权/Agent/Unknown/跨写入、ACL递归恢复/删除及59项继续。[实际写后结果与边界](qa/CONTAINER_ACL_POST_CHANGE.md)。

**2026-11-03 ACL写入前Worker强杀/Agent重启实机通过**：运行633a2ca/1.51不重发；冻结ed241e9，两新独占资源在Preparing/HistoryVerified暂停，Worker实际SIGKILL后产品0.116秒停helper，原token/expiry完全相等，42次真实采样/87.49秒等原自然租约；Agent实际重启后0.015秒停helper，PID3122630→3288068、维护PID3122598不变，152次维护HTTP均200 healthy/Ready。两案原业务Failed/Interrupted、Root Aborted、完整源/两类ACL/xattr与2HDD保留，2意图/4Job/8原主体审计、同上下文正常退出、4真实PNG及独立原件/准确清理通过。6fce7dc补实际Worker见证/严格租约后只读复核仍通过，47边界/语法通过，11核心healthy；变更后/Unknown/跨写入、ACL递归恢复/删除及其余59项继续。[实际重启、租约与边界](qa/CONTAINER_ACL_RESTARTS.md)。

**2026-11-03 ACL原登录实时移组/停用实机通过**：633a2ca/1.51不重发；冻结f3ca007/27依赖，两新独占资源在Preparing/HistoryVerified且尚未写ACL时，仅准确变更原临时身份的组或active，原正快照余7164秒以上，产品5.195/5.213秒终止绑定helper。原登录旧任务403，移组/me200且非管理员、320暗色/forbidden；停用/me403、1440亮色/login，两案引用键null/取消POST0。2意图/4Job/8审计、Canceled/Aborted、完整字节/九字段/两类ACL/xattr及2永久HDD保留，独立原件/准确清理与旧v1证据只读兼容通过。38边界与语法通过，11核心healthy，除Worker正常启停外核心/两宿主保持；其余重启/变更后/Unknown/跨写入、ACL递归恢复/删除及59项继续。[真实失权与验收边界](qa/CONTAINER_ACL_REVOCATION.md)。

**2026-11-03 ACL进行中取消与正常双端退出实机通过**：运行仍633a2ca/1.51；冻结78479ec，两新独占资源在Root Preparing/HistoryVerified且尚未写ACL时由真实Vue取消/原用户菜单退出，产品5.433/5.520秒终止精确绑定helper。两案Canceled/Aborted、完整2097159字节与九字段/两类ACL/普通属性不变、2永久HDD/4 Job/9审计、同原上下文双端退出和独立原件/准确清理通过，320×600暗色取消按钮可见。14专项+8范围与语法通过，失败未知ID/Unknown/活动租约清理已加固；11核心healthy，除Worker正常启停外其余10核心及两宿主PID/source不变。尚非ACL变更后中断、撤权/重启/Unknown/跨写入或递归恢复、删除等完整验收；59项保持部分实现。[实际切点、失败与未验边界](qa/CONTAINER_ACL_FAULTS.md)。

**2026-11-03 ACL已正常覆盖633a2ca，真实页面九场景首轮及独立核验通过**：唯一发布Invocation5761b5620b27452d925f196bb134537e成功，六组件/HTTP/实际宿主映射同源、生产ACL迁移17 CHECK应用，11核心healthy且8数据库平台不变，user NOPASSWD保留。冻结0668215/21依赖，以正常Authentik→Vue→SQL/Worker→Agent→实际ACL/HDD完成8成功/1预检取消，显式ID/权限0、mask/NoChange/default清空、完整2097159字节/原inode-UID-GID-mtime/非目标ACL/子项逐轮核对，8份原HDD与35审计/17 Job、原key/确认重试不重做通过。同原上下文正常双端退出、普通8路403/匿名4路401、CSRF/Origin及缺字段拒绝、9真PNG、准确清理与独立原件核验通过；源/HDD同device不算异盘。14离线及语法通过，前批单元/预览不冒称复跑或新CI全绿；真实进行中故障/Unknown/跨写入、ACL递归/历史恢复、删除及其余59项继续，不计完整ACL。[实际部署、原件和未验边界](qa/CONTAINER_ACL_REAL_UI.md)。

**2026-11-03 ACL管理员API/Vue候选062e361已通过，尚未部署**：实际永久业务入口与容器文件/全局Unknown处置页面已接；access/default完整条目、显式ID/权限0、mask有效权限/模式、默认清空、原两类ACL/执行身份/不可变原回执与确认/取消/原key恢复齐接。根/条目/确认/处置严格拒绝缺失、重复、额外和错误类型，保留实时权威身份/原登录/CSRF/Origin与十方保护；首登记执行回复只允许原GET核实。4272 Unit/432 Integration（7默认原生跳过）、972前端Unit、Release零警告错误及类型/构建/lint/格式通过；43 ACL和156关联生产预览浏览器分两组全通过，18明暗PNG及5175实际字节来源核验通过。真实桌面页脚越界已修正，脚手架/断言失败保留；**测试身份/API/仓储/Agent不是Authentik/实际ACL/HDD业务证据，无发布或生产迁移**。下一步正常冻结覆盖与真实常规/故障整链，再ACL递归/恢复、删除和其余59项。[实现、失败和验收边界](qa/CONTAINER_ACL_WEB_UI.md)。

**2026-11-03 ACL永久SQL/十方共享保护与专用Worker候选已通过真实PG，未部署**：72d678e新增EF仓储/迁移17个CHECK、双Job/Outbox/审计、90秒租约/CAS与原回执独立MAC原子保存/不可改；既有八类容器写入及主机锁双向路径/发布/预算保护齐接。c9c9265补通用Worker专用消息排除与通用重试拒绝，最终4272 Unit/364 Integration（7默认原生跳过）、Release零警告错误及10范围/状态门禁通过。冻结72d678e双隔离真实PG完整1执行/1通过/0跳过，独立原件/准确清理、11核心不变且healthy/8数据库平台不变、生产ACL表不存在通过；原回执/身份/时钟为合成输入，**不是实际Worker/Authentik/ACL文件HDD或页面证据**。无发布/回滚，前批4a58961/1.51安装RPC事实保留；下一步严格管理员API/Vue及正常覆盖后真实业务常规/故障整链，ACL递归/恢复、删除与其余59项继续。[候选、失败和证据边界](qa/CONTAINER_ACL_PERSISTENCE.md)。

**2026-11-03 ACL内部业务候选41f02fb已验证，尚未接永久仓储/页面**：新增独立意图、原登录/实时授权/幂等ID、请求/计划/执行/确认/回执分域HMAC；ACL列表深复制与逐条比较、default显式清空/mask模式、三态与不可变原回执、先Inspect原ID/Started后不重做及Unknown明确处置已接内部编排。Release零警告错误、4270 Unit/363 Integration（7默认原生跳过）及8范围通过；首轮夹具Unknown回执未保留的1失败已修正并保留。**仓储仍是显式内存测试夹具，无EF/迁移、共享保护实现、Worker/API/Vue或真人业务验收，本批未发布**；4a58961/1.51实机RPC/HDD保持前批事实，不重做、不当作本批业务证据。下一步具体永久SQL/双向写入保护和隔离真实PG，再API/Vue及真实身份文件/HDD整链；ACL递归/恢复、删除和其余59项继续。[候选、失败与边界](qa/CONTAINER_ACL_WORKFLOW.md)。

**2026-11-03 ACL Agent1.51已安装，真实签名四RPC/HDD及旧能力兼容通过**：正常覆盖后宿主实际4a58961，Agent PID2818452/MaintenanceHost PID2818448；API/Worker/Gateway仍b1fafed，11核心自安装前ID/镜像/StartedAt不变且healthy。新ACL运行4a/工具224f744分列，9操作(8完成/1执行前Discard)、13预检/4执行绑定拒绝、8HDD原ACL元数据、完整2097159字节/原inode-owner-mtime/非目标ACL/子项及每轮重放核对通过；首个旧ID真实签名Apply不重写。主/独立准确清理通过；旧所有者6操作、递归chmod4/递归chown4及各自独立HDD兼容通过。真实入口历史类错误和探针非法篡改预期错误两次失败均在ACL写入前，已修复、保留Failed及准确恢复；NuGet EOF标准重试成功，无缓存/凭据注入或审计降级。469 Linux-root/97 ACL C#/3探针/12宿主/5安装CLI/8范围及语法通过；最终维护healthy/Ready、idle/transient0、user NOPASSWD保留。仍无永久ACL业务/共享保护/Worker/API/Vue或真人页面，同设备非异盘。[安装、原件和剩余边界](qa/CONTAINER_ACL_AGENT_RPC.md)。

**2026-11-03容器ACL独立RPC1.51/SDK/固定传输候选6a62027通过，未安装**：新增四unary方法、完整请求/方法/单次短时capability独立签名、显式ID/权限presence、原access/default ACL与目标mask模式/回执绑定；SDK严格协商major1/minor≥51及专属能力，旧chmod/chown不扩大。固定stdin/unit/准确Invocation停止、独立状态和安装/验证接入；无CAP_CHOWN/SYS_ADMIN，初始user namespace及准确FD statmount BY_FD证明非IDMAP，不支持/错误直接拒绝。冻结Release零警告错误且编译Agent来源6a62027；4132 Unit(96新)、363 Integration(7默认原生跳过)、468 Linux-root(12新)、12宿主/5安装CLI/8范围及语法通过。首轮2测试编译错误/只读汇总sudo PATH错误保留并修正。实际只读apps FD依赖支持、11核心及宿主PID/b1fafed来源不变且healthy、idle0；当前ACL文件/状态未安装，真实签名RPC0，无业务/Worker/API/Vue或真人页面，不重复ed50实盘。下一步原生探针/正常宿主覆盖及真实RPC-HDD拒绝/兼容，再永久业务页面/ACL剩余/删除及59项。[完整候选与边界](qa/CONTAINER_ACL_RPC.md)。

**2026-11-03记录：容器ACL底层候选ed50dc1已通过真实容器/HDD，未安装**：2026-11-02冻结源码新增独立FD access/default ACL、显式mask/数值ID、64项预算、独立原ACL HDD历史及只观察恢复；写后确认/恢复须原inode fsync成功，持续EIO保留Unknown。22新专项纳入456项Linux-root全套零跳过、8范围与语法通过；实际setuid/组权限、default继承、6隔离强杀切点有证据。新独占容器/HDD候选首次9操作(7完成/2中止)、2候选SIGKILL、8份HDD元数据/完整2097159字节/原inode-owner-mtime普通属性及迟到不重写通过，独立核验准确清理/11核心与宿主PID来源不变且healthy通过。产品仍b1fafed/1.50，未签名RPC/永久业务/Worker/API/Vue或真人页面；同设备历史非异盘，namespace映射须接入前明确。下一步C#协议/SDK/固定受限传输及安装态验证，再完整业务页面、ACL剩余范围/删除及其余59项。[实现、实际依赖与边界](qa/CONTAINER_ACL_ENGINE.md)。

**2026-11-02递归所有者目标消失Unknown及跨容器目录延期/释放实机通过**：产品保持b1fafed/1.50，无发布/回滚；冻结003f504及a6f472b/各21依赖，两套新组合首次实机和独立核验均通过。准确首项fchown后只移除自有CID，源树保留，原管理员正常双端退出，产品2.755/2.197秒结束原helper；均汇总0/0/0/0/18待证明、物理1项实际修改，不能推断零修改。不同新管理员真实320×600五计数/暂不处置/明确确认及原Unknown/Root/HDD/完整源/迟到不改通过；第二例另一容器原目录任务版本2/4/6/8自然延期，23:38:46.298696处置后原ID才于23:38:53.318649实际Root写入并Succeeded、Vue列表可见。9真PNG/12哈希审计/4HDD元数据、准确清理与最终idle0、11核心healthy/8数据库平台不变通过。56递归+8范围和语法通过；仅跨容器目录组合、同设备历史，不扩大其他写入/异盘或未保存的历史文件系统重算结论。[事实与剩余边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_UNKNOWN_BARRIER.md)。

**2026-11-02递归所有者真实组撤权/账号停用与Partial五计数页面通过**：产品保持b1fafed/1.50，无发布/回滚；冻结cf291aa/21依赖，两套新独占资源首次实机门禁及各自独立核验通过。首项实际fchown后只调整本轮临时身份，正快照仍余7161.151/7162.322秒，产品3.951/4.255秒结束原helper；旧任务403，移组后me200非管理员/403页，停用后me403/登录页，取消POST0。均原Partial 1完成/1修改/1中止/16未开始/0待证明，另一管理员真实320×600页面逐项五计数、先暂不处置再唯一确认及正常双端退出通过；原Root/HDD/完整源/迟到不改、准确清理、10哈希审计/10真PNG及11核心healthy/8数据库平台不变通过。旧页面只验证当前region/sessionStorage提示清理，不泛称全部缓存；撤权不记正常退出。56递归+12身份+8范围及语法通过，附加只读汇总器tuple/list错误保留后修正，不重做操作。Unknown/跨写入、ACL/删除及其余59项继续。[事实、原件及未验边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_REVOCATION.md)。

**2026-11-02递归所有者Worker强杀/原自然租约及Agent重启/维护入口实机通过**：产品b1fafed/1.50不发布不回滚，冻结0442fb4/21依赖，两套新资源首次门禁及独立核验通过。首项fchown后准确Worker SIGKILL137/同CID镜像重启，产品2.255秒结束helper，40样本/83.985秒自然等待原租约后原ID恢复Partial；仅固定Agent真实重启PID2380326→2542378、维护PID2380322不变，17次入口采样200/healthy/Ready/no-store，产品2.341秒结束helper。两例均1完成/1修改/1中止/16未开始/0待证明、不续做，当前另一管理员真实页面处置/正常退出、Root/HDD/完整源与迟到不改及准确清理通过；10哈希审计/10真PNG、11核心healthy/8数据库平台不变。56递归+8范围及语法通过；原租约观测新增有界拒绝，不编辑SQL/时钟。实时撤权/Unknown/ACL/删除及其余59项继续。[原件、边界与恢复证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_PROCESS_RESTARTS.md)。

**2026-11-02递归所有者真实进行中取消/正常双端退出与Partial处置通过**：产品保持b1fafed/1.50，不发布/回滚。冻结51e0ec3及21依赖，两套新资源首次真实门禁与各自独立核验通过；首项实际fchown后页面取消/同上下文正常双端退出由产品5.012/4.339秒结束原helper，均保留Partial（1完成/1修改/1中止/16未开始/0待证明）。退出前后原账号启用/组/身份与原CID/PID/StartedAt不变，正快照余7157秒、取消POST0、旧任务401/私有引用清空。不同管理员真实小屏先暂不处置再确认，原Job Failed/Acknowledged，Root/HDD/完整源事实不改、迟到不续做；11哈希审计/10真实PNG、准确清理/11核心healthy/8数据库平台不变通过。55递归离线+8范围及语法已接CI；同设备历史非异盘，旧六场景退出Failed保留。其余故障/ACL/删除/59项继续。[完整事实与未验边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_PARTIAL.md)。

**2026-11-02递归所有者b1fafed已覆盖，真实页面六场景/源树HDD分项签收**：唯一正常发布Invocation32e9789056bf4728b58f189d160abefe成功，六组件/HTTP/实际宿主映射同源，生产迁移应用、11核心healthy且8数据库平台不变。正常Authentik→Vue→SQL/Worker→Agent完成5成功/1预检取消，显式0:0、零修改、完整2097159字节/原inode/模式/mtime/普通属性与5聚合/20子历史逐轮核对；23原主体审计、原key重试/刷新不重做。**原总脚本最后退出检查失败保持Failed**；观察器改为等实际OIDC导航/会话终态，另身份纯登录退出补证401/403，无文件修改。原六轮信号/SQL/Root/HDD重新核对并准确清理、独立只读核验通过，未重复所有者操作，不伪造原上下文双端退出。17离线门禁通过，17移出不算完成。进行中故障/同上下文退出、Partial/Unknown、ACL/删除和其余59项继续。[真实事实、失败与恢复边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_REAL_UI.md)。

**2026-11-02递归所有者管理员API/Vue候选a806f56已验证，未部署**：严格显式UID/GID、原登录/实时身份/幂等登记、原树预检/确认/取消/原引用恢复及当前管理员Partial/Unknown处置已接；原根属主/目标/执行身份、五计数、历史证明与当前观察分列，容器文件/全局异常入口与旧写入互斥、退出清理保留。4036 Unit、363 Integration（7默认原生跳过）、909前端Unit及Release零警告错误/类型/生产构建/lint/格式通过；44项生产预览递归所有者及112项关联页面浏览器分两组全部通过。首轮旧夹具令牌断言失败与准确中断保留，**测试身份/API/仓储/Agent不是实机业务整链**；本轮未发布/未改生产schema。下一步正常冻结覆盖及真实Authentik→Vue→SQL/Worker→Agent→实际树/HDD常规和故障整链，ACL/删除与其余59项继续。[候选、失败和证据边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_WEB_UI.md)。

**2026-11-02递归所有者永久SQL/九方保护/专用Worker候选a19f5c4通过真实PG，未部署**：新增EF仓储/迁移与16个CHECK、双Job/Outbox/事件/审计、90秒租约/CAS、原请求/计划/执行与Root计数历史不可改；九方双向数据库/路径/操作预算覆盖主机锁和八类容器写入，Partial/Unknown保留保护、只允许原ID明确处置，不自动续做/回滚。冻结Release零警告错误、4036 Unit/309 Integration（7默认原生跳过）、10范围/回执门禁通过；双隔离真实PG全套首次1执行/1通过/0跳过，独立准确清理/11核心ID镜像启动时间不变且healthy/生产新表不存在通过。编译与测试入口失败保留；Root/身份/时钟为合成输入，**不是实际Worker进程、Authentik、文件/HDD或页面证据，本轮未发布**。宿主22906f5/1.50、应用0dede08前批安装事实保留；下一步管理员API/Vue及正常冻结覆盖后的真人树/HDD常规与故障整链，ACL/删除和其余59项继续。[实现、失败与验收边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_PERSISTENCE.md)。

**2026-11-02递归所有者内部业务编排候选7a94733已实现，未接永久仓储/Worker/API/Vue**：新增独立意图、原登录/幂等ID、请求/整树计划/执行/确认分域HMAC，原属主/目标UID-GID/执行身份分列；显式0与缺失拒绝分开，当前权威身份及原登录在读写返回前复核，执行期间每6秒检查授权/取消/租约。Inspect优先、已开始不续做、Partial/Unknown保留计数与保护，当前管理员明确处置使用原执行ID。并修复两种递归Dispatcher在身份依赖失联时延迟可信Partial/Unknown/Aborted回执的分支；仅保存已核对事实，不放宽写入授权。冻结Release零警告错误、4034 Unit/308 Integration（7默认原生跳过）、8范围检查通过。**当前只有内部候选和测试夹具，尚无具体EF仓储/迁移、九方共享保护、Worker注册或真实业务页面，本轮未部署**；上一批宿主22906f5/1.50与应用0dede08安装证据保留。下一步永久SQL/双向保护和隔离真实PG，再API/Vue/正常覆盖及真实身份树/HDD故障整链；ACL/删除和其余59项继续。[候选、验证与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_WORKFLOW.md)。

**2026-11-02递归所有者Agent1.50已安装，真实签名四RPC/HDD通过**：冻结22906f5正常宿主覆盖，实际Agent PID2113282/MaintenanceHost PID2113274同源22906f5，标准validate/运行库/SHA/动态入口/user NOPASSWD保留通过。新原生四RPC在准确HDD树完成4操作(3完成/1执行前Discard)，含显式root 0:0、11预检/5执行绑定拒绝、完整2097159字节/原inode/mode/mtime/普通属性、每次重放原树与Root不改，3聚合/12子项HDD和独立准确清理通过。另旧递归chmod4操作及旧单对象所有者6操作与各自独立核验通过；11核心跨安装/RPC ID/镜像/状态/StartedAt不变且healthy，API/Worker/Gateway仍0dede08。两个构建收尾失败及旧核验参数错误保留，未重复已完成构建或真实操作。**仍无本功能永久EF业务/共享保护/Worker/API/Vue或真人登录页面**；同设备历史不计异盘灾备，CI四路成功/前端运行中不冒称全绿。下一步永久业务及真实身份页面/故障矩阵，ACL/删除与其余59项继续。[安装态证据与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)。

**2026-11-02递归所有者RPC/SDK候选2d3b7e0通过，未安装**：独立Plan/Apply/Inspect/Discard、完整请求/方法/单次capability签名、显式HasUid/HasGid（0不等于缺省）、五计数/历史/绑定校验与SDK major1/minor50专属能力协商已接，固定stdin/unit/准确Invocation停止、安装依赖和CI检查已登记。3904 Unit（106专项+4新增EOF）、308 Integration（7默认原生跳过）、434 Linux-root及12 host artifact检查通过，冻结Release零警告错误。新unit有CAP_CHOWN，旧chmod未扩大。**仍无永久业务/Worker/API/Vue或真实签名UDS/HDD验收**，运行0dede08/1.49、两宿主原PID/source及11核心healthy/上一实盘baseline不变，只读预检0，无发布/回滚。下一步宿主正常覆盖与真实四RPC/HDD/拒绝/兼容，再永久业务及真实身份页面；ACL/删除与其余59项继续。[完整候选与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_RPC.md)。

**2026-11-02递归所有者底层候选ef5425a通过真实容器/HDD**：新增显式UID/GID、有界双遍清单与独立聚合/子项历史域，复用单对象fchown与永久回执，Partial/Unknown只观察恢复、不续做。421项容器文件Linux-root（16新专项）零跳过通过；冻结10模块新资源4操作(2完成/1部分/1中止)、2次真实候选子进程SIGKILL、完整2097159字节/原inode/mode/mtime/普通属性与4聚合/9子项HDD和迟到不重做通过，独立准确清理/11核心不变且healthy通过。共享框架默认chmod的另4操作/2强杀及独立HDD回归也通过；schemaVersion严格整数加固未覆盖线上。**未安装/无签名RPC/永久业务/Worker/API/Vue或人类页面**，产品仍0dede08/1.49，不发布/不回滚；同设备元数据不计异盘灾备。下一步C# RPC/SDK/固定传输与安装实测，再永久业务及真实页面；ACL/删除和其余59项继续，17移出不算完成。[候选证据与边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_ENGINE.md)。

**2026-11-02递归进行中正常双端退出实机通过**：产品保持0dede08/1.49、不发布/不回滚。冻结8dc8d55及20份运行依赖，新资源首次真实门禁及独立核验通过。首项实际chmod后正常可见用户菜单完成BFF/Authentik双端退出，产品4.257秒结束原helper、取消POST0；退出前后原账号仍启用且组/Subject/PK不变、原快照尚余7157秒，原容器CID/PID/StartedAt不变。旧任务HTTP401、旧region/引用清空；原ID保留Partial(1完成/1修改/1中止/16未开始/0未知)，切点/退出后/恢复后全树相同，不续做。不同管理员真实明色小屏逐dt/dd核对五类计数，先暂不处置再确认，原Job Failed/Acknowledged、Root/HDD/完整源事实保留，迟到不重做。5哈希审计/5真实PNG/52检查通过，11核心healthy及8数据库平台不变；同设备历史不计异盘灾备。继续递归所有者/ACL/删除和其余故障/59项。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_LOGOUT.md)。

**2026-11-02递归进行中实时组撤权/账号停用两例实机通过**：产品保持0dede08/1.49、不发布/不回滚。冻结3c76ecc及20份运行依赖，两套新资源首次真实门禁及各自独立核验通过。首项实际修改后只改准确临时Authentik账号，原正快照仍剩约7160秒；产品4.116/4.113秒结束原helper，无取消POST，原实例保留。旧任务均HTTP403，移组后me200/admin=false及forbidden，停用后me403及login，旧任务region/引用清空。两例原ID保留Partial(1完成/1修改/1中止/16未开始/0未知)，切点/退出后/恢复后全树相同，不续做；不同管理员真实页面先暂不处置再确认，原Job Failed/Acknowledged、Root/HDD/完整源事实保留，迟到不重做。共10哈希审计/8小屏PNG/51检查、11核心healthy及8数据库平台不变；撤权不计正常双端退出，同设备历史不计异盘灾备。其余切点/退出、递归所有者/ACL/删除和59项继续。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_REVOCATION.md)。

**2026-11-02递归进行中Agent重启/维护入口17次观测通过**：产品保持0dede08/1.49，不发布/不回滚。冻结6d257a2门禁首次完整通过，首项实际修改后只重启固定Agent，PID1432315→1851523且实际apphost同源，MaintenanceHost PID1432309/source不变。重启前至结束后17次本机维护状态HTTP200/healthy/Ready/no-store，产品2.290秒结束原helper；切点/退出后/恢复后整树相同，原ID/计划恢复Partial(1完成/1修改/1中止/16未开始/0未知)，不续做。不同管理员真实页面处置/迟到不重做、完整文件/Root/HDD及独立核验通过，5哈希审计/5小屏PNG/11核心healthy、8数据库平台不变。后续QA异常收尾finally加固以离线注入验证，共30检查；非再次真实重启/启动失败。其余切点/退出/撤权、递归所有者/ACL/删除与59项继续。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_AGENT_RESTART.md)。

**2026-11-02递归Unknown跨容器目录真实延期/释放整链通过**：产品保持0dede08/1.49，不发布/不回滚。冻结7e8af47新组合通过，原Unknown18/实盘1修改保留保护时，另一独占容器目录原Job在Worker运行下观测到至少4次真实延期，版本2/4/6/8、延期时刻各异、无租约/目标目录/Root回执；排除其他任务/锁/终端。另一管理员真实页面先暂不处置再确认，原异常17:47:25.137157处置后，原目录Job才于17:47:25.353502开始并Succeeded，唯一登记、实际inode/1000:1000/0750及Vue列表可见。独立核验、5递归+2目录哈希审计、5小屏PNG、26检查及11核心healthy/8数据库平台不变通过；原件/HDD不变、两套资源准确清理。首轮Queued/Pending QA错误与正常失败清理保留。仅签收跨容器新建目录组合，其他类型/故障及递归所有者/ACL/删除等继续。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN_BARRIER.md)。

**2026-11-02递归目标消失Unknown不同管理员真实处置通过**：产品保持0dede08/1.49、不部署/不回滚。冻结300d19e门禁首次完整通过，首项实际修改后仅移除准确自有容器，源树保留；原页面正常双端退出由产品2.757秒结束原helper。SQL/Root为0完成/0已证明修改/0中止/0未开始/18待证明并保留保护，宿主侧独立证实1项修改，不能把Unknown汇总0推断为物理零修改。原账号删除后另一管理员320×600真实页面先暂不处置再确认，原Job Failed/Acknowledged、Root仍Unknown、计数/HDD/全文件不改；迟到原固定helper不续做（追加探针非签名RPC）。独立核验、5条哈希审计/4 PNG/24项检查/11核心healthy及8数据库平台不变通过。同设备元数据不计异盘灾备；其他组合、Unknown期间其他写入延期/释放、递归所有者/ACL/删除与59项继续。[完整证据和未验边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN.md)。

进度与代码量口径见[当前比例及规模估算](PROGRESS_ESTIMATE.md)：40/59（67.8%）为部分实现覆盖，不是完成率；整项签收0，整体工作量粗估25%–35%。非空产品源码不含迁移约9.49万行、测试/验收/工具约9.99万行；剩余新增产品约14.5万–29.5万行，加测试工具约26.5万–54.5万行，仅作规划，不替代验收。

**2026-11-02递归Worker强杀/原自然租约/Partial真实页面通过**：产品保持0dede08/1.49，不部署/不回滚。冻结23704b9的新资源在首项实际修改后准确SIGKILL原Worker（137），同容器/镜像重启；产品2.329秒结束原helper，Root可先保存Partial，SQL40次观测保留原token/expiry/保护/Job及文件指纹，实际83.827秒等原16:51:41.333302+08:00到期后才恢复。原任务1完成/1修改/1中止/16未开始/0未知，恢复未续做；原账号删除后另一管理员真实页面先暂不处置再明确确认，原Job Failed/Acknowledged、Root/HDD/完整文件保留。独立只读核验、5条哈希审计、5真实320×600 PNG、22项检查与11核心healthy/8数据库平台不变通过。两轮QA失败与准确清理保留；其余组合/Unknown/ACL/删除与59项继续。[证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_WORKER_CRASH.md)。

**2026-11-02递归进行中取消与Partial不同管理员真实处置通过**：产品保持0dede08/1.49、不重复部署。冻结c154d0a门禁在18项独占树第一项真实修改后捕获原helper，真实320×600页面取消由产品4.943秒结束helper，得到1完成/1修改/1中止/16未开始/0待证明，Partial保留保护与原计数。原账号删除后另一管理员实际页面先暂不处置再明确确认，原Job Failed/Acknowledged、保护释放；原Root/HDD/全文件字节与元数据不改，迟到固定helper不续做（该追加探针非签名RPC）。增强独立核验3fbbd45、6条哈希审计、4真实PNG、11核心healthy及8数据库平台不变通过；只有同一Worker容器按预检停启。首轮QA误要求未处置Outbox为0的失败和准确清理保留，第二轮新资源通过，20项探针/范围检查通过。同设备元数据不计异盘灾备；Unknown及退出/撤权/强杀/重启等组合、递归所有者/ACL/删除与其余59项仍待。[证据与未验边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_PARTIAL.md)。

**2026-11-02递归普通权限0dede08已覆盖，真实身份/目录树/HDD常规页面6场景通过**：唯一发布Invocation f396efd8f67f4c0ca9e02a48315dc9f2成功结束，六组件/HTTP及实际宿主进程同源，生产迁移入库、11核心healthy且8数据库平台不重建。冻结85d7d11验收器以正常Authentik登录完成5成功/1执行前取消，含0变更、显式0000/0777、每场景实盘完整字节/九字段/混合属主/mtime/xattr、5聚合/20子项HDD、6登记/5确认原HTTP重试及刷新、匿名/普通拒绝/CSRF/Origin/退出，独立只读核验准确清理通过，23原主体哈希审计/11唯一Job通过。6真实PNG及320×600取消可见性已核验；同设备元数据保留不计异盘灾备，进行中故障及业务Partial/Unknown仍待。发布源码0dede08的CI五路最终全部成功（41402013851），user NOPASSWD保留，不自动回滚；其余59项/递归所有者/ACL/删除继续，17移出不算完成。[实机证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_REAL_UI.md)。

**2026-11-02递归普通权限管理员API/Vue候选2056191与验收器f706c86通过，未部署**：已接原树异步预检/明确文件与目录模式/确认/原引用接续取消、Partial/Unknown计数与HDD历史/观测区分及当前管理员处置，原登录/实时权威身份与写入互锁保留。3794 Unit、308 Integration（7默认原生跳过）、832前端Unit、Release零警告错误及类型/构建/lint/格式通过；生产预览44递归+104关联分批通过，148联合首轮147通过/1个null测试断言失败与修复记录保留，不冒称单次148全绿。小屏/桌面动作可见、过期提示和编辑禁用已修，截图/源码/构建摘要核验通过；测试接口/身份/仓储与Agent夹具不是实机业务证据。宿主仍e668dbe/1.49，应用d1fcca8；下一步正常冻结覆盖与真实Authentik→Vue→SQL/Worker→实际目录树/HDD及故障整链，递归所有者/ACL/删除/长期清理和其余59项继续，17项移出不算完成。[候选与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_WEB_UI.md)。

**2026-11-02递归普通权限永久SQL/八方保护/专用Worker候选cca8ec9通过，未部署**：真实EF迁移/双Job/Outbox/审计、90秒租约、原请求/计划/执行/回执计数与历史不可改、Partial/Unknown保留保护且不自动Claim、原ID明确处置及旧七方双向数据库/路径/预算保护已接。3794 Unit、259 Integration（7默认原生跳过）、Release零警告错误及10项范围/回执门禁通过；冻结cca8ec9双隔离真实PG全套1执行/1通过/0跳过，独立准确清理、原Unknown锁不改、11核心门禁期间不变且healthy、生产候选表不存在。四轮失败/中断保留，Root/身份/时钟为合成输入；不是实际树/HDD/UI证据。宿主仍e668dbe/1.49，应用仍d1fcca8；下一步接管理员API/Vue、实时身份及真实树/HDD页面与故障整链，递归所有者/ACL/删除/长期清理及其余59项继续，17项移出不算完成。[完整证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_PERSISTENCE.md)。

**2026-11-02递归内部业务候选71b6334通过，回执丢失保护已覆盖e668dbe**：独立请求/计划/执行HMAC、确认与实时授权、Partial/Unknown保留计数/保护及原ID处置已实现，尚未EF仓储/迁移/八方保护/Worker/API/Vue。3792 Unit（本批114新）、258 Integration（7默认原生跳过）、405 Linux-root及Release零警告错误通过。修复递归及单对象chmod/chown缺失Root时误报Aborted：先查可信HDD/子项，遗留即Unknown，依赖不可用不作缺失。宿主最终e668dbe/1.49正常覆盖；递归4+chmod6+chown6共16个真实签名操作及各自独立HDD/原件/清理通过。递归准确自有回执实际丢失、原件保留、Discard/迟到Apply Unknown且全树/HDD不变与独立核验通过；单对象对应丢失组合仍需安装态验收。应用容器仍d1fcca8、11核心不变且healthy，59项继续，17项移出不算完成。[候选、修复及边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_WORKFLOW.md)。

**2026-11-02递归普通权限Agent1.49已安装，真实签名RPC通过**：宿主冻结95c0787正常覆盖，真实进程/10.0.13/UDS/权限/HDD/动态入口通过；API/Worker/Gateway仍d1fcca8，11核心容器未重建。原生四RPC在准确HDD树完成4操作（3完成/1执行前取消）、8预检/3执行绑定拒绝，完整2097159字节/原inode/属主/mtime/属性、逐次重放源九字段/Root原件不变、3聚合/12子项HDD与独立准确清理通过。旧单对象权限6操作兼容与独立核验通过，最终任务/终端/锁/屏障与未结束意图预检0、11核心healthy。安装正常重启两宿主，RPC期间PID稳定；未真实人类/永久业务/Worker/API/Vue，仍需完整链、进行中故障、ACL/删除及其余59项，17项移出不算完成。[实机证据及边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_AGENT_RPC.md)。

**2026-11-02递归普通权限签名RPC/SDK候选aead344通过，未安装**：独立完整请求签名与四RPC、强类型DTO/回执计数、能力1.49协商、固定stdin helper/准确Invocation停止及安装/校验已接，未新增HTTP/Vue入口。3678 Unit（93新专项）、258 Integration通过（7默认原生跳过），33递归/395关联Linux-root零跳过、12宿主artifact/8范围与Release零警告错误通过。复核补SIGTERM逐项取消与Unknown正计数，真实子进程信号后Partial不续做通过；非真实UDS/身份/实盘签名证据。运行仍最后记录d1fcca8/1.48，25281a1内部实盘保持原批次；继续宿主覆盖/原生RPC、永久业务/Worker/API/Vue及真实身份树/HDD全链、ACL/删除和其余59项，17项移出不算完成。[候选与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_RPC.md)。

**2026-11-02递归普通权限内部候选25281a1通过真实HDD**：有界乐观清单/独立HDD聚合与子项历史、最深项先执行、逐项永久回执和Partial/Unknown只观察恢复已实现。20专项/382关联Linux-root零跳过、8范围与语法检查通过；冻结独占容器4操作（2完成/1部分/1中止）、2次候选真实SIGKILL、每文件完整2097159字节及原inode/属主/mtime/属性、4份聚合/9份子项HDD和迟到重放不变均通过，独立复核准确清理/11核心不变且healthy。失败与修复保留。未安装/helper/RPC/永久业务/Worker/API/Vue，不算真实身份页面或完整递归权限；运行仍d1fcca8/1.48，继续全链/更多故障、递归所有者/ACL/删除及其余59项，17移出不算完成。[证据与边界](qa/CONTAINER_RECURSIVE_PERMISSIONS_ENGINE.md)。

2026-11-02新增所有者实机证据：在真实fchown已发生、原Root仍Applying/未确认的切点进行页面取消与Worker强杀，两例均按原操作恢复Succeeded/Completed；原自然租约、完整内容/同inode/目标属主/属性/HDD与迟到Apply不重做及独立清理通过。[变更后证据](qa/CONTAINER_OWNERSHIP_POST_CHANGE.md)。仅QA与记录变化，产品仍d1fcca8/1.48；其余回执/变更后组合、目录/跨盘和递归/ACL/删除继续。

2026-11-02所有者实时权威身份已在d1fcca8覆盖：真实Authentik组移除与账号停用在原正快照仍有效时停止执行，实际403/登录页面、SQL/Root/HDD/全部字节和独立清理通过；同版本常规7场景也重跑通过。其他模块即时撤权/全部BFF注销尚不能据此签收。[最新实机证据](qa/CONTAINER_OWNERSHIP_REVOCATION.md)。

当前API/Worker/Gateway/Migrator与Agent/MaintenanceHost均为447070e/协议1.51，ACL迁移保持原633a2ca版本；此前612f77c的常规摘要/写后Agent记录保留，本批447070e小屏修复及写后Unknown/明确处置摘要已签收，Agent3947643、维护3947551；实际来源与未验边界见本节最新记录；以下为之前批次：`0dede08`/协议1.49，递归权限生产迁移已应用并通过6场景真实常规页面。数据库平台8个容器和依赖版本未重建，WhaleDeck自身schema已正常前向升级。前批所有者故障/小屏记录使用d5e62ca/e91a770。2026-10-31所有者真实页面四类中断（取消/退出/Worker强杀自然租约/Agent重启）与Unknown不同管理员处置、等待目录保护/迟到Apply不重做、独立SQL/Root/HDD/原文件和准确清理通过；修复Unknown小屏确认按钮后已覆盖并实际重验，记录于2026-11-02补齐。[完整证据及未验边界](qa/CONTAINER_OWNERSHIP_FAULTS.md)。d5e62ca常规7场景证据独立保留；所有者真实组撤权/账户停用已补实机；变更后取消/Worker回执中断已补实机；仍需其他变更后/回执组合及递归/ACL/删除，不计完整文件模块完成，不把旧健康当当日复跑。

2026-10-30用户已通过`[Goal]`重新启用本目标执行。前批deef721新增[所有者内部业务候选](qa/CONTAINER_OWNERSHIP_WORKFLOW.md)：业务签名/原登录状态机已通过3579全Unit与Release；SQL/七方保护/Worker注册/API/UI尚未接通，不能算完成永久业务或新部署。

最新安装来源与常规所有者实机证据见本节对应实机记录；生产迁移/管理员API/Vue、四类进行中中断和Unknown实机已接通；剩余其他变更后/回执等故障、递归/ACL/可恢复删除、长期清理及其余主项仍需完成；原始候选与安装批次保留在对应QA中。

UI风格、C#酒馆/统一供应商/Live2D均为待实现子计划，按DEV-UI-001..004与DEV-AI-001..008衔接；272ST/6PET及16子门槛没有新增功能验收。完整历史日志保留在本文附录，当前目标不从历史70/76项或旧HOLD恢复范围。

## 4. P0：控制面可靠性与安全闭环

依赖：现有运行时。先消除正确性与数据安全缺口，再扩展高权限能力。

| ID | 缺失工作/交付 | 当前状态 | 必须证明的结果 |
| --- | --- | --- | --- |
| P0-01 | 登录、注销、会话续期/撤销、OIDC 深链接与 Cookie 上限 | 部分实现 | Authentik/面板完整退出；门户直达、循环登录和超大 Cookie 回归；失效权限清空界面；递归首项实际修改后正常双端退出、账号/容器实例保留且Partial不续做、旧界面清空及不同管理员处置已通过[本批实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_LOGOUT.md)，其他模块/组合继续 |
| P0-02 | 权限 2 小时复核、紧急撤权、队列/Agent 签发二次授权 | 部分实现 | 匿名/普通/管理员、越权/跨资源、缓存到期/身份服务故障均有真实证据；递归首项实际修改后组撤权/账号停用已在原快照未过期时真实停止，旧API/页面拒绝且Partial/HDD保留，见[本批证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_REVOCATION.md)，其他模块与组合仍需闭环；8d93fbe已补ACL写后移组/停用原快照未过期时真实停止与403/页面拒绝，历史Completed事实保留，其他模块仍待，见[写后授权边界](qa/CONTAINER_ACL_POST_REVOCATION.md) |
| P0-03 | Job/Outbox/锁/租约/取消/SSE 续传/断线与进程重启恢复 | 部分实现 | 不重复执行、不丢终态、取消与完成竞态稳定；同一原任务接续。8d77293已部署五类当前管理员独立计划/确认/Worker/审计/WebUI；真实删除原账号后的目录移动Unknown接管、原回执/原件不变和原锁释放通过，五类真实SQL含审计失败/锁等待租约边界通过；递归Worker强杀原自然租约40次实际观测及Partial页面/不同管理员处置也已通过，见[原租约实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_WORKER_CRASH.md)；递归目标消失Unknown/另一管理员页面处置与原事实保留已通过，见[Unknown实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN.md)；递归Unknown期间跨容器目录4次实际延期及明确处置后原Job发布也已通过，见[跨写入实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_UNKNOWN_BARRIER.md)；其余物理故障组合继续，不伪造旧登录或改写Root Unknown；0b7d43c真实不同当前管理员任务中心核对原ACL执行ID/Succeeded、管理写0及原SQL不变通过；已发现ACL任务resultJson为空仍显示成功后占位文案，该运行版缺口见[本批事实](qa/CONTAINER_ACL_POST_REVOCATION.md)；后续2491add已补[安全摘要候选](qa/CONTAINER_ACL_JOB_RESULTS.md)，后续612f77c已签收常规17新摘要/18详情页面及独立原件复核，见[实际签收范围](qa/CONTAINER_ACL_JOB_RESULTS_REAL_UI.md)，Unknown/处置摘要及其他故障仍待；47a1769后续已在612f77c签收ACL写后Agent真实重启/维护153次HTTP、原任务Completed摘要与原件保持，见[本批实机证据](qa/CONTAINER_ACL_POST_AGENT_RESTART.md)，其他故障仍待；后续447070e已签收写后目标消失Unknown→明确处置两摘要真实页面、原事实/HDD保持与跨容器目录四次延期释放；前两轮小屏失败及CSS修复保留，其余组合继续，见[本批证据](qa/CONTAINER_ACL_POST_UNKNOWN.md)；后续d2ca69d冻结QA在447070e实机签收写后同CID新实例Unknown/null、不同管理员明确处置与两次真实摘要、四次跨容器目录延期后发布；首轮断言失败保留，其他组合继续，见[实例变化证据](qa/CONTAINER_ACL_INSTANCE_CHANGE.md)；后续caa9c64已修正预检失败摘要并准备独占旧版本真实门禁，候选未部署/未真实验收，见[候选边界](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)；306f646后续已覆盖并实机签收旧版本预检Failed/无执行历史及两种真实摘要、权限拒绝/退出/独立清理，剩余组合继续，见[实际证据](qa/CONTAINER_ACL_STALE_PREFLIGHT.md) |
| P0-04 | 应用归档强杀/异常恢复、共享资源/失效指纹/坏归档与坏 journal | 部分实现、待验收 | 原容器解除本任务暂停、不重建、不触碰人工暂停/其他项目；partial 精确清理；坏 journal 不阻断其他恢复 |
| P0-05 | Docker 重启/错误配置/Agent 离线与核心升级维护页 | 部分实现 | 独立维护入口持续可用、等待状态真实、普通操作不能停止核心；错误预检阻断；0dede08正常覆盖/动态入口已核验，进行中Agent重启时维护PID不变且17次本机HTTP正常已补[实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_AGENT_RESTART.md)，其他入口/切点继续；旧previous-revision曾误记新HEAD，134fd25实际权威账本与正常升级已证明current/previous修复，真实手动恢复/强杀/冷启动仍不能据此补签；47a1769后续已在612f77c签收ACL写后Agent真实重启/维护153次HTTP、原任务Completed摘要与原件保持，见[本批实机证据](qa/CONTAINER_ACL_POST_AGENT_RESTART.md)，其他故障仍待；463e19e/1811bd3已实现可信发布记录/失败重试/手动目标选择与原子状态候选，30隔离/11构建/5安装及六组件只读探测通过，生产工具/账本尚未安装或创建，强杀/冷启动入口及真实新发布继续，见[本批候选](qa/PLATFORM_REVISION_HISTORY.md)；1176084/4eb4d15持久控制入口恢复候选已通过47相关/两隔离systemd场景，含实际SIGKILL、影子易失目录重建及11核心身份保持；未安装或验生产发布/物理冷启动，见[控制入口恢复候选](qa/PLATFORM_CONTROL_RECOVERY.md)；134fd25已真实安装/唯一正常覆盖，账本current134fd25/previous306f646、旧错误原件legacy、11核心/8数据库/配置sudo与47相关通过，身份文件HDD页面/手动恢复/冷启动未验，见[实际安装签收](qa/PLATFORM_CONTROL_INSTALLATION.md)；0478262真实确认框中文/取消关闭与长计划已修复并唯一覆盖，QA37dbe58管理员/普通Auth及正常双端退出11PNG独立通过，执行POST0，current0478262/previous134fd25；不补签安装态中断/手动回退/冷启动，见[真实维护UI](qa/PLATFORM_MAINTENANCE_REAL_UI.md) |
| P0-06 | Secret 生命周期、脱敏、下载/日志/诊断包预算 | 部分实现 | 仓库/日志/审计/浏览器均无 Secret；一次性 Secret 原子消费；输出有界 |
| P0-07 | 指标采样/聚合/保留、容量/负载/服务告警和通知状态 | 部分实现 | 6 秒采集、14 天保留、持久 1 小时 rollup/first/last、告警升级/静默/恢复正确 |
| P0-08 | 全页状态、紧凑卡片视觉、响应式/字体/主题/可访问性/动作截断规范 | 部分实现 | 所有页正常操作及空/503/403/过期；320–1920px 明暗主题无页面横向溢出；长文本省略+完整提示或有界换行；按[前端视觉规范](UI_STYLE_GUIDE.md)分批完成大数字+小趋势图指标卡、汇总/折叠变更卡与明暗一致性，保留真实数据及确认/失败事实 |
| P0-09 | 安装/更新源测速、镜像加速选择、离线/USB 可达、日志预算 | 部分实现 | 新机与重复执行幂等；固定依赖命名；版本锁定；不存在双重挂载数据库 |
| P0-10 | 逐项证据账本与合并/发布门禁 | 部分实现 | 已有Release/分析器/迁移/实际依赖/UI门禁和逐批次实机账本；b118454六Linux组件实际程序集/镜像来源、HTTP与真实版本卡已签收。仍需全部功能逐项映射、完整新机/离线和正式交付门禁，不以模拟证据冒充原生 |

P0-08前端视觉改造已于2026-10-30纳入当前计划，状态为**待实施、待真实页面验收**：DEV-UI-001共享卡片语言→DEV-UI-002指标卡→DEV-UI-003任务/变更摘要与当前P1/P2页面→DEV-UI-004全页回归，详见[视觉规范与验收](UI_STYLE_GUIDE.md)。当前所有者等业务继续推进，新增页面按此规范实现；既有页面分批迁移，不增加主任务或原型审批阶段，59项总数不变。

P0-07 指标子项 2026-10-13 在运行时 `3b5b3d9` 已验证：336 小时/8413 rollup 完成回填、first/last 与 IOPS 权重、原始 4133024 样本保留、迟到并发与迁移、276 万冷查询 12.491s、生产 7d 2.951s、权限/界面通过，见 [聚合验收](qa/METRIC_AGGREGATION.md)。第一轮大范围原始回退超时已修，保留失败历史；模块整体仍部分实现，不能据此跳过告警/日志压力与其余 76 项。

## 5. P1：完整单机容器与应用管理（1Panel 操作体验）

依赖：P0 控制面、权限、任务和维护能力。

| ID | 缺失工作/交付 | 当前状态 | 必须证明的结果 |
| --- | --- | --- | --- |
| P1-01 | 容器概览/卡片/表格/筛选/搜索/分页/批量启停删除 | 部分实现 | 状态决定可用动作；核心只读；批量结果逐资源可追踪、不隐藏失败 |
| P1-02 | 完整创建/复制/导入：环境/端口/挂载/网络/资源/健康/重启/设备 | 部分实现 | 原生配置计划、校验、敏感输入票据、差异确认；危险配置显式授权 |
| P1-03 | 容器详情/日志/持续统计/关联图/事件 | 部分实现 | 长镜像/动作不截一半；详情可定位镜像/卷/网络/应用；流式日志限速 |
| P1-04 | 镜像列表、拉取/推送/标签/构建、更新检查、缓存和安全清理 | 部分实现 | 摘要/来源/使用关系、构建过程与失败、在用/保留镜像保护，清理预检 |
| P1-05 | Registry 配置/凭据/连通性和加速源管理 | 部分实现 | 凭据不回显；失败不覆盖有效配置；保存重启状态正确 |
| P1-06 | 网络/卷创建、编辑适用字段、删除、容量/使用/依赖图 | 部分实现 | 在用/共享/受保护资源不可误删；端点关系精确；macvlan/ipvlan 预检与受控配置 |
| P1-07 | Compose 编辑/校验/差异/模板/导入/环境/服务生命周期 | 部分实现 | 字段级 diff、源冲突、单次计划、服务级进度、Secret 脱敏 |
| P1-08 | 完整应用市场（分页/分类/版本/来源/搜索/本地模板） | 部分实现 | Top 6 仅首页推荐；Worker 获取轩辕快照与来源/过期；市场完整可安装目录 |
| P1-09 | 高级安装表单、参数/备份/升级/重装/卸载/保留数据 | 部分实现 | OCI 元数据与受控模板生成表单；绑定资源/依赖/端口冲突；安装后原生配置匹配 |
| P1-10 | autoupdate 约束、更新历史、窗口/预检/备份与手动回退 | 部分实现 | 未标签/核心不自动更新；失败保留诊断，普通应用恢复/回退有真实演练 |
| P1-11 | 主机与容器终端、Exec、文件传输相关会话 | 部分实现 | 主机/容器终端、主机文件操作及真实角色/会话证据保留；容器文件浏览、原生下载/上传、UTF-8编辑、HDD历史内容及普通4属性恢复，已接独立实例/计划/永久任务/Worker/审计/UI，并有真实管理员与普通用户拒绝、跨页退出及2e13376三种重启证据。容器新建目录已有正常业务/UI实机证据；复制常规整链已签收；移动Agent/永久SQL/Worker/API/Vue已覆盖并通过真实身份常规移动与页面，文件部分写入取消/退出及Worker/Agent重启通过，目录目标发布后部分源留存Unknown跨管理员页面及晚到Apply栅栏已实机签收，其余交换/撤权/跨盘等故障继续。普通权限cb4c400已覆盖并通过真实身份/文件/HDD常规页面与取消/退出/Worker强杀/Agent重启/Unknown跨管理员处置，见[权限实机证据](qa/CONTAINER_PERMISSION_FAULTS.md)；所有者引擎与2a9ceb6/1.48安装态RPC已验证，永久SQL/七方保护/专用Worker/API/Vue已在d5e62ca覆盖并通过真实身份/文件/HDD常规页面7场景与独立清理，见[实机边界](qa/CONTAINER_OWNERSHIP_REAL_UI.md)；所有者四类进行中中断/Unknown已实机通过，实时组撤权/停用也已实机通过，变更后取消/Worker回执中断也已实机对账，其他组合与递归/ACL/删除继续，见[故障证据](qa/CONTAINER_OWNERSHIP_FAULTS.md)；容器删除及完整权限等写入、目录业务故障、个人根/共享根/容器资源完整隔离与长期清理仍需闭环；不恢复已移出的企业租户体系。见[容器恢复](qa/CONTAINER_FILE_RESTORATION.md)与[进程重启](qa/CONTAINER_RESTORATION_RESTARTS.md)；递归普通权限首项实际修改后的真实组撤权/账号停用与Partial保留、不同管理员明确处置已通过，见[撤权实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_REVOCATION.md)；原实例保留时正常双端退出及Partial五计数页面处置也已通过[实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_LOGOUT.md)；递归所有者底层/HDD、1.50签名RPC与22906f5安装态四RPC/旧协议兼容已分批通过，见[安装证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)；7a94733内部编排后续已在a19f5c4接永久EF/迁移/九方双向写入保护/专用Worker，并通过双隔离真实PG及独立清理；后续b1fafed已覆盖且真实页面5成功/1预检取消、源树/HDD及准确清理分项签收；原整套退出检查Failed原文保留；新51e0ec3两套资源已签收首项修改后真实页面取消与原身份/实例保留时正常双端退出、Partial五计数/不同管理员处置及独立清理，后续0442fb4 Worker真实强杀/自然租约和Agent实际重启/维护17次观测也已签收；cf291aa两套新资源首项fchown后实时组撤权/账号停用及另一管理员五计数页面处置、原事实保留与独立清理也已实机通过，后续003f504目标消失Unknown及a6f472b另一容器目录四次自然延期/明确处置后原ID实际发布、真实页面和独立清理也已签收，余其他写入/回执/路径磁盘故障、ACL/删除继续，见[最新Unknown与跨写入边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_UNKNOWN_BARRIER.md)；；ACL底层与协议候选后续已在4a58961正常宿主覆盖，1.51四RPC实际签名9操作(8完成/1中止)/8HDD、13预检及4执行绑定拒绝和各自重放/迟到不改、独立准确清理通过，旧权限/所有者能力兼容也已签收；仍缺永久ACL业务/共享保护/Worker/API/Vue及真人页面、完整递归/恢复和故障，不计完整ACL，见[ACL安装态实际事实](qa/CONTAINER_ACL_AGENT_RPC.md)；41f02fb后续已实现内部登录绑定ACL编排/分域回执MAC及137项新增边界测试，仍无具体永久仓储/共享保护/Worker/页面，见[内部候选](qa/CONTAINER_ACL_WORKFLOW.md)；后续72d678e已接具体ACL SQL/17 CHECK/十方保护/专用Worker，双隔离真实PG与独立清理通过，c9c9265任务入口已加固；未部署、管理员API/Vue及真人业务链仍待，见[持久化候选](qa/CONTAINER_ACL_PERSISTENCE.md)；062e361后续已接ACL管理员API/Vue、严格原条目/mask/三态与原引用处置，432集成/972前端单元及43+156两组生产预览通过；未部署、真实身份ACL文件/HDD常规及故障链仍待，见[页面候选](qa/CONTAINER_ACL_WEB_UI.md)；ACL后续633a2ca已正常覆盖/前向迁移，并以0668215真实页面九场景(8成功/1预检取消)、同上下文退出、8HDD/完整源/35审计及独立准确清理通过；仍须进行中故障/Unknown/跨写入、ACL递归/恢复与删除，不计完整权限，见[实际边界](qa/CONTAINER_ACL_REAL_UI.md)；后续78479ec两新资源的ACL执行前已留历史进行中取消/正常退出与原件/独立清理通过，剩余变更后中断/撤权/重启/Unknown/递归恢复/删除继续，见[ACL故障实机证据](qa/CONTAINER_ACL_FAULTS.md)；f3ca007后续两新资源在原正快照余7164秒以上实时移组/停用，真实拒绝页面、原ACL/2HDD保留及独立清理已通过；剩余重启/变更后/Unknown/递归恢复/删除继续，见[ACL实时撤权](qa/CONTAINER_ACL_REVOCATION.md)；后续ed241e9在写ACL前Worker强杀/原自然租约42采样与Agent重启/维护152采样、Failed/Aborted原件/独立清理通过；变更后/Unknown/递归恢复/删除等继续，见[ACL重启实机证据](qa/CONTAINER_ACL_RESTARTS.md)；517c42a后续已实际签收ACL写后取消与Worker回执中断，原任务Succeeded/Completed、原HDD/完整写后清单及迟到Apply不重写通过；剩余Unknown/其他写后组合/递归恢复/删除继续，见[ACL写后实机证据](qa/CONTAINER_ACL_POST_CHANGE.md)；d977c7a后续ACL原容器消失Unknown、不同管理员小屏明确处置与另一容器目录四次自然延期/释放后同ID发布已严格实机签收，首轮计数/视口缺口拒绝及修复原件保留；其他Unknown/写后组合/递归恢复/删除继续，见[ACL Unknown证据](qa/CONTAINER_ACL_UNKNOWN.md)；8d93fbe后续真实ACL写后移组/停用已签收，原用户403拒绝、既有事实Succeeded/Completed/原HDD与迟到重放保持，不同当前管理员只读API及独立核验通过；后续0b7d43c任务中心原ID/Succeeded只读页面通过，完整结果摘要与其他组合/递归恢复/删除继续，见[ACL写后撤权](qa/CONTAINER_ACL_POST_REVOCATION.md)；后续612f77c常规17新任务安全摘要/18实际详情与原SQL/Root/HDD已通过，Unknown/处置摘要及其他故障仍待，见[实际摘要证据](qa/CONTAINER_ACL_JOB_RESULTS_REAL_UI.md)；ACL写后Agent重启与两原Job摘要320×600真实详情已签收，剩余Unknown/实例/跨写入、递归恢复与删除继续，见[本批实机证据](qa/CONTAINER_ACL_POST_AGENT_RESTART.md)；后续447070e已签收写后目标消失Unknown→明确处置两摘要真实页面、原事实/HDD保持与跨容器目录四次延期释放；前两轮小屏失败及CSS修复保留，其余组合继续，见[本批证据](qa/CONTAINER_ACL_POST_UNKNOWN.md)；后续d2ca69d冻结QA在447070e实机签收写后同CID新实例Unknown/null、不同管理员明确处置与两次真实摘要、四次跨容器目录延期后发布；首轮断言失败保留，其他组合继续，见[实例变化证据](qa/CONTAINER_ACL_INSTANCE_CHANGE.md)；后续caa9c64已修正预检失败摘要并准备独占旧版本真实门禁，候选未部署/未真实验收，见[候选边界](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)；306f646后续已覆盖并实机签收旧版本预检Failed/无执行历史及两种真实摘要、权限拒绝/退出/独立清理，剩余组合继续，见[实际证据](qa/CONTAINER_ACL_STALE_PREFLIGHT.md) |
| P1-12 | Compose/应用 Git 同步、Webhook、配置版本和差异 | 部分实现 | 固定仓库范围、签名 webhook、冲突提示、敏感扫描、任务锁和变更证据 |

## 6. P2：系统、文件、数据库与完整备份/恢复

最新文件子范围（截至2e13376）：主机目录覆盖恢复与目录历史组件清理已分别有真实BFF/永久业务及常规页面证据；容器浏览/传输/编辑/历史普通属性恢复和三种真实进程重启已验证。下表按仍缺失的容器写入、完整隔离、长期清理及故障矩阵继续，不再把已接通的SQL/API/UI当未实现。各历史批次和失败证据保留于对应qa记录，P2-03/P2-04仍不签完整。

依赖：P0/P1；恢复和网络变更默认计划+管理员二次确认。

| ID | 缺失工作/交付 | 当前状态 | 必须证明的结果 |
| --- | --- | --- | --- |
| P2-01 | 主机/进程/服务/用户/SSH/时区/更新/重启管理 | 部分实现 | 进程 PID/开始时间绑定、受保护服务防误杀、操作计划和安全提示 |
| P2-02 | 存储/挂载/SMART/大目录扫描/清理建议 | 部分实现 | NVMe/HDD 区分、容量与占用真实、扫描预算、清理范围/确认、数据卷保护 |
| P2-03 | 文件列表/搜索/上传下载/编辑/复制移动/权限/压缩解压 | 部分实现 | 主机普通文件与目录的浏览/名称及正文搜索、上传下载/UTF-8编辑、复制移动、普通chmod、TAR/TAR.GZ压缩解压、回收/历史恢复/目录覆盖恢复均有分批实际API/Agent/HDD与页面证据；e46ef29目录历史组件清理已接永久SQL/墓碑/任务/真人权限及常规UI，不再列作未接业务。容器文件已实际签收浏览/传输/编辑/历史普通属性恢复及重启；容器新建目录已接永久业务/API/Worker/Vue并有实际四组页面证据；复制常规整链已签收，移动Agent/永久SQL/Worker/API/Vue已覆盖并通过真实身份常规移动与页面，文件部分写入取消/退出及Worker/Agent重启通过，目录目标发布后部分源留存Unknown跨管理员页面及晚到Apply栅栏已实机签收，其余交换/撤权/跨盘等故障继续；普通权限cb4c400已覆盖并通过真实身份/文件/HDD常规页面与取消/退出/Worker强杀/Agent重启/Unknown跨管理员处置，见[权限实机证据](qa/CONTAINER_PERMISSION_FAULTS.md)；所有者引擎与2a9ceb6/1.48安装态RPC已验证，永久SQL/七方保护/专用Worker/API/Vue已在d5e62ca覆盖并通过真实身份/文件/HDD常规页面7场景与独立清理，见[实机边界](qa/CONTAINER_OWNERSHIP_REAL_UI.md)；所有者四类进行中中断/Unknown已实机通过，实时组撤权/停用也已实机通过，变更后取消/Worker回执中断也已实机对账，其他组合与递归/ACL/删除继续，见[故障证据](qa/CONTAINER_OWNERSHIP_FAULTS.md)；仍需容器删除/递归与ACL等完整权限、所有者剩余故障、目录业务实际故障、完整用户/资源隔离、长期清理及剩余故障矩阵。递归普通权限0dede08已通过常规6场景、进行中取消Partial不同管理员处置、Worker强杀原自然租约/真实页面恢复、首项修改后Agent重启Partial及目标消失Unknown不同管理员处置/跨容器目录实际延期与释放后原任务执行，其余写入与故障组合仍待，见[递归Partial证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_PARTIAL.md)。完整属性ACL/xattr/特殊位/atime仍按当前设计单独跟踪，普通属性验收不代表完整文件功能完成。见[目录清理BFF](qa/FILE_TREE_PURGING_BFF.md)、[页面](qa/FILE_TREE_PURGING_REAL_UI.md)与[容器恢复](qa/CONTAINER_FILE_RESTORATION.md)；递归普通权限首项实际修改后的真实组撤权/账号停用与Partial保留、不同管理员明确处置已通过，见[撤权实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_REVOCATION.md)；原实例保留时正常双端退出及Partial五计数页面处置也已通过[实机证据](qa/CONTAINER_RECURSIVE_PERMISSIONS_LOGOUT.md)；递归所有者底层/HDD、1.50签名RPC与22906f5安装态四RPC/旧协议兼容已分批通过，见[安装证据](qa/CONTAINER_RECURSIVE_OWNERSHIP_AGENT_RPC.md)；7a94733内部编排后续已在a19f5c4接永久EF/迁移/九方双向写入保护/专用Worker，并通过双隔离真实PG及独立清理；后续b1fafed已覆盖且真实页面5成功/1预检取消、源树/HDD及准确清理分项签收；原整套退出检查Failed原文保留；新51e0ec3两套资源已签收首项修改后真实页面取消与原身份/实例保留时正常双端退出、Partial五计数/不同管理员处置及独立清理，后续0442fb4 Worker真实强杀/自然租约和Agent实际重启/维护17次观测也已签收；cf291aa两套新资源首项fchown后实时组撤权/账号停用及另一管理员五计数页面处置、原事实保留与独立清理也已实机通过，后续003f504目标消失Unknown及a6f472b另一容器目录四次自然延期/明确处置后原ID实际发布、真实页面和独立清理也已签收，余其他写入/回执/路径磁盘故障、ACL/删除继续，见[最新Unknown与跨写入边界](qa/CONTAINER_RECURSIVE_OWNERSHIP_UNKNOWN_BARRIER.md)；；ACL底层与协议候选后续已在4a58961正常宿主覆盖，1.51四RPC实际签名9操作(8完成/1中止)/8HDD、13预检及4执行绑定拒绝和各自重放/迟到不改、独立准确清理通过，旧权限/所有者能力兼容也已签收；仍缺永久ACL业务/共享保护/Worker/API/Vue及真人页面、完整递归/恢复和故障，不计完整ACL，见[ACL安装态实际事实](qa/CONTAINER_ACL_AGENT_RPC.md)；41f02fb后续已实现内部登录绑定ACL编排/分域回执MAC及137项新增边界测试，仍无具体永久仓储/共享保护/Worker/页面，见[内部候选](qa/CONTAINER_ACL_WORKFLOW.md)；后续72d678e已接具体ACL SQL/17 CHECK/十方保护/专用Worker，双隔离真实PG与独立清理通过，c9c9265任务入口已加固；未部署、管理员API/Vue及真人业务链仍待，见[持久化候选](qa/CONTAINER_ACL_PERSISTENCE.md)；062e361后续已接ACL管理员API/Vue、严格原条目/mask/三态与原引用处置，432集成/972前端单元及43+156两组生产预览通过；未部署、真实身份ACL文件/HDD常规及故障链仍待，见[页面候选](qa/CONTAINER_ACL_WEB_UI.md)；ACL后续633a2ca已正常覆盖/前向迁移，并以0668215真实页面九场景(8成功/1预检取消)、同上下文退出、8HDD/完整源/35审计及独立准确清理通过；仍须进行中故障/Unknown/跨写入、ACL递归/恢复与删除，不计完整权限，见[实际边界](qa/CONTAINER_ACL_REAL_UI.md)；后续78479ec两新资源的ACL执行前已留历史进行中取消/正常退出与原件/独立清理通过，剩余变更后中断/撤权/重启/Unknown/递归恢复/删除继续，见[ACL故障实机证据](qa/CONTAINER_ACL_FAULTS.md)；f3ca007后续两新资源在原正快照余7164秒以上实时移组/停用，真实拒绝页面、原ACL/2HDD保留及独立清理已通过；剩余重启/变更后/Unknown/递归恢复/删除继续，见[ACL实时撤权](qa/CONTAINER_ACL_REVOCATION.md)；后续ed241e9在写ACL前Worker强杀/原自然租约42采样与Agent重启/维护152采样、Failed/Aborted原件/独立清理通过；变更后/Unknown/递归恢复/删除等继续，见[ACL重启实机证据](qa/CONTAINER_ACL_RESTARTS.md)；517c42a后续已实际签收ACL写后取消与Worker回执中断，原任务Succeeded/Completed、原HDD/完整写后清单及迟到Apply不重写通过；剩余Unknown/其他写后组合/递归恢复/删除继续，见[ACL写后实机证据](qa/CONTAINER_ACL_POST_CHANGE.md)；d977c7a后续ACL原容器消失Unknown、不同管理员小屏明确处置与另一容器目录四次自然延期/释放后同ID发布已严格实机签收，首轮计数/视口缺口拒绝及修复原件保留；其他Unknown/写后组合/递归恢复/删除继续，见[ACL Unknown证据](qa/CONTAINER_ACL_UNKNOWN.md)；8d93fbe后续真实ACL写后移组/停用已签收，原用户403拒绝、既有事实Succeeded/Completed/原HDD与迟到重放保持，不同当前管理员只读API及独立核验通过；后续0b7d43c任务中心原ID/Succeeded只读页面通过，完整结果摘要与其他组合/递归恢复/删除继续，见[ACL写后撤权](qa/CONTAINER_ACL_POST_REVOCATION.md)；后续612f77c常规17新任务安全摘要/18实际详情与原SQL/Root/HDD已通过，Unknown/处置摘要及其他故障仍待，见[实际摘要证据](qa/CONTAINER_ACL_JOB_RESULTS_REAL_UI.md)；ACL写后Agent重启与两原Job摘要320×600真实详情已签收，剩余Unknown/实例/跨写入、递归恢复与删除继续，见[本批实机证据](qa/CONTAINER_ACL_POST_AGENT_RESTART.md)；后续447070e已签收写后目标消失Unknown→明确处置两摘要真实页面、原事实/HDD保持与跨容器目录四次延期释放；前两轮小屏失败及CSS修复保留，其余组合继续，见[本批证据](qa/CONTAINER_ACL_POST_UNKNOWN.md)；后续d2ca69d冻结QA在447070e实机签收写后同CID新实例Unknown/null、不同管理员明确处置与两次真实摘要、四次跨容器目录延期后发布；首轮断言失败保留，其他组合继续，见[实例变化证据](qa/CONTAINER_ACL_INSTANCE_CHANGE.md)；后续caa9c64已修正预检失败摘要并准备独占旧版本真实门禁，候选未部署/未真实验收，见[候选边界](qa/CONTAINER_ACL_STALE_PREFLIGHT.md)；306f646后续已覆盖并实机签收旧版本预检Failed/无执行历史及两种真实摘要、权限拒绝/退出/独立清理，剩余组合继续，见[实际证据](qa/CONTAINER_ACL_STALE_PREFLIGHT.md) |
| P2-04 | 文件回收站、版本留存与彻底清理 | 部分实现 | 普通文件/目录回收、原路径与其他路径恢复、覆盖前备份、目录历史组件分范围清理已具永久业务与实机证据；e46ef29两类捕获×Archive/Originals/All真实BFF及Trash-All明暗/手机UI通过，不能以此代替全部故障/网页矩阵。容器历史恢复与普通属性/独立管理员封存已签收；仍需其回收与彻底清理、长期私有/孤儿/过期对象策略及其故障恢复、个人根/共享根/资源隔离与保留矩阵。HDD同设备时仅版本留存，不计异盘灾备；受保护配置不由通用入口绕过。见[目录清理证据](qa/FILE_TREE_PURGING_REAL_UI.md)及[容器历史恢复](qa/CONTAINER_FILE_RESTORATION.md) |
| P2-05 | 六类数据库/缓存的实例/库/账户/角色/权限/连接/轮换 | 部分实现 | PostgreSQL/MariaDB/SQL Server/MongoDB/Valkey 9/7.2 原生临时资源闭环及清理 |
| P2-06 | 单实例数据库配置编辑/差异/健康、SQL/管理工具入口 | 部分实现 | 原生校验、会话限权、凭据短时、工具内部路由；不做主从集群、自动选主或高可用切换 |
| P2-07 | 备份中心：数据库/应用/站点/平台策略、并发、保留与校验 | 部分实现 | 范围明确、原生一致性与文件系统一致性区分、容量阈值、完整历史及取消 |
| P2-08 | 六类数据库恢复、应用/站点恢复、导入/下载 | 待实现 | 先隔离恢复/验证，目标覆盖计划与数据风险确认；错误/取消不破坏现有数据；受保护依赖专用流程 |
| P2-10 | 系统迁移/平台快照/灾难恢复与定期恢复演练 | 部分实现 | 空环境恢复配置/数据库/Secret 引用，所有依赖启动、原始 ID/权限与数据可用 |
| P2-11 | 防火墙/端口/路由/DNS/NIC/网络配置写入 | 待实现 | IP/USB 变更保持维护入口；限时确认、断线保护、冲突/权限预检；不要求企业路由器控制权 |
| P2-12 | 定时任务模板/自定义受控脚本/执行日志、外部通知和消息中心 | 部分实现 | 幂等租约、补跑/错过策略、Cron 时区、消息已读/历史、失败重试及投递去重 |

## 7. P3：网站托管、运行环境、WebOps 与开发空间

依赖：P1 应用/Compose + P2 文件/备份/网络 + 新网站资源权限。

| ID | 缺失工作/交付 | 当前状态 | 必须证明的结果 |
| --- | --- | --- | --- |
| P3-01 | 网站中心：静态、运行环境、反向代理、转发/停用/删除 | 待实现 | 站点不是门户链接；独立资源/目录/配置/任务/状态，IP+端口与域名路由均可用 |
| P3-02 | 域名/别名/子域名、路由冲突、站点端口和根目录 | 待实现 | Host 路由与默认站点明确；合法 DNS 配置后可访问；不以 .local 子域名必然解析为假设 |
| P3-03 | 实用证书中心：导入/自签/标准ACME、续期/到期告警 | 待实现 | 私钥不泄漏、挑战与端口预检、原子替换；DNS-01仅适配实际选择的方式，不做全厂商管理；无授权域名时不擅自改现有HTTP入口 |
| P3-04 | OpenResty/Nginx配置、代理/缓存/静态/WS/HTTP2与基础访问控制 | 部分实现 | 原生配置测试、字段diff、热加载、失败不污染有效站点；长连接、基本访问控制和限流真实可用，不建设独立企业WAF |
| P3-05 | .NET/C#、Node/Vue、Python运行环境与受控OCI模板 | 待实现 | 独立版本/依赖/启动配置、端口冲突、CPU/内存与Secret；三类内置示例原生验收。PHP/Java/Go等按锁定模板扩展，不做全版本扩展市场 |
| P3-07 | Git 部署、签名 Webhook、预览/暂存、发布历史 | 待实现 | 仓库权限/凭据隔离、并发锁、构建失败不覆盖线上资源、版本与站点绑定 |
| P3-09 | 站点日志/搜索/访问统计/流量/资源配额/可用性 | 待实现 | 有界查询/脱敏、真实请求指标、按站点统计、告警与资源关联 |
| P3-12 | 站点/应用级备份恢复与迁移导入 | 待实现 | 站点配置+文件+数据库一致性集，隔离验证和跨空环境恢复 |
| P3-13 | 开发空间、组合式多语言环境与可重复模板 | 待实现 | 同一空间实际运行 .NET + Node/Vue 等组合；SDK/镜像摘要、依赖锁、资源/存储配额、独立测试服务与配置版本可追溯 |
| P3-14 | Git 分支/ref 导入与更新、文件/目录/归档上传 | 待实现 | Git 凭据不泄漏、提交与导入清单绑定；脏工作区不被静默覆盖；路径穿越/符号链接/解压炸弹拒绝，失败不污染现有代码 |
| P3-15 | Web IDE、本地 IDE 接入、多语言调试与私有预览 | 待实现 | 实际编辑/断点/单步/终端/预览；一次性目标绑定会话、WS Origin、撤权/过期断开；不开放宿主 SSH/Docker/Agent Socket |
| P3-16 | 构建/测试/运行任务、流水线与运行历史 | 待实现 | 项目级多服务依赖、退出码/取消/超时/断线恢复/资源锁、日志预算；失败/部分失败真实记录，不自动部署生产 |
| P3-17 | 调试信息自动收集、测试/覆盖率解析与可用报告 | 待实现 | 同一 run 关联源码/环境/步骤/错误栈/退出码/指标；解析真实 TRX/JUnit 等证据；失败/取消也有脱敏 Markdown/JSON 报告，未知原因不捏造 |
| P3-18 | 开发空间启停/重建/快照/清理、隔离与完整验收 | 待实现 | 空闲停机保留代码、删空间不误删他人/生产资源；NVMe 运行/HDD 备份、配额/保留/敏感信息/跨用户拒绝、两语言真实调试报告闭环 |

## 8. P4：实用安全、通知、身份与开发协同

依赖：P2/P3；默认不自动打开企业网络端口或对外投递。

| ID | 缺失工作/交付 | 当前状态 | 必须证明的结果 |
| --- | --- | --- | --- |
| P4-05 | 镜像/依赖漏洞扫描、SBOM与修复报告 | 待实现 | 锁定扫描器、更新源、真实测试样本、错误/过期、例外/修复版本与审计；不自研通用主机杀毒平台 |
| P4-06 | SMTP Email与通用Webhook通知 | 部分实现 | 模板/签名/测试/限流/重试与脱敏；Sender/私有值WebUI/Agent安全写入、共享限流/尝试、显式测试/未知确认重试已有隔离HTTP/TLS SMTP和真实配置链路证据。生产身份UI、批准目的端与长期压力仍缺；现有钉钉等模板可复用，不强制扩展全厂商矩阵 |
| P4-07 | GitLab/Git 平台部署模板、OIDC/目录/门户与备份 | 待实现 | 数据库/缓存版本预检、现有 Authentik 联动、仓库/CI 入口和资源保护；不接管未知现有实例 |
| P4-08 | GPU/设备与AI模板；C#酒馆完整业务、Vue聊天配置/媒体及扩展兼容 | 部分实现 | 既有GPU保护保持；新增酒馆272个ST子项按[设计](AI_TAVERN_MASCOT.md)及[账本](AI_TAVERN_ACCEPTANCE.md)逐条实施，核心编排C#、参考Cindy的统一供应商/用途绑定、Vue原生配置、14随包重构/66目录扩展逐源码与依赖核实后迁移适配；每条权限/安全/失败恢复/实际依赖/UI证据齐全。模型连接与本人余额读取纳入；不恢复商业网关/计费/通用插件平台。新增AI子范围待实现，不以文本连通或原Node代理代替 |
| P4-10 | 个人/公共门户、收藏/引用隐藏/搜索与可用性 | 部分实现 | 每人自配、管理员公共发布、引用/隐藏、权限裁剪、探测限流/SSRF防护和失效链接；无团队发布层级 |
| P4-11 | Authentik 用户/组/MFA/服务应用/Provider/SSO 运维 | 部分实现 | 受控协议模板、双入口图标/地址、绑定/移除影响预检、目录与权限真实同步 |
| P4-12 | 运维安全事件、会话/近期登录、通知已读与审计导出 | 部分实现 | 查询/导出有界、不可变审计、事件关联、普通用户只能本人数据 |

## 9. P5：单机产品完善、安装与最终交付

依赖：保留阶段的资源、权限和任务模型；不引入多节点、租户、订阅或插件平台前置条件。

| ID | 缺失工作/交付 | 当前状态 | 必须证明的结果 |
| --- | --- | --- | --- |
| P5-05 | 单工作站仪表盘、品牌/主题/设置与原生AI看板娘 | 部分实现 | 明暗/系统主题与移动资源/安全状态保持；[看板娘](AI_TAVERN_MASCOT.md)六类PET及[Live2D/AI动作子门槛](AI_PROVIDERS_LIVE2D.md)有真实模型/资源/身份/UI与恢复验收，所有AI经酒馆/统一供应商，不遮确认/终端，睡眠不抑制安全中心；单一品牌，不做每租户白标/企业报表或独立翻译管理平台；新增PET待实现 |
| P5-09 | 新机 bootstrap、全镜像构建、配置/升级与手动恢复包 | 部分实现 | 全新/离线/重复安装、源测速、加速可选、依赖 clone 配置、WD build/deploy 全流程通过 |
| P5-10 | 整站性能/安全/故障/浏览器/无障碍/升级兼容 | 待验收 | 每功能测试映射、实依赖+负载/长时运行、日志风暴、断网/重启/恢复与安全扫描 |
| P5-11 | 使用/运维/开发/灾备文档与 master 正式交付 | 部分实现 | 源码/锁文件/migration/镜像摘要/说明/验收版本一致，master 可部署；dev 循环不自动回滚 |
| P5-12 | P0–P5 最终逐项验收与遗留归零 | 待验收 | 下述完成标准全部成立，不能以本地测试/空菜单/未启用适配器填为完成 |

## 10. 共用实现与完成标准

每个 ID 拆成以下工作包；按依赖推进，不只做前端：

1. `PAGE`：页面/路由、普通/管理员/显式授权开发者视图，加载/空/错误/离线/过期/无权限/长文本/手机/明暗主题。
2. `UC`：Application 用例、REST/OpenAPI/Swagger、计划/确认/幂等/分页/限流/流式协议和稳定错误码。
3. `RPC`：本地UDS、能力/版本、资源白名单与敏感输入渠道，不新增远程Agent入口。终端、文件与网络写入使用专用契约，不放宽任意路径/命令禁令。
4. `PERM`：默认拒绝、主体/本人空间/资源授权、Worker权限、二次确认与审计字段；不因删掉企业租户而删除用户数据隔离。
5. `DM`：明确实体/关系/唯一约束/版本/索引/保留；数据库 schema 变更有 migration，缓存可重建且 TTL。
6. `DEV/QA`：实现、单元/集成/架构/原生依赖/UI、安全/失败与恢复验证、精确清理；收录提交及运行时证据。

完成必须同时满足：

- 功能不是 fixture、空接口、不可用按钮或“暂不实现”占位；正常用户路径真实执行。
- UI/API/后台任务/Agent/持久化按适用范围完整闭环；只读及写操作均有正确权限。
- 风险操作有计划和确认、幂等、取消边界、资源锁、重启/断线恢复、部分失败结果与审计。
- 真实依赖验收隔离于生产数据；没有遗留临时容器/库/用户/卷/归档/权限授予。
- 不用模拟证明物理网络/磁盘/SMTP/证书验证/灾备；保留需求的外部条件欠缺明确标未验收，不擅自操作企业设施。17项授权移出能力不再要求原多主机/邮件托管/DNS平台验收。
- 发布覆盖/升级后健康、数据保留与功能回归通过；所有源码、文档和构建版本可追溯。
- `master` 完整交付且全部任务验收。用户自行处理全量 sudo/密码，不作为自动化完成门槛。

测试或操作卡住不等于暂停目标：尽量推进独立安全工作；仅用户显式暂停才设paused。需要保留功能的域名或通知目的端授权时记录真实条件，不擅自使用企业基础设施；单机交付不要求第二主机。

## 11. 功能参照与许可说明

检索日期同上；仅用官方公开功能资料识别领域，具体实现与验收要求是 Whale Deck 的产品设计，不声称与付费扩展逐二进制一致：

- [Plesk 功能总览](https://www.plesk.com/features/)：网站托管及相关邮件/DNS、运行环境、Git、权限与备份领域。
- [Plesk服务计划与订阅历史参考](https://docs.plesk.com/en-US/obsidian/reseller-guide/understanding-service-plans-and-subscriptions.64607/)：仅保留原范围来源；套餐/订阅已明确移出，不是当前实现要求。
- [1Panel 官方产品与功能目录](https://docs.fit2cloud.com/1panel/)：单机应用、容器、系统等运维入口及高级能力的体验参照。实际购买版本不在此假定。

实施期间以本清单为固定范围，官方未来新增功能不会自动扩大目标。支付结算/转售收费、社交营销、第三方商业扩展复制、单独原生移动 App、Windows 宿主机全面兼容不属于本次 Linux Web 管理平台清单；功能扩展须先补充明确任务和验收要求。

变更审核：GLM Flash 仅只读盘点公开设计文档；一次成功、无重试，输入 658366 Tokens（缓存 593472）、输出 5715，186269ms，费用未由响应返回。其行号/文件长度存在不一致，不采作权威证据；范围、70 项任务分期、权限与架构取舍均由 GPT 核对现有文档和本日用户决定后制定。

## 附录A：历史范围变更与批次记录

以下按原批次保存；其中“当前/最新/已激活”、70/76项、版本及暂停状态只在记录当时成立，不自行启用目标模式或恢复移出任务。

- **2026-10-30后续追加：统一供应商→酒馆→Live2D看板娘，待实现**：所有看板娘AI复用酒馆编排与参考Cindy的集中模型供应商配置；对话页必须Live2D，AI通过动作目录/结构化指令选择动作表情，C#校验及Vue播放，并有取消/去重/睡眠/用户优先级和真实语音口型。详见[配置与动作契约](AI_PROVIDERS_LIVE2D.md)。沿用DEV-AI-001..008、59主ID和272ST/6PET父级，增加8个供应商/8个Live2D子门槛；本轮仅文档，未调用模型、下载SDK或部署，不读取Cindy实际密钥。

- **2026-10-30用户追加：完整C#酒馆与AI看板娘已纳入计划，待实现/验收**：P4-08扩大为.NET10/C#重构酒馆权威编排、Vue原生聊天/配置及完整功能；P0-08/P5-05增加dsh-pet参考的动画、交互/物理、碎碎念/对话、真实余额/额度与睡眠。全部272个ST ID（206能力+66目录扩展）和6个PET逐条登记，DEV-AI-001..008直接实施前后端；66扩展仍待逐源码/许可/依赖审计，不把目录或按钮当兼容。59个主ID保持，新增范围与工作量明确扩大，17个移出主任务不恢复；酒馆内模型/余额/扩展是本次明确例外，不恢复计费/通用平台插件。Authentik/BFF、Agent最小权限、NVMe/HDD、核心隔离/维护和原所有者/递归/ACL/删除计划保持。当前仅调研/设计，未实现AI代码、未部署/模型调用/真实页面签收。见[设计与事实](AI_TAVERN_MASCOT.md)、[全部验收账本](AI_TAVERN_ACCEPTANCE.md)、[固定dsh-pet源码](research/DSH_PET_REFERENCE.md)。

- **2026-10-30 所有者独立引擎与Agent1.48安装态通过，业务/UI待接**：新增显式UID/GID存在位、独立完整请求签名/四RPC、SDK版本能力门禁及单独CAP_CHOWN固定helper；旧chmod未扩权。3478 Unit/213 Integration（7默认原生跳过）、103新增专项、362容器文件Linux-root及Release/探针零警告错误通过。冻结532ceb9实际HDD/NVMe文件/目录20操作、8专属候选强杀、完整2MiB+7/原inode/模式/mtime/子项及20份HDD元数据独立核验准确清理通过；2a9ceb6宿主安装成功，运行exe/DLL映射同源。真实签名UDS6操作5Completed/1Aborted、5份HDD/6 Root与独立精确清理通过，旧权限6 RPC回归通过，11核心不变。Agent/MaintenanceHost为2a9ceb6/1.48，API/Worker/Gateway仍cb4c400；永久意图/七方保护/Worker/API/Vue及真实身份页面仍待，不计完整所有者功能完成。59项active，17项移出不算完成。[实施恢复点与证据](qa/CONTAINER_OWNERSHIP_AGENT_RPC.md)。


- **2026-10-30 普通权限真实页面故障五例通过**：产品仍cb4c400/1.47，无重复部署或回滚。真实页面取消/正常退出、Worker准确SIGKILL与自然租约、Agent实际重启，产品结束helper分别5.457/5.433/0.121/0.024秒；原文件完整2097159字节和九字段指纹不变，SQL双任务/原主体审计/Root Aborted及物理HDD元数据独立核验通过。准确临时容器移除后Unknown保留null/历史/保护；原账号删除，另一管理员320暗色页面先取消再明确处置，原执行Failed、Root仍Unknown，迟到Apply不重做；另一容器目录只在释放保护后创建，唯一新管理员审计通过。独立准确源/CID/身份清理、活动/租约/屏障0、11核心healthy。12新观察器/35权限传输Linux-root与8范围通过；首轮Future等待、Unknown换行帧的验收器失败保留，完整重验通过。所有者/递归/ACL、删除及剩余故障仍待，59项active、17项移出不算完成。[实机故障证据](qa/CONTAINER_PERMISSION_FAULTS.md)。


- **2026-10-30 普通权限cb4c400已覆盖并通过真实页面常规整链**：六组件/镜像/HTTP同源、运行进程映射与权限迁移核对通过。真实Authentik登录后，桌面/320明暗6场景（文件变更/无变化/恢复、目录变更/恢复、Ready取消）、原登记6次/原确认5次HTTP重试、刷新原引用与正常双端退出通过；5 Succeeded/1 Canceled，独立SQL/Root/HDD五份元数据、完整2097159字节/原inode/UID/GID/mtime/子项及准确清理通过。普通8路403/匿名4路401、CSRF/Origin拒绝，活动/屏障/租约/临时身份0、11核心不变。前两轮关闭抽屉选择器、第三轮执行身份与源属主混淆均为验收器失败，原证据保留且准确清理，第四轮完整命令0及独立只读复核通过；产品未回滚。此次重跑3375 Unit/213 Integration（7默认原生跳过）、695前端、35 Linux-root权限/传输、8范围与类型/生产构建通过；GitHub CI仍执行，不签全绿。完整权限和故障矩阵继续，59项active、17项移出不算完成。[发布与真实页面证据](qa/CONTAINER_PERMISSION_REAL_UI.md)。


- **2026-10-30 容器普通权限永久业务与Vue候选已生成**：0d31641已接三段独立业务签名、永久双Job/SQL/Outbox/审计、不可变模式/HDD事实、六方双向排斥、专用Worker/管理员API、预检确认/原引用接续/取消与Unknown页面。3375 Unit/213 Integration（7默认原生跳过）、Release零警告错误，冻结13c0f2a隔离真实PG1执行/1通过/0跳过、独立准确清理与11核心不变；695前端单元、28权限/72关联生产预览浏览器及类型/构建/lint/格式通过，320暗色/1440明色截图复核。SQL回执与浏览器API为明确夹具，不冒称真实身份/实盘业务；候选未覆盖，Agent/MaintenanceHost仍c1f3cd7/1.47、API/Worker/Gateway仍a59ec97。继续冻结发布与真实身份/权限/实际文件/故障整链及其余59项，17项移出不算完成。[SQL证据](qa/CONTAINER_PERMISSION_PERSISTENCE.md)、[页面与边界](qa/CONTAINER_PERMISSION_WEB_UI.md)。


- **2026-10-30 容器普通权限Agent1.47安装态已通过**：冻结c1f3cd7的专用强类型Plan/Apply/Inspect/Discard、整请求单次签名、SDK能力门禁与受限固定helper已安装。3280 Unit/209 Integration（7默认原生跳过）、91新增专项、329容器Linux-root零跳过、74安装/8范围基线及Release零警告错误通过。真实签名UDS完成文件改模式/无变化/恢复、目录改模式/恢复、永久取消共6操作（5Completed/1Aborted）；完整内容/原inode/UID/GID/mtime/子项、5份物理HDD元数据与6份Root原件通过，第二进程只读独立核对和准确清理通过，原目录RPC实机回归通过，11核心不变。Agent/MaintenanceHost为c1f3cd7，API/Worker/Gateway仍a59ec97；永久业务SQL/共享屏障/Worker/API/Vue及完整权限/删除继续，不冒称公开权限页面已完成。59项active，17项移出不算完成。[证据与边界](qa/CONTAINER_PERMISSION_AGENT_RPC.md)。


- **2026-10-30 容器普通权限内部候选通过真实HDD/NVMe**：原inode非递归模式变更、固定HDD元数据快照/独立域、永久原回执与只观察不重做已生成。26新/320关联Linux-root零跳过；冻结996a25c实际两设备文件/目录20操作（16完成4中止，8真实SIGKILL）、完整2MiB+7/原inode/UID/GID/mtime/子项及20份HDD元数据通过，独立只读核验准确清理/11核心不变。首轮卷命名被保护拒绝，修夹具不放宽白名单，失败另证准确清理且不改记通过。未安装/helper/RPC/永久业务/API/Vue，仍a59ec97/minor46；chown/递归/ACL/删除等保留，59项active、17项移出不算完成。[证据与边界](qa/CONTAINER_PERMISSIONS_ENGINE.md)。

- **2026-10-30 目录移动部分源留存Unknown真实页面通过**：实际a59ec97，16份共33554544字节文件的目标已完整发布、1份原件转存/15份仍在源时准确pidfd中断；SQL/Root保留true/null/null与发布保护。原临时身份删除、另一管理员320暗色页面先取消再一次确认，原Job失败/Acknowledged而非移动成功；另一容器写任务仅解除保护后成功。晚到原固定helper Apply仍Unknown，原Root/完整目标及分布两处原件不变；严格准确清理，独立复核活动/屏障/租约/临时身份0、11核心healthy。9新/14原观察器及47移动root回归通过；同HDD、非全部交换/撤权/跨盘验收。59项active、17项移出不算完成。[完整证据](qa/CONTAINER_MOVE_UNKNOWN.md)。

- **2026-10-29 容器移动实际部分文件中断四例通过**：运行a59ec97不重部署；真实BFF明确取消/正常退出、Worker SIGKILL与原自然租约、Agent systemd重启均在已登记部分私有inode时通过，产品结束helper分别4.008/4.041/0.116/0.268秒。完整134218176字节源SHA及9项原指纹不变、目标未发布、SQL/Root三事实false，原任务/回执/审计保留；第三进程独立核对四例与准确清理，活动/屏障/租约/临时身份0、11核心healthy。14项root观察器测试接CI；不冒称可见页面中断、目录树发布/源留存/Unknown或跨盘业务已验。59项active、17项移出不计完成。[证据与边界](qa/CONTAINER_MOVE_INFLIGHT.md)。

- **2026-10-29 a59ec97容器移动真实常规整链通过**：真实Authentik用户名/密码登录，1440/320明暗四次文件/目录移动、四次原登记HTTP重试与刷新原意图、Ready明确取消通过；独立SQL双任务/Root原件、完整2097159字节、混合UID/GID/普通属性与原文件inode匹配。普通8路403/匿名4路401、CSRF/Origin拒绝及双端退出通过。准确测试容器/源和临时身份清理，4 Succeeded/1 Canceled永久历史保留，活动/屏障/租约0、11核心healthy；只读第三进程重核通过。此次实际业务同HDD，不冒称跨盘；进行中取消/撤权/重启/Unknown等仍待，59项active、17项移出不计完成。[实际证据与边界](qa/CONTAINER_MOVE_REAL_UI.md)。

- **2026-10-29 容器移动a59ec97已正常dev覆盖，真实身份整链待验收**：同一发布Invocation的journal成功结束，六组件/镜像/HTTP来源一致；宿主实际可执行文件与DLL映射也为当前安装，新增移动迁移/表已入库。11核心healthy、database-platform未重建，活动移动/屏障/租约0，两个新增GET匿名401；托管容器运行库及PKCS来源分别核验10.0.13。3189 Unit/209 Integration（7默认原生跳过）、冻结实际PG1执行通过及独立清理、655前端和生产预览78/78保持。尚未真实管理员/普通用户、实际业务移动/页面与故障验收；59项active、17项移出不计完成。[发布与边界](qa/CONTAINER_MOVE_DEPLOYMENT.md)。

- **2026-10-29 容器移动永久SQL/Worker/API/Vue候选已生成，尚未覆盖**：迁移与不可变三态事实、Job/Outbox/审计、五方双向发布屏障、专属Worker和禁止通用重试已接通。冻结73fa4dd真实隔离PG1执行/1通过/0跳过及独立准确清理、11核心不变；首三轮测试问题保留并修正。Release零警告错误、3189 Unit/209 Integration通过（7默认原生跳过）；655前端单元及生产预览78/78、类型/构建/lint/格式通过，320明色/1440暗色目视复核。待冻结覆盖与真实身份/实际磁盘/故障整链；宿主仍53701f8/minor46，API/Worker/Gateway仍7b3f44e。59项active、17项移出不算完成。[SQL证据](qa/CONTAINER_MOVE_PERSISTENCE.md)、[页面证据](qa/CONTAINER_MOVE_WEB_UI.md)。

- **2026-10-29 容器移动Agent1.46已安装签收，业务候选继续**：53701f8宿主707文件匹配、签名UDS两类完整移动/取消及复制回归通过；e839a43已安装固定helper三次HDD↔NVMe完整移动/混合所有者与独立Root摘要、准确清理及11核心不变通过。失败观察器现场保留，单备份命名目录属主定点修复。新业务模型/三层签名/调度候选新增96项，全量3187 Unit/205 Integration通过（7默认原生跳过），Release零警告错误；尚未SQL/Worker注册/API/Vue。API/Worker/Gateway仍7b3f44e，59项active、17项移出不计完成。[安装态证据](qa/CONTAINER_MOVE_AGENT_RPC.md)、[业务边界](qa/CONTAINER_MOVE_WORKFLOW.md)。

- **2026-10-29 容器移动独立计划/永久回执候选c2c78b7：Move独立摘要域、完整内层绑定、三态发布/移除/原件事实、仅观察不重做已实现；目录混合UID/GID保留，复制属主规则保持，内层摘要漂移使当前绑定失效。19专项含9次测试子进程SIGKILL、389容器Linux-root/35宿主移动/27复制通过；冻结真实Docker三次跨文件系统2MiB+7、混合UID/GID、原外层回执/原件/清理及11核心不变独立验证通过。未安装/RPC/业务SQL/Worker/API/UI，仍7b3f44e/minor45；59项active、17项移出不算完成。** [证据及首轮失败](qa/CONTAINER_MOVE_INTENTS.md)。

- **2026-10-29 容器移动内部FD候选d48aaae：独立源/目标/私有原件与操作ID绑定、两端可写、父inode及固定FD校验已实现；17专项/368关联Linux-root零跳过，冻结真实非root独占容器完成HDD→NVMe文件/目录和NVMe→HDD文件3次完整2MiB+7、原件inode/普通属性/重试验证，准确CID/命名卷/源清理及独立复核通过、11核心保持。未安装/RPC/永久业务任务/Worker/API/UI，运行时仍7b3f44e；继续外层容器域协议与整链，59项active、17项移出不计完成。** [证据与边界](qa/CONTAINER_MOVE_SCOPE.md)。

- **2026-10-29 容器复制部分文件中断通过：真实BFF取消/退出、Worker SIGKILL/自然租约及Agent systemd重启共4例，原SQL任务/Root原件及准确清理独立复核，零活动/租约/屏障、临时身份0、11核心healthy；12项root观察器测试通过。验证器15秒退出时钟与30秒服务drain已分开，原子回执读取改为固定FD，失败轮保留。上午属主事故定点修复546对象及Agent应用根，六类数据库临时生命周期复验通过；六类实际HDD备份与校验也通过。产品仍7b3f44e，CI五作业最终success；目录树故障/Unknown可见页面/其余59项继续，17项移出不算完成。** [中断与失败证据](qa/CONTAINER_COPY_INFLIGHT.md)，[属主事故](qa/RUNTIME_OWNERSHIP_REPAIR.md)。

- **2026-10-29 7b3f44e真实容器复制常规整链通过，并修复动态入口双端退出**：实际浏览器正常Authentik登录，1440/320明暗四次文件/目录复制、刷新原意图、四次原登记HTTP重试、Ready明确取消、普通8路403/匿名4路401通过；独立SQL双任务/Root原件/完整2MiB+7/属性与准确清理匹配，5意图4成功1取消，零活动/租约/屏障，临时身份0、11核心healthy。首轮退出Authentik400仍200，修复精确批准issuer虚拟Host后，伪造hint400保持会话、正常双页退出WD401/Authentik拒绝通过；2996 Unit/205 Integration（7默认原生跳过）及真实YARP回环通过。进行中取消/撤权/Worker或Agent重启/Unknown真实故障整链与其余59项继续，17项移出不算完成。[证据与失败](qa/OIDC_DYNAMIC_LOGOUT.md)。

- **2026-10-29 容器复制页面/API/Worker已覆盖7c3facd，真实业务整链待验收**：584前端单元、28复制及77关联生产预览页面通过；42复制/294关联Linux-root零跳过。发布单元成功0，六组件及HTTP同源，新复制迁移入库、11核心healthy。Agent独立实际签名UDS已通过全字节/属性/永久取消与准确清理；真实登录页面到SQL/Worker/Root的复制及撤权/重启/Unknown整链仍待，CI40801153829未签终态。59项active，17项移出不算完成。[实际证据](qa/CONTAINER_COPY_WEB_UI.md)。

- **2026-10-29 容器复制页面候选与 Agent1.45 安装态通过**：新增两阶段复制/原引用恢复/显式取消/Unknown管理员处置页面，584前端单元、28生产预览浏览器及检查构建通过；42复制专项与294关联容器Linux-root全部执行零跳过。冻结ab406eb已安装Agent/MaintenanceHost，实际签名UDS两次完整文件/目录复制与永久取消通过，独立Root原件/属性/准确清理及11核心healthy。验证器相对模块路径失败已修；关联77页面回归通过；API/Worker/前端覆盖、真实身份SQL/Worker/UI整链仍待。59项active，17项移出不算完成。[证据与边界](qa/CONTAINER_COPY_WEB_UI.md)。

- **2026-10-28 容器复制 SQL/Worker/API 候选通过隔离真实 PG**：`e911452` 实现永久意图/EF migration、双 Job/Outbox/审计、共享发布排斥、不可变历史、专用 Worker 与管理员 API。2970 Unit/205 Integration 通过（7 默认原生跳过）；`a6156a0` 冻结真实 PG 1 执行/1 通过/0 跳过，包含并发确认、四方双向触发器、取消/租约/Unknown/保留，独立清理复核及11核心healthy。首轮夹具保留锁导致 Started 断言失败，修正顺序并保留全部新旧保护；未放宽锁。Agent1.45、API/Worker 尚未部署，复制 UI/真实身份与实际文件整链仍待。59 项 active，17项移出不算完成。[证据与失败记录](qa/CONTAINER_COPY_PERSISTENCE.md)。

- **2026-10-28 容器复制异步业务候选已生成，未持久化/部署**：`24238fb` 新增请求/完整计划/确认执行三层独立签名、永久意图实体与事务仓储契约、异步服务和六秒复核调度。HTTP 不扫描/复制，Ready 明确确认后独立执行；原 ID 丢回复只观察或封闭，Unknown 不释放保护。新增 86 项，2968 Unit/201 Integration 通过（7 原生默认跳过）；进行中取消与租约丢失各真实等待一次六秒并取消夹具 RPC。SQL/EF、跨写入排斥、Worker/API/真实 UI 与安装态尚待，不把内存测试算落库。59 项 active，17项移出不算完成。[证据及下一步](qa/CONTAINER_COPY_WORKFLOW.md)。

- **2026-10-28 容器复制 Agent 1.45 候选已接通，未安装**：`6be0751` 新增独立强类型四 RPC、完整请求体签名、版本/能力门禁、SDK 及固定受限 helper/安装源。Release 零警告错误，2882 Unit/201 Integration 通过（7 原生默认跳过），TerminalProbe 编译通过。当前复制 Python 42 项仅 2 通过/40 非 root 跳过；进程 NoNewPrivs=1 阻止 sudo，未覆盖运行服务。前序 CI 四路成功、前端 408/1 的抽屉入场测量已修，开发/生产预览各 18 项通过，新整条 CI 待终态。实际安装态 UDS、永久业务任务/共享屏障/Worker/API/真实 UI 仍待，59 项 active，17项移出不算完成。[证据与边界](qa/CONTAINER_COPY_AGENT_RPC.md)。

- **2026-10-28 容器复制独立回执候选实机通过**：f1edb43新增容器独立计划/永久回执、元数据观察、固定传输和不重做的恢复。40复制专项与294原容器文件Linux-root均全执行通过；真实Docker九项、七次自有候选子进程SIGKILL，4 Completed/3 Aborted/2 Unknown，完整字节/树摘要、属性、容器ETag及Root原件匹配。两个准确CID/来源清理，11核心healthy；尚未安装/RPC/业务SQL/Worker/API/UI。继续Agent签名协议、固定helper及完整业务链路，59项active，17项移出不算完成。[证据及边界](qa/CONTAINER_COPY_INTENTS.md)。上一轮元数据原文CI失败已修，3d7db9f五作业全success；不代表本候选CI已通过。

- **2026-10-28 Prepared真实进程中断与容器复制基础推进**：安装态仍5f3622d/minor44；Worker SIGKILL/同CID显式启动及自然90秒租约、Agent实际重启分别通过，helper由产品0.674/0.165秒退出，维护PID不变，原任务均Failed/Interrupted、Root Aborted。独立SQL/Root/准确清理：24任务13成功/3取消/4失败/4Acknowledged，活动/租约/屏障0、11核心healthy。[过程与失败保留](qa/CONTAINER_DIRECTORY_PROCESS_RESTARTS.md)。容器复制新增固定root FD适配器，19专项/292关联及CI原294容器Linux-root全执行通过，冻结b8466a7在真实独占Docker容器完成两类全量复制/属性/重试，以及发布前实际目标消失Unknown/不重做，两个精确容器与来源清理；两轮验证器失败残留已私有转存未删除。复制尚未安装/RPC/业务/UI，继续原59项；17项移出不计完成。[候选证据与后续](qa/CONTAINER_COPY_SCOPE.md)。

- **2026-10-28 目录发布准备故障/Unknown真实页面与进行中撤销通过**：运行仍5f3622d/minor44，产品源码无差异，未重复构建部署。pidfd同时绑定准确容器root FD/原操作锁FD，实际Preparing目标消失与Prepared非空对象均自然Unknown保留屏障；另一容器两次延后。原身份删除后不同管理员真实320暗色页面取消再确认，旧Job Failed、原绑定/Root原件保留，只有解除屏障后等待任务才创建；存活原容器的固定Root helper迟到Apply仍Unknown。Prepared中明确取消5.312秒、正常退出5.387秒由产品主动终止helper，Root Aborted/目标不存在；分别Canceled和Failed/Interrupted，不冒报同一终态。294 Linux-root/零跳过及独立SQL/Root/准确清理通过，20总任务12成功/3取消/1中断/4Acknowledged，活动/Unknown/租约/屏障0、临时身份/unit0、11核心healthy。保留报文缺换行那轮失败，不把Root固定入口冒称新增签名RPC。交换/Worker/Agent进行中组合及其余59项继续，17项移出不计完成。[证据与边界](qa/CONTAINER_DIRECTORY_UNKNOWN.md)。

- **2026-10-28 目录排队重启/取消/正常退出实机通过**：产品运行时仍5f3622d/minor44，未重复构建部署。b7dc949验证实际4次Worker停止启动与1次Agent重启，维护入口PID不变；API持续可访问，永久原任务两成功/两取消，Root Completed/Aborted、SQL/实际目录inode属性匹配。成功轮次准确源/CID/临时身份清理，独立10总任务8成功2取消、活动/Unknown/租约/屏障0、11核心healthy。第一次Agent观察exec时序错误已修，原任务成功且无生产服务故障；未冻结原inode的唯一原始测试文件转存root私有证据，未放宽删除门禁。Root发布中强杀/撤权/Unknown真实页面及其余59项仍待，17项移出不计完成。[证据与边界](qa/CONTAINER_DIRECTORY_RESTARTS.md)。 运行时5f3622d的CI40611513488已最终五作业全部success；不代表之后QA提交的CI也已执行。

- **2026-10-28 容器新建目录已覆盖并通过真实页面常规链路**：实际5f3622d六组件/HTTP同源，更新0/SUCCESS、3分35.989秒，新永久目录迁移入库。真实管理员1440/320明暗共4次中文目录、0750/0700/0755、取消零登记/同HTTP重试原任务/刷新只查/两页完整注销通过；独立SQL/Worker/Root计划指纹、实际空目录inode/1000:1000/权限匹配，普通8路403/匿名401、缺CSRF400/错误Origin403通过。精确测试源/容器/临时身份清理、永久历史保留；4成功、活动/租约/屏障0、11核心healthy。2802 Unit/201 Integration（7默认跳过）、522前端、50联合页面及补充目录10项、冻结真实PG1执行通过并清理。CI40611513488四路success、frontend仍运行。实际业务重启/进行中取消/Unknown跨管理员页面及其他容器写入/隔离/留存仍需继续；59项active，17项移出不算完成。[证据及失败记录](qa/CONTAINER_DIRECTORIES.md)。

- **2026-10-27 容器目录Agent minor44已安装验收**：82850b2宿主/固定helper与SDK独立签名四RPC已完成，API/Worker/Gateway保持852ca6f。实际UDS中文目录/内核所有者/不重复创建、永久取消拒绝迟到写入、篡改/错误域/nonce重放/核心/只读根拒绝通过；独立Root回执/源码/运行PID、准确资源清理、11核心healthy与原活动/Unknown/屏障0通过。Release零警告错误、2784 Unit/199 Integration（7原生默认跳过）、287容器Linux-root/12宿主检查通过。更新整unit尾部探针权限失败已准确记录，安装与host validate成功，剩余探针发布独立0/SUCCESS；不重复安装或自动回滚。永久目录业务SQL/Worker/API/UI及实际身份页面仍待，59项active，17项移出不算完成。[证据与剩余](qa/CONTAINER_DIRECTORIES.md)。

- **2026-10-27 容器新建目录底层候选b43fedf，未安装**：精确实例/父版本/普通模式/内核所有者计划、私有准备与原子不覆盖发布、永久回执和只观察恢复已实现。23新增/279容器Linux-root、8范围检查通过；真实独占Docker容器正常创建及四个实际SIGKILL切点均通过，准确资源清理、独立回执/精确CID/源不存在和11核心healthy通过。已安装852ca6f/minor43不变，RPC/持久任务/Worker/API/UI尚待，59项继续active，17项移出不算完成。[证据及后续](qa/CONTAINER_DIRECTORIES.md)。

- **2026-10-27 852ca6f 发布准备故障和明确目标消失已实机通过**：正常dev覆盖0/SUCCESS、3分18.741秒，六组件/HTTP同源与宿主检查通过。Root私有副本实际8617984字节且覆盖前HDD已Verified后强制重启Worker，原任务Unknown保留全局屏障，另一应用Ready真实延后两次；原身份删除后不同管理员真实手机UI一次计划/取消确认/再确认、schema4封存，只有解除保护后原等待任务才发布成功，目标/属性与128MiB+7来源及2MiB+7备份完整核对。明确Docker404修复在真实删除容器后10.448秒进入TargetUnavailable/Unknown，零接收/发布、独立管理员UI封存；两整链正常退出与准确清理通过。独立79意图56成功30恢复(属性16)、活动/Unknown/租约/屏障0、三类身份0、11核心健康。2720 Unit/199 Integration(7默认原生跳过)、256 Linux-root/496前端/41接管浏览器/8范围检查通过；CI40518574767四路success、前端运行。首次失败均保留并正常收尾，全部59项继续active，17项移出不算完成。[证据及边界](qa/CONTAINER_RESTORATION_PUBLICATION.md)。

- **2026-10-27 2e13376 真实恢复进程重启与发布前能力复核通过**：正常dev覆盖0/SUCCESS、2分54.329秒，六组件/HTTP同源及宿主检查通过。排队恢复经Worker/Agent实际重启后完整134217735字节/4属性发布一次；Root实际收到64KiB后Worker SIGKILL显式启动、32KiB后Agent重启，均自然对账Failed/Discarded、目标/来源不变、发布0。修复Stage后能力丢失仍会申请发布屏障的问题，针对性用例修前失败修后通过；同任务恢复能力后不重Stage只发布一次。随后当前网页4次2097159字节/两宽明暗/正常双端退出及普通五路403通过；独立核对72意图53成功28恢复(属性14)、活动/Unknown/租约/屏障0、身份/准确测试资源0、11核心健康。2718 Unit/199 Integration(7默认原生跳过)、冻结真实PG1执行通过并清理；CI40509563766四路success、前端运行。两次观察器失败及收尾明确保留，不宣称自动拉起或完整59项完成；17项移出不算完成。[证据与边界](qa/CONTAINER_RESTORATION_RESTARTS.md)。

- **2026-10-27 d8822c6 显式属性恢复已覆盖并实机通过**：六组件/HTTP同源、正常dev覆盖成功、实际新增属性策略迁移及10.0.13/UDS/权限/动态入口通过。真实网页4次各2097159字节的原路径/其他路径新建覆盖，SQL永久true策略/Root schema4/HDD来源/旧inode/完整内容及UID/GID/权限/纳秒mtime匹配；另4次默认内容恢复仍schema1通过。两宽明暗、取消零登记/浏览器内容0、正常跨页退出、实际普通五路403/匿名401通过；精确身份/源/容器清理，独立重核55意图40成功18恢复(属性4)、活动/Unknown/租约/屏障0、11核心健康。2717 Unit/199 Integration(7默认原生跳过)、496前端/13历史浏览器、冻结真实PG1执行通过并清理；CI40501605203四路success、前端仍运行。ACL/xattr/特殊位、进行中重启故障、重建身份/长期清理与其余59项继续，17项移出不算完成。[证据与边界](qa/CONTAINER_RESTORATION_ATTRIBUTES.md)。

- **2026-10-27 宿主06b6eed/minor43已安装并通过签名属性恢复**：宿主更新0/SUCCESS、10.0.13/权限/UDS/动态入口通过；API/Worker/Gateway仍5fd8d3b。实际BFF正常上传产生旧属性捕获，独占容器重启后独立签名来源完整读取/Stage/Publish/Inspect，原路径及另一新路径各2097159字节、4属性/旧inode/HDD匹配，来源篡改/重放/错误摘要拒绝。升级后旧版内容恢复网页4次、两宽明暗/跨页退出及schema1兼容另验通过；最终45业务意图30成功、恢复10、活动/Unknown/租约/屏障0；两次属性发布是明确原生协议验证，没有业务Job或页面；三类身份/精确测试容器与源均0、11核心healthy。Release零警告错误，2704 Unit/199 Integration（7默认跳过）、251 Linux-root/12宿主检查通过。安装器范围说明已修，b9685da新CI四路成功、前端仍执行；持久属性策略/迁移/页面及更多故障仍待，59项active、17项移出不算完成。[证据及边界](qa/CONTAINER_RESTORATION_RPC.md)。


- **2026-10-27 历史普通属性发布引擎候选00f50b7，未安装**：可信HDD完整来源派生UID/GID/mode/纳秒mtime，schema4及独立指纹域、当前ID映射检查、交换前原子设置/精度校验和中断私有内容证明已实现。20专项/251容器Linux-root通过，含5次真实SIGKILL，12宿主安装检查通过；属性专用32KiB长路径回执/封存预算已通过，普通保持16KiB；首轮utime失败后清理缺证明已修且保留失败。实际独占容器重启后原路径/其他路径各2097159字节、4属性、旧inode/物理HDD及错误摘要通过；精确资源与冻结SHA独立核对。生产仍5fd8d3b、39意图零活动/Unknown/租约/屏障、11核心healthy。没有签名RPC/业务属性策略/页面选项，ACL/xattr/特殊位尚不支持，不签完整属性恢复或59项完成；17项移出不算完成。[证据及边界](qa/CONTAINER_RESTORATION_ATTRIBUTES.md)。


- **2026-10-27 5fd8d3b 同容器其他路径恢复实机通过**：正常dev覆盖0/SUCCESS，六组件/HTTP同源，冻结真实PG1执行/1通过/0跳过及清理。真实网页4次各2097159字节，含原路径、其他路径新建与覆盖、取消零登记/浏览器内容0/跨页正常退出；独立SQL来源与目标、Root、全字节/inode/HDD匹配。实际普通五路403/匿名401/零登记，临时身份与精确测试源/容器/隔离库0；39意图24成功/8取消/7失败，恢复6，活动/Unknown/租约/屏障0、11核心healthy。2688 Unit/199 Integration（7默认原生跳过）、494前端/9历史浏览器和检查通过；CI四路success、前端仍运行，上版3bac全success。历史完整属性、重建身份、长期清理与真实重启/发布故障继续，59项active、17项移出不算完成。[验收与边界](qa/CONTAINER_FILE_RESTORATION.md)。


- **2026-10-27 3bac49d原路径内容恢复已实机通过**：正常dev覆盖0/SUCCESS，六组件/HTTP同源、宿主校验及实际新迁移通过。冻结真实PG1执行/1通过/0跳过且清理；实际BFF生成2MiB+7旧捕获并重启测试容器，桌面明色/320暗色各一次恢复不同历史版本，浏览器内容0、取消确认零登记、正常跨页双端退出清空通过。SQL永久目录/来源MAC、Root、最终全字节、旧inode/HDD链独立核对；34意图19成功/8取消/7失败，恢复2，活动/Unknown/租约/屏障0，三类临时身份/测试容器源/原生库0，11核心healthy。原始属性、跨路径、容器重建迁移、真实Worker/Agent及发布故障等仍缺，59项active、17项移出不算完成。[实际证据与边界](qa/CONTAINER_FILE_RESTORATION.md)。

- **2026-10-27 容器历史内容恢复候选，尚未部署**：永久目录/迁移、独立来源绑定计划、现有发布任务/保护复用、Worker从HDD完整校验到暂存、API与原路径内容恢复页面已接。普通上传null字段保持旧MAC/快照，恢复拒绝浏览器内容与通用重试，原Unknown接管保留。Release零警告错误、2682 Unit/198 Integration（7原生默认跳过）、494前端/检查/构建通过；组合浏览器首轮34/35，修取消夹具版本后历史7/7通过。冻结真实PG与实际BFF/Root/HDD/网页尚待；原属性完整恢复/跨路径/跨容器重建及其他文件功能仍缺，当前e6b5034不变，59项active、17项移出不算完成。[边界和门禁](qa/CONTAINER_FILE_RESTORATION.md)。

- **2026-10-27 e6b5034/minor42已覆盖，编辑与历史来源实机通过**：两次正常dev覆盖0/SUCCESS，当前六组件/实际HTTP一致、宿主权限/UDS/动态入口及10.0.13校验通过。实际桌面明色/320暗色两次编辑，BOM/CRLF、取消零内容/单次确认发送、旧inode/HDD和跨页退出清空通过；真实BFF生成2MiB+7旧版本，独立签名List/ReadSource完整读回及实际测试容器重启后旧捕获读回、签名/重放/核心拒绝通过。真实只读沙箱误报可写要求已修且保留失败证据，写路径仍失败关闭。最终31意图16成功/8取消/7失败，Unknown/活租约/屏障0，临时身份0、11核心healthy；231容器Linux-root/22备份安装通过，CI后端/原生PG/原生Valkey/安装器success、前端尚运行。自然两小时撤权已完整通过；恢复永久目录/任务/计划/API/UI及其余59项继续active，17项移出不算完成。[真实协议与边界](qa/CONTAINER_HISTORY_RPC.md)、[编辑证据](qa/CONTAINER_TEXT_EDITOR.md)。

- **2026-10-27 自然两小时撤权通过；历史 RPC 候选待部署**：原登录实际7203.941秒/246次观察，移组后原快照到期首次403、到期后成功授权0、未改时钟/SQL/缓存，独占身份清理。minor42独立List/ReadSource/固定只读helper/严格EOF SDK及原生门禁候选完成，Release零警告错误、2666 Unit/191 Integration通过（7原生夹具默认跳过）、230容器Linux-root/22备份安装测试通过。尚未部署，实际仍fab6e2e+72c72cc helper；下一步覆盖并执行真实编辑与HDD来源RPC，恢复意图/目录SQL/API/UI仍待，59项继续active。[协议与边界](qa/CONTAINER_HISTORY_RPC.md)、[两小时证据](qa/CONTAINER_UPLOAD_INTERRUPTION.md)。

- **2026-10-27 容器HDD历史校验基础候选**：独立v2容器历史域、字段/游标预算、存在与完整内容校验区分、绑定当前实例的旧捕获只读来源已实现。20新增Linux-root及最终216容器组通过；冻结引擎在真实HDD完整2MiB+7读回/实际独占容器重启后读取旧捕获、错误UUID/旧当前范围拒绝通过，源/沙箱精确清理。尚未安装或接RPC/目录SQL/恢复意图/API/UI，不宣称已恢复文件。运行时fab6e2e+72c72cc helper不变，两小时仍观察；继续独立签名协议与完整恢复链。[证据与剩余](qa/CONTAINER_FILE_HISTORY_ENGINE.md)。

- **2026-10-27 真实浏览器进行中断通过，并修复只读清理竞态**：在实际Root已接收32KiB后，页面中止及跨页正常退出均有真实CDP ERR_ABORTED/canceled与独立SQL/Worker/Root终态，零目标发布；两页login、旧Cookie401/Authentik拒绝通过。门禁抓到只读/宿主--collect退出竞态，24确定性测试修复前10失败、修后全通过；helper已原子热修，无服务重启，程序集仍fab6e2e。最终25意图10成功/8取消/7失败、Unknown/活租约/屏障0、11核心healthy；失败输入精确核验清理。自然两小时仍观察，文本编辑未部署，下一次覆盖须包含helper修复。[浏览器证据](qa/CONTAINER_UPLOAD_BROWSER_INTERRUPTION.md)、[修复及安装摘要](qa/FILE_TRANSPORT_STOP_RACE.md)。

- **2026-10-27 有发布保护的Unknown真实整链通过**：原fab6e2e上512MiB旧版本实际HDD partial写入后停止独占容器，Unknown保留RootStarted/全局屏障；另一正常容器上传两次被Worker推迟。原身份正常退出并删除后，不同管理员持久计划/二次确认永久封存及失败对账解除屏障，后者才成功发布。原件全SHA/inode、Root载荷与HDD未完成副本保留，测试源精确清理/11核心healthy；清理观察器首轮遗漏空私有目录已修正并完整重跑通过。22意图10成功/7取消/5失败，Unknown/活租约/屏障0。权限两小时仍观察，编辑候选未部署；发布交换故障/重启、真实编辑和历史恢复继续。[证据与边界](qa/CONTAINER_UPLOAD_PUBLISH_INTERRUPTION.md)。

- **2026-10-27 容器文本编辑候选与部分接收中断门禁**：前端已支持独立容器位置、1MiB UTF-8/BOM/换行、打开时版本绑定、确认取消零字节/草稿返回与同步私有清理；469单元/46组合浏览器、检查和生产构建通过，候选尚未部署。实际fab6e2e三项先接收64KiB后取消/退出/RST，Worker终止及Root清理通过，无发布；18意图8成功/7取消/3失败、活租约/屏障0、11核心healthy。两小时自然权限降级仍在观察，不改时钟/缓存、不提前签通过；实机编辑、发布中故障/重启/有屏障Unknown及历史恢复继续。[编辑边界](qa/CONTAINER_TEXT_EDITOR.md)、[中断证据](qa/CONTAINER_UPLOAD_INTERRUPTION.md)。

- **2026-10-27 容器上传常规BFF/UI已覆盖并实机通过**：fab6e2e六组件/HTTP一致、11核心healthy；真实2MiB+7新建/覆盖/全内容读回、旧inode/HDD，显式取消/退出后Worker撤销，原账号删除后不同管理员封存/失败对账通过。真实1440明色/320暗色各一次发送、确认取消零字节、目录刷新/无溢出、跨页正常双端退出与旧Cookie拒绝通过；14条永久意图/审计/Root历史保留、Unknown/活租约/发布保护均0，临时身份0，测试源精确清理。发布中故障/撤权及有屏障Unknown、编辑/历史恢复与其余59项继续。[真实证据与失败](qa/CONTAINER_UPLOAD_BFF_UI.md)。

- **2026-10-27 minor41已安装并验证七RPC，业务入口未部署**：宿主Agent/MaintenanceHost实际b681e1d、10.0.13运行库/PKCS和权限/动态入口通过；真实签名五方法完整2MiB+7发布/读回/旧inode/HDD及取消通过，独立protobuf客户端验证历史Observe/Seal、实际停止重启、精确批准/范围拒绝与幂等。自有数据清理、两个失败验证留下的空锁保留，11核心healthy；API/Worker/Gateway仍f5a0029。下一步部署新业务并验收真实Cookie/SQL/Worker/HDD/UI，59项继续。[安装及失败证据](qa/CONTAINER_UPLOAD_HTTP_WORKER.md#b681e1d-宿主安装态验证)。

- **2026-10-27 容器上传HTTP/Worker/精确代理候选，未部署**：独立管理员计划/登记/原始暂存/状态/取消、真实SQL DI与专属HostedService已接通；minor41门禁保持，目标丢失或过期无法确认进入Unknown并保留保护。Release零警告错误、2604 Unit/191 Integration通过（7原生夹具默认跳过），17新HTTP/文档/边界、实际YARP 2MiB+7/6秒/Host与原生nginx精确额度通过；456前端单元/检查/构建通过。仍待minor41安装、真实Cookie→Worker/SQL→Root/HDD及页面整链，59项继续active。[证据与失败](qa/CONTAINER_UPLOAD_HTTP_WORKER.md)。

- **2026-10-27 容器上传管理员接管候选，未部署**：第12类container-upload的永久计划/确认、当前登录与权限复核、独立容器定位/观察验证、不可变审计及共享屏障原子解除已接通。丢封存回执只观察，收包前丢请求只重试幂等Seal，绝不重发文件或Publish；历史Published不伪成功。2593 Unit/174 Integration（7默认原生跳过）、冻结34ffb2e真实PG1执行/1通过/0跳过及清理通过；前端455单元/检查/构建、最终3浏览器合成流程通过。前两轮降级断言/永久意图清理失败均保留并修正，两个真实降级保护及所有历史保留。HTTP/专属上传Worker/代理/安装态整链仍待，59项继续active。[证据与边界](qa/CONTAINER_UPLOAD_ADMINISTRATOR_RECOVERY.md)。

- **2026-10-27 容器历史观察/永久封存候选，未安装**：minor41新增独立签名ObserveRecovery/SealRecovery，历史观察不进原namespace；原journal原子封存并完整保留原件/HDD，冻结minor40 reader与迟到writer也拒绝。2563 Unit/174 Integration通过（7原生夹具跳过）、180容器Linux-root通过；实际固定systemd profile完整2MiB+7、真实停止/重启、历史摘要/封存与当前实例迟到发送拒绝通过，临时资源清理/11核心healthy。没有安装候选或开放恢复SQL/API/UI；59项继续active。[证据与剩余](qa/CONTAINER_UPLOAD_RECOVERY_SEAL.md)。

- **2026-10-27 容器永久上传业务与界面候选，未部署**：SQL意图/Job/Outbox/共享发布保护、原登录接收、单次发布/持久取消调度及Vue上传确认生命周期已实现；minimum Agent40完整能力预检拒绝低版本新写。Release零警告错误，2501 Unit/174 Integration通过（7原生夹具明确跳过）；前端453单元/lint/格式/类型/生产构建通过。6fbca7b冻结隔离真实PG1执行/1通过/0跳过，包含双连接幂等及发布互斥；后续预检/WebUI不以该结果冒充整链验证。HTTP/Worker注册、独立容器Unknown管理员恢复、精确代理和真实BFF/Root/HDD/页面验收仍待，59项继续active。[证据与边界](qa/CONTAINER_FILE_UPLOAD_INTENTS.md)。

> **2026-10-27 登录撤销已覆盖并真实Worker签收**：f5a0029六组件/HTTP来源一致、11核心healthy；原登录退出后18次旧Cookie401，同主体第二登录仍管理员，Worker取消其未收字节上传，锁释放/审计/root拒收凭据通过。修复实际Valkey缺TIME权限；b764741生产类别ACL冻结原生1执行/1通过/0跳过，两提交CI五路success；完整OIDC双端退出、33MiB+7宿主上传/下载/HDD、容器大文件中断/普通拒绝及宿主终端通过。真实320暗色/1440明色跨页正常退出及旧Cookie拒绝通过；进行中撤销、永久容器上传/共享写屏障/业务权限审计与BFF/UI继续，59项不签完成。[证据与失败](qa/BFF_LOGIN_LIVENESS.md)。

> **2026-10-26 首字节前取消已安装签收**：136b8a0/minor40保存独立Prevented拒收标记；a58a489真实签名UDS先取消/查询/重放，再发送合法首帧，精确CONTAINER_UPLOAD_CANCELED且零暂存载荷。完整2MiB+7发布读回/HDD旧原件仍通过，2441 Unit/160 Integration（6原生夹具跳过）、166容器Linux-root通过；本批载荷精确清理、11核心healthy。永久容器任务/共享写屏障、登录撤销/权限审计及BFF/UI仍待，59项继续。[证据和后续约束](qa/CONTAINER_FILE_CANCEL_FENCE.md)。

> **2026-10-26 容器写入Agent已安装签收**：ac72b41/minor39与维护入口已覆盖，真实签名UDS五方法、完整2MiB+7发布/原只读RPC逐字节读回、旧inode/HDD原件、owner/请求体/MAC/重放拒绝与显式暂存取消通过。修复collect卸载竞态掩盖权限拒绝，8故障注入通过；Release零警告错误、2411 Unit/160 Integration通过（6原生夹具跳过）、154容器Linux-root通过。成功与两次失败现场精确核验清理，11核心healthy，应用容器未更新。永久任务/当前权限审计、BFF上传编辑/历史恢复/Unknown及真实UI仍待，59项继续。[完整证据和失败](qa/CONTAINER_FILE_WRITE_RPC.md)。

> **2026-10-26 容器写入协议候选（未安装）**：Agent minor39独立五RPC与请求体绑定一次性签名、SDK/Agent/root三层完整流校验、固定受限helper已实现。Release零警告错误、2410 Unit/160 Integration通过（6原生夹具跳过）、138容器Linux-root通过；真实systemd profile完成2MiB+7全内容发布/HDD旧原件、精确InvocationID活任务取消和清理，11核心healthy。没有产品安装态、永久任务/当前权限/审计或BFF写入页面；59项继续active。[证据及边界](qa/CONTAINER_FILE_WRITE_RPC.md)。

> **Linux 接续：容器原子发布/中断候选**。跨盘准备、完整输出读回、NOREPLACE/EXCHANGE、旧 inode/HDD 原件保留与取消/Unknown 观察已实现。23 项新真实 Linux-root 含 10 处 SIGKILL，127 容器/184 宿主/14 目录备份/8 范围通过。实际可写绑定和容器可写层的新建/覆盖、镜像原有文件及真实强杀后新会话不重复写入对账通过，合成资源精确清理、11 核心 healthy。未接 RPC/helper、任务/权限审计与 BFF/UI，运行时不变，59 项继续 active。[证据](qa/CONTAINER_FILE_PUBLISHING.md)。

> **Linux 接续：容器 HDD 捕获候选**。新增冻结目标证明、独立容器历史域与覆盖前捕获；19 项新真实 Linux-root（含三处真实 SIGKILL）、104 项容器/184 项宿主文件联合通过。实际容器 2 MiB+7 在物理 HDD 全内容/摘要/重放/错误 UUID 与停止实例拒绝通过，原件/父版本不变；源与留存同 HDD，仅版本留存。另宿主 NVMe→HDD 实际沙箱通过，临时内容精确清理、11 核心 healthy。尚未原子发布、RPC/helper/任务/BFF/UI，运行时不变，59 项继续 active。[证据](qa/CONTAINER_FILE_HDD_CAPTURE.md)。

> **Linux 接续：容器上传暂存候选**。严格容器/启动实例/父与叶版本绑定、私有暂存/回执/重放/明确丢弃已实现；25 项真实 Linux-root（含接收进程 SIGKILL）和独占实际容器 2 MiB+7 全字节/SHA/原件不变与清理通过。Release/2308 Unit/160 Integration 通过，6 原生夹具测试跳过。未接 HDD 发布、RPC/helper、任务/BFF/UI，当前运行时不变，59 项继续 active。[证据](qa/CONTAINER_FILE_STAGING.md)。

> **2026-10-22后续真实CDP终态通过，运行时0d18dd3不变**：页面取消和同源另一页正常退出均在真实大文件部分接收后记录ERR_ABORTED/canceled，SQL两条Incomplete/0 Validated；重新完整小文件全SHA与loadingFinished、双页login/清空私有管理壳和WD401/Authentik403通过。8项新观察器单元已接CI，临时资源精确清理/11核心健康独立确认。下方网络未证明是前次历史；2小时过期/组撤权及容器写入/HDD历史/恢复、其余59项继续。[完整证据](qa/SESSION_EXPIRY_UI.md)。

> **2026-10-22实际0d18dd3/minor38**：冻结PG1执行/1通过且清理、CI39726926076四路success、六组件/HTTP来源一致。真实320暗色完整2MiB+7摘要、跨页正常退出双页回login/清空管理壳、无Storage信号的401刷新兜底及普通真实用户拒绝/首页/个人门户/双端退出通过；新SSH临时身份0/独占数据清理/11核心healthy，13单元和60 Linux-root重新通过。大文件SQLIncomplete但浏览器网络终态观察未签；完整组撤权、容器写入/HDD历史/恢复及其余59项继续。下方未部署/未签及旧76项均为历史，不当作当前完成率。[证据](qa/SESSION_EXPIRY_UI.md)。

> **2026-10-22扩展BFF验收，运行时dd290e9不变**：44c4d1d工具通过真实2MiB+7完整下载、64MiB+7实际响应断开与Incomplete、后续完整下载，普通四路403及退出原Cookie重放四路401。51真实Linux-root测试和独立身份/文件/容器清理、SQL审计/11核心健康通过。真实浏览器因工具超时仍未签收，下载中途撤权、容器写入/HDD历史/恢复与全部59项继续；CI前端依赖安装失败待查，不能称整CI通过。[完整证据](qa/CONTAINER_FILE_BFF_UI.md)。

> **2026-10-22容器文件只读BFF/UI已覆盖dd290e9**：五运行组件实际来源一致、固定更新正常结束/11核心healthy。新管理员权限/审计/只读HTTP/容器详情浏览下载已接；2308 Unit/160 Integration（6原生本机跳过）、431前端单元、51联合浏览器+2专项及11新用例修后重跑通过，两个冻结源码真实PG各1执行/1通过并清理。真实临时管理员Cookie/BFF/SQL/UDS的896字节独立内容、核心/匿名/路径/实例/版本/Range拒绝与WD退出通过；独立核对身份0/容器清理/审计无原名泄漏。普通/撤权、真实网页、大文件/中断以及写入/HDD历史/恢复仍缺，不签整模块或59项。[完整证据](qa/CONTAINER_FILE_BFF_UI.md)。

> 2026-10-22容器只读Agent子门禁：fd3bda2/minor38及维护入口已装；43项真实Linux-root、实际受限helper/独立签名四RPC、完整2MiB+7和新SSH精确清理证明通过，11核心healthy。API/Worker/Gateway仍9ec8721，尚无业务审计/BFF/UI；Gateway候选11纯协议测试与完整2275 Unit/159 Integration（6原生本机跳过）通过，未部署。当前59项仍active，不签整个容器文件/文件管理。[证据及剩余](qa/CONTAINER_FILE_READS_INSTALLED.md)。

> **2026-10-21单工作站范围修订，优先于下方历史76项记录：当前59项，17项明确移出，不是完成。** 用户不需要多节点/租户，并授权去掉复杂低收益能力。完整决策、删项原ID和保留底线见 [单工作站实用版范围](SCOPE_SINGLE_WORKSTATION.md)。当前任务表保留原ID且已移除删项；旧QA/提交证据不重写、不作为恢复旧范围的指令。

- **2026-10-21容器文件Agent候选**：minor38独立只读四RPC、精确实例/UTF-8路径、完整字节SHA/正常helper EOF后end及独立helper profile已接；2263 Unit/159 Integration通过（6原生本机跳过），Windows Python9纯通过/23原生跳过。没有安装或RPC实机验收、业务/BFF/UI尚缺，实际仍9ec8721/1.37；全59项继续。[候选证据](qa/CONTAINER_FILE_AGENT_RPC.md)。

- **2026-10-21容器文件读取基础**：708e343/756f87e两轮真实容器完整2097159字节、源身份与拒绝检查通过并精确清理；最终31项Linux-root全部执行通过，含真实SIGKILL两槽自动释放。独立新SSH核对两容器API404、载荷不存在、11核心healthy。固定helper候选/安装源码已提供但尚未安装，没有容器文件RPC/BFF/UI，不签整个文件模块；实际仍9ec8721/Agent1.37。[证据与未完成边界](qa/CONTAINER_FILE_READS.md)。

- **2026-10-21实时采样连续性**：独立摄取、120批/65536点有界原始重放及微秒幂等已部署；真实UDS停读20秒补取3批、420秒14序列各70点/最大6.008906秒/零缺批通过，未插值。9ec8721六组件来源一致，历史GET固定35秒预算修正，其他普通请求仍5秒；四历史窗口及80桶SQL比对、匿名401/普通403通过。2211 Unit/159 Integration通过（6原生跳过），前序两次冻结真实PG各1执行/1通过。窄屏/全部交互和最新CI前端仍待，不签整个P0或59项。[证据及失败记录](qa/TELEMETRY_CONTINUITY.md)。

- **2026-10-21目录清理常规BFF子门禁已装通过**：实际e46ef29/1.36六组件一致、11核心healthy、CI四路success；afb41b8验证真实两捕获×三范围6执行+4明确补集、2MiB+7完整内容/物理状态和当前对象保留。33 Job/10成功/零活动锁待处理消息独立复核，成功批次载荷及账号清理；两失败现场保留，同HDD不冒称灾备。真实网页/普通与撤权/中断接管继续，完整76项及P2文件项仍active。[证据](qa/FILE_TREE_PURGING_BFF.md)。

- **2026-10-21清理WebUI候选**：6a0083f/39d10e2显式三范围、二次确认、双任务及只读接管已接；415前端单元、139联合浏览器+手机780高19重跑、2186 Unit/159 Integration（6原生跳过）及Release通过；冻结工作站隔离PG1执行/1通过且清理。新BFF客户端9项纯测试通过但尚未运行，真实身份/UDS/HDD/网页和覆盖发布继续，不签完整文件模块或76项。[证据](qa/FILE_TREE_PURGING_UI.md)。以下旧候选“尚无页面”为对应历史。

- **2026-10-21清理业务材料候选**：独立当前登录/目录证明绑定、签名确认、续租调度与严格Gateway、Unknown组件三态已实现；2124 Unit/146 Integration（6本机原生跳过）及Release通过，旧维护快照并发修复后原测试10轮通过。[证据](qa/FILE_TREE_PURGING_BUSINESS_MATERIAL.md)。尚无清理SQL/migration/Worker注册/API/页面，未部署；下一步永久任务/锁/事务审计、数据库清理事实及第11类管理员处置。完整76项保持active。

- **2026-10-21清理安装态子门禁通过**：56101c8/1.36宿主/签名UDS及6c60ea3双检查点独立完整磁盘通过：两捕获类型/三范围、2MiB+7/1GiB+7、6完成/6取消及81路径精确清理复核。[证据](qa/FILE_TREE_PURGING_INSTALLED.md)。尚缺清理当前管理员/永久业务任务/SQL墓碑/页面，同HDD不冒称异盘或安装态强杀；API/Worker/Gateway保持2c8d1bb，全部76项仍active。

- **2026-10-21清理Agent候选**：独立minor36/capability四方法、固定helper/可信loader、严格三范围计划与回执、精确默认根升级已实现。Release及1856 Unit/146 Integration（6原生跳过）、冻结Linux清理30/文件184通过；尚未安装，未接业务任务/数据库墓碑/页面。[证据及剩余](qa/FILE_TREE_PURGING_AGENT.md)。运行仍2c8d1bb/1.35，全部76项保持active；下列“无RPC”为前轮历史。

- **2026-10-21目录清理内部候选**：单捕获三范围、完整证明、逐文件删除与墓碑/Unknown不续删已实现；未开放helper/RPC/任务/页面，未安装。最终专项24/普通181真实Linux-root通过，v5目录253整组通过，旧宿主模块字节一致；1GiB+7完整内容及6强杀通过，早期错误码失败保留并修复。Release及1758 Unit/146 Integration（6本机原生跳过）通过，上一44bcd66四CI成功；[证据及边界](qa/FILE_TREE_PURGING_ENGINE.md)。实际2c8d1bb/1.35与全部76项保持，不签整项。

- **2026-10-21真实覆盖网页子门禁通过**：已装2c8d1bb/1.35的1440明色/320暗色回收与覆盖、独立完整2MiB+7磁盘与HDD证明、刷新/取消、普通权限/正常退出通过。自有载荷精确清理、永久记录保留，5操作/9任务/4捕获及零活动/锁/pending Outbox独立复核；运行时和上一文档CI四路成功，新观察器14项Linux-root通过。[证据](qa/FILE_TREE_OVERWRITE_REAL_UI.md)。只验收此子范围，不签完整文件管理；目录清理、容器文件及全部76项保持active。

- **2026-10-21已安装2c8d1bb/1.35**：覆盖历史内容域修复通过冻结PG、同源码已装UDS/2MiB+7与1GiB+7独立完整磁盘、实际Cookie/BFF/Worker/SQL与Overwrite捕获两条实际恢复链，已dev覆盖发布/六组件来源与健康核对；网页覆盖交互仍未签收。1758 Unit/146 Integration（6本机原生跳过）、Linux32+23+5通过，新CI前端继续；首次旧预期失败保留。[证据及剩余](qa/FILE_TREE_OVERWRITE_CONTENT_PROOF.md)。完整76项仍active，不签整个文件管理。

- **2026-10-21覆盖历史内容证明修复候选**：发现目录inode树摘要/归档内容域混用，新增独立TargetContentSha256及Agent1.35专用能力、捕获签名与备份复核，未削弱原证明。1758 Unit/146 Integration、Release通过（6本机原生跳过），新实际Linux/PG/UDS/BFF/页面仍待验。上一1ea9de6已dev覆盖部署并核对来源/健康；完整76项保持active。[边界](qa/FILE_TREE_OVERWRITE_CONTENT_PROOF.md)。

- **2026-10-20安装态覆盖恢复子门禁通过**：57877a9验证客户端/观察器对已装6a6f8e2完成签名UDS及2MiB+7、1GiB+7完整原件/被覆盖目录/归档/新输出证明和新夹具精确清理；首轮观察器目录inode误断言已修并以5项真实Linux root回归保护，旧失败现场保留。6e68746四CI全成功，尚无候选API/Worker/WebUI覆盖发布；真实身份BFF/页面与Overwrite捕获复用继续。文件根/备份同HDD已明确，不冒充物理异盘或整个数据库平台问题。[证据](qa/FILE_TREE_OVERWRITE_RESTORING_UI.md)。完整76项仍active，下方为前序历史。

- **2026-10-20覆盖恢复WebUI候选 f35ee05**：独立3字段登记、当前目录版本冻结、计划/执行双任务、by-key与丢确认只查、取消/Unknown二次确认、覆盖前备份事实及第10类接管提示已接通；历史Trash/Overwrite严格类型均可复用。364前端单元、类型/生产构建/lint/格式通过，主代理Chrome生产预览88/88通过（含320/1045/1440明暗、普通拒绝页就绪后0文件请求）。一次DEV演示身份测试环境不匹配已保留、未削弱断言；[证据与未完成边界](qa/FILE_TREE_OVERWRITE_RESTORING_UI.md)。独立观察器3项真实Linux root范围/清理测试通过，Linux客户端已发布，安装态专用签名UDS/完整磁盘核验正在观察原句柄；未部署候选，真实身份BFF/页面与全76项仍active。实际Agent/MaintenanceHost/Gateway6a6f8e2、API/Worker18f38bb保持；以下为前序历史。

- **2026-10-20覆盖恢复管理员接管/Worker/API候选 d50b765**：第10类现任管理员Inspect/Discard对账、原身份冻结、备份证明单调、原子目录登记/审计/Job/Outbox/锁释放，以及独立HTTP/Worker/取消分流已接通；恢复kind字段扩至32并新增防截断降级保护迁移。Release零警告错误、1738 Unit/146 Integration通过（6本机原生明确跳过）；冻结d50b765工作站隔离真实PG1执行/1通过，临时DB/容器已清理并独立复核、11核心healthy。[证据及未完成边界](qa/FILE_TREE_OVERWRITE_ADMINISTRATOR_RECOVERY.md)。尚未部署：覆盖恢复UI、安装态专用签名UDS/独立磁盘证明、真实身份BFF/页面验收继续；实际Agent/MaintenanceHost/Gateway6a6f8e2、API/Worker18f38bb。完整76项仍active；以下为前序历史。

- **2026-10-20覆盖恢复持久化候选**：SQL仓储、永久双任务/Outbox/共享锁/租约、不可降级的覆盖前备份目录及Trash/Overwrite签名兼容已实现。Release零警告错误、1700 Unit/144 Integration通过（6本机原生跳过）；冻结62cd5dc在工作站隔离真实PG实际1执行/1通过、旧schema升级与审计回滚/取消/Unknown/跨Kind重用通过，临时容器清理独立确认、11核心healthy。前五次夹具/JSON查询失败均保留并修正后完整重验。[证据及未完成边界](qa/FILE_TREE_OVERWRITE_RESTORING_PERSISTENCE.md)。尚未部署：当前管理员对账、Worker/API/UI与安装态UDS/BFF/页面继续；实际Agent/MaintenanceHost/Gateway6a6f8e2，API/Worker18f38bb，完整76项仍active。以下为对应历史。

- **2026-10-20恢复在线后**：11核心healthy、Agent/维护服务重启0、WD live200。Authentik品牌最终验收通过：320/390/740短窗口/1440明暗无横向溢出，修复系统主题切换背景回退及语言控件遮Logo；独立匿名背景全SHA吻合，真实密码/OIDC/双端正常退出与临时身份清理通过。见[最终证据](qa/DISK_CAPACITY_AUTHENTIK_BRANDING.md)。新增覆盖恢复Application绑定/服务/调度与Gateway候选，1678 Unit/143 Integration通过（6本机原生跳过）、183项新纯测试；Windows原子快照并发问题也已修复并回归。尚无覆盖恢复SQL仓储/迁移/Worker/API/UI，未部署候选；见[业务材料边界](qa/FILE_TREE_OVERWRITE_RESTORING_MATERIAL.md)。实际Agent/MaintenanceHost/Gateway仍6a6f8e2，API/Worker18f38bb；完整76项继续active。以下离线/旧候选状态是对应历史。

目录覆盖恢复新RPC1.34候选已接独立capability/四方法/固定helper/默认根升级，1463 Unit/143 Integration（6原生本机跳过）、27覆盖恢复/驱动和23配置升级实际Linux通过。未安装，业务任务/API/UI与签名UDS仍缺，实际18f38bb/1.33不变；[证据](qa/FILE_TREE_OVERWRITE_RESTORING_AGENT.md)。全量76项继续，P2-03/P2-04仍部分实现。

- 当前实际 **18f38bb/Agent1.33** 的不存在目标恢复已通过真实桌面/手机、新管理员恢复已删除账号捕获、独立完整字节及权限/正常退出验收；f3f9a80四CI及11组既有实机回归通过。目录覆盖恢复新增内部引擎（独立授权、HDD捕获后原子交换/旧原件保留、8强杀），尚未接RPC/业务任务/网页或安装；[证据与缺口](qa/FILE_TREE_OVERWRITE_RESTORING_ENGINE.md)。P2-03/P2-04仍部分实现，全部76项范围不变；以下旧候选/运行状态是历史。

- 目录恢复WebUI候选 **9d83056** 已接入历史记录与第九类接管，328前端单元及生产构建/类型/lint/格式通过，同源码工作站隔离PG1执行/1通过且清理；完整474浏览器仍运行，恢复专项加入后续CI。实际仍e0175a0/1.32，真实恢复身份/签名UDS/网页与覆盖部署仍待签收，P2-03/P2-04和全部76项继续；见[页面记录](qa/FILE_TREE_RESTORING_UI.md)。

- 目录恢复后端修正候选 **66451f7**：1383 Unit/143 Integration（6本机原生跳过）、工作站冻结真实PG1执行1通过及独立临时容器清理通过；CI39359695663后端/原生PG/安装成功、前端仍运行。首次29245ce共用测试将已完成恢复版本误断为null的失败已修正并保留。新恢复实际UDS/身份BFF/WebUI与安装尚缺，实际仍e0175a0/1.32、全部76项继续；见[冻结证据](qa/FILE_TREE_RESTORING_BFF.md)。

- 目录恢复业务后端新候选：原捕获索引解析、永久双Job/共享路径及捕获锁/Worker/事务审计/第九类当前管理员对账/API与EF迁移已接通，1383 Unit/143 Integration通过（6本机原生跳过）。冻结实际PG/新CI及恢复WebUI、真实身份和安装态尚待验收；实际仍e0175a0/1.32，全部76项继续，见[业务边界](qa/FILE_TREE_RESTORING_BFF.md)。

- 目录恢复新增 **RPC1.33 源码候选，未部署**：独立Plan/Apply/Inspect/Discard、固定helper/可信模块与精确默认授权升级已实现；1336 Unit/141 Integration（6本机原生跳过）、19恢复/19根升级真实Linux通过。新签名UDS/独立磁盘探针已编译但未运行；实际e0175a0/1.32不变。永久任务/Worker/API/UI、覆盖恢复/彻底清理、容器文件及全部76项继续，P2-03/P2-04仍部分实现；见[候选证据](qa/FILE_TREE_RESTORING_AGENT.md)。

- 最新实际 **e0175a0/1.32** 目录回收常规链路已签收：四CI/冻结实际PG、1302 Unit/141 Integration与316前端单元/304浏览器通过；成功覆盖更新、六组件来源一致和11核心健康。真实管理员1440明色/320暗色各2MiB+7回收、刷新不重做、独立完整HDD归档/原inode验证、普通真实用户0页面请求/有效CSRF403、双端退出与合成数据精确清理通过。当前文件源/留存同设备，仅版本留存；[完整证据与失败](qa/FILE_TREE_RECYCLING_UI.md)。七类旧功能回归继续；新类型实际跨管理员中断接管、公开目录恢复/覆盖恢复/彻底清理、容器文件和完整76项不缩减，P2-03/P2-04仍部分实现。下方源码候选/未安装说明为历史。

- 新目录回收源码补齐当前管理员第八类事务对账、根范围捕获目录查询与索引、源专用确认/取消/Unknown及捕获记录WebUI。1297 Unit/141 Integration（6原生跳过）、316前端单元及类型/lint/格式/生产构建通过；首轮22浏览器通过，最终304联合、冻结实际PG和新CI待验证。未安装，实际adc6cae/1.31不变；[边界](qa/FILE_TREE_RECYCLING_UI.md)，全部76项继续，不签完P2文件模块。

- 目录回收新增业务源码接通双任务/确认、层级锁/Worker/审计/API和独立签名HDD捕获目录；1294 Unit/141 Integration通过（6原生本机跳过）。新源码实际PG/CI/安装及新第八类当前管理员处置、WebUI仍待完成，实际adc6cae/1.31不变；[证据边界](qa/FILE_TREE_RECYCLING_BFF.md)，全部76项继续。1722242四CI全绿不替代本批验证。

- 新目录回收Agent1.32源码候选已接独立RPC/固定helper/可信模块与精确默认授权升级，1265 Unit、139 Integration（6原生本机跳过）和40实际Linux通过；引擎cf7a6a3同一冻结源码214项分批通过，首次模块选择错误明确保留。尚未安装、永久业务任务/Worker/API/UI及真实身份仍缺；实际adc6cae/1.31和76项保持，见[边界](qa/FILE_TREE_RECYCLING_AGENT.md)。

- 目录回收最终工作副本20项完整Linux通过，恢复16项、目录移动安全增强34项通过；覆盖真实1GiB+7回收还原、7强杀、父路径与私有路径并发换名。尚未完成提交后的冻结门禁、Agent/业务/API/UI与安装验收；实际adc6cae/RPC1.31不变，全部76项保持active。下方19项为前序历史，不重复计数。

- 新目录回收/HDD恢复内部链路源码已实现、19回收分批与16恢复完整Linux测试通过，含1GiB+7全量回收还原、旧文件inode保留/原路径新inode、7实际强杀及旧捕获精确兼容。尚无RPC/持久业务任务/Worker/API/UI和安装，见[边界](qa/FILE_TREE_RECYCLING_ENGINE.md)。前序cfbf64b四CI已成功，实际仍adc6cae/RPC1.31；目录覆盖恢复、清理、完整业务/页面、容器文件和76项继续，P2-03/P2-04仍部分实现。

- 2026-10-20实际adc6cae/RPC1.31：目录刷新位置已完成四CI、冻结PG、覆盖发布与真实管理员桌面/手机暗色/200稳定帧/0文件写请求/退出清理签收，11核心健康、当前USB动态入口可达。新增HDD目录恢复内部源码15实际Linux/176旧回归通过，但新RPC/持久业务任务/Worker/API/UI与安装仍缺；目录回收/覆盖恢复/清理、容器文件及76项继续，P2-03/P2-04保持部分实现。详见[位置](qa/FILE_BROWSER_LOCATION.md)和[恢复引擎](qa/FILE_TREE_RESTORING_ENGINE.md)。

- 2026-10-20实际49c7495/RPC1.31：解压完整常规BFF与实际管理员桌面/手机两格式、独立完整输入输出/Root证明、普通真实用户UI0请求/有效CSRF403、七类旧文件及OIDC/Host/资产回归通过。四CI/冻结实际PG/覆盖更新成功，六组件来源一致、11核心健康；夹具与账号清理。新目录刷新位置恢复已实现、302前端单元通过但未部署，见[解压网页](qa/FILE_TREE_UNPACKING_UI.md)及[位置候选](qa/FILE_BROWSER_LOCATION.md)。目录回收/恢复、容器文件与全部76项继续，P2-03/P2-04仍部分实现；下方旧未部署状态为历史。

- 解压操作WebUI新增候选已通过281前端单元、127联合浏览器（含26项解压）与另1解压接管专项，类型/格式/lint/构建及视觉通过。未部署、未执行新真实BFF/安装态网页验收；后端e365f4c的实际隔离PG证据保持，最终页面候选CI仍待跑。实际73513a4/1.31不变，全部76项及P2-03/P2-04继续，见[页面证据](qa/FILE_TREE_UNPACKING_UI.md)。

- 新解压业务后端 e365f4c（未部署）已完成持久双任务/锁/预算/Worker/取消/当前管理员第七类恢复/API及输入证明迁移，1237 Unit/139 Integration和同源码工作站隔离实际PG1执行1通过并清理。解压操作WebUI本地281前端单元/26生产预览浏览器及独立视觉复核通过，旧文件联合回归仍在运行。新真实身份BFF/安装态/网页尚未执行，实际运行时仍73513a4/1.31；全部76项及P2-03/P2-04仍部分实现。见[后端](qa/FILE_TREE_UNPACKING_BFF.md)和[页面](qa/FILE_TREE_UNPACKING_UI.md)，下方“业务任务/API/UI仍缺”为旧安装态/候选历史。

- 最新实际 **73513a4/1.31** 解压Agent子范围已签收：四CI/同源码工作站PG、真实签名UDS/helper、PAX/GNU两格式与1GiB+7全量新目录/独立磁盘、权限/取消/重放及精确清理通过。客户端bc8037d仅验证端修正，不改变产品；7类旧文件/OIDC/资产/宿主与11核心健康、文件任务/锁/终端0。见[最终证据及首次失败](qa/FILE_TREE_UNPACKING_AGENT.md)。解压BFF/持久任务/Worker/API/WebUI、目录回收/容器文件及全部76项仍继续；新业务绑定材料尚未部署。P2-03/P2-04仍部分实现，下方旧状态为历史。

- 解压Agent1.31候选已新增独立RPC/固定helper/可信加载与精确默认升级；1196 Unit/137 Integration、153原生树/15根升级和Release通过。本机6PG跳过、未安装/未实测新UDS、业务任务/Worker/API/UI仍缺；[证据边界](qa/FILE_TREE_UNPACKING_AGENT.md)。实际运行时保持abef670/1.30，全部76项继续；下面无RPC记录为前序历史。

- 目录解压新增内部源码：独立域计划/回执、两格式私有解包/完整核验/新目录发布与只观察恢复；152真实Linux通过，含22解压、1GiB+7全量及七强杀栅栏。未安装，CLI/RPC/BFF/Worker/UI仍待接通，实际运行时仍abef670/1.30；见[边界](qa/FILE_TREE_UNPACKING.md)。全部76项不缩减、P2-03/P2-04仍部分实现。

- 最新实际 **abef670/RPC1.30** 已签收常规目录压缩网页链路：四CI39238574798、269前端单元/245浏览器与工作站冻结实际PG通过；真实管理员桌面TAR/手机暗色TAR.GZ、原计划刷新只读恢复、完整原生下载及独立Root/磁盘/源元数据验证、普通真实身份隔离与有效CSRF下403、完整退出通过。六旧文件/资产/宿主回归及11核心健康、活跃文件操作/锁/终端0，自有验证数据与账号清理。见[最终网页证据](qa/FILE_TREE_PACKING_UI.md)。超过1GiB浏览器/实盘Unknown等边界、解压/目录回收恢复及全部76项仍继续，P2-03/P2-04保持部分实现；下方候选文字是历史。

- 目录压缩WebUI已新增源码候选：格式/目标、持久双阶段确认取消、原引用刷新恢复、Unknown和浏览器原生下载接通，269前端单元/86联合浏览器及独立视觉复核通过。尚未部署或真实身份网页签收，运行时仍0661051/1.30；见[边界](qa/FILE_TREE_PACKING_UI.md)。P2-03/P2-04仍部分实现，解压/目录回收恢复及全部76项继续。

- 最新实际 **0661051/RPC1.30** 已修复压缩路由并签收目录压缩后端：四CI39222897941、1169 Unit/137 Integration、冻结实际PG及真实身份/BFF/Worker/UDS/helper、两格式与超过1GiB完整HTTP/独立全SHA通过。六类既有文件、OIDC/资产/宿主回归通过，11核心healthy。压缩操作WebUI、解压、目录回收恢复和全部76项继续；P2-03/P2-04仍部分实现。见[最终证据与边界](qa/FILE_TREE_PACKING_BFF.md)，下方旧候选/失败待修文字保留为历史。

- 9675bbf/1.30已安装且四CI/冻结PG通过，但真实压缩API在创建任务前发现新旧复制路由冲突，未签收；源码已修复并补全局精确路由重复检查/负控制，待覆盖及真实完整链路重验。见[真实故障与证据](qa/FILE_TREE_PACKING_BFF.md)，不缩减76项或把全绿测试替代实机验收。下方旧安装态/候选状态为历史。

- 目录压缩业务后端新增候选：持久双任务/共享锁/Worker/当前管理员恢复/回执专用HTTP及EF迁移已实现，1169 Unit/135 Integration通过；尚未发布、冻结PG/真实HTTP及压缩WebUI仍待完成，见[证据边界](qa/FILE_TREE_PACKING_BFF.md)。不签完整P2-03/P2-04或76项。

- 最新实际8aa73c0/RPC1.30已签收压缩Agent与专用大归档下载：四CI/冻结实际PG、真实UDS/helper两格式及1GiB+7源/完整流和独立磁盘、六旧文件BFF与OIDC回归通过，11核心健康。压缩BFF/持久任务/Worker/人类审计/HTTP/UI仍缺；不把Agent等同完整P2，见[边界与证据](qa/FILE_TREE_PACKING_AGENT.md)。完整76项不缩减。

- 新增独立压缩Agent1.30源码（未安装）：RPC/受限helper/显式根授权/已知默认升级/可信模块与回执绑定大归档流已实现，1119 Unit/127 Integration和Release通过；真实安装态与人类权限/BFF任务/页面仍缺，线上2a60668/RPC1.29。见[Agent证据边界](qa/FILE_TREE_PACKING_AGENT.md)，完整76项继续。

- 2026-10-19新增压缩发布内部引擎，20真实Linux（1GiB+7全量/7栅栏SIGKILL）及实机登记根两格式探针通过；与codec/HDD捕获共56项新测试。没有新增RPC/业务任务/页面或部署，完整目录压缩/回收/恢复及76项继续，见[压缩发布边界](qa/FILE_TREE_PACKING.md)。线上为独立容器详情修复2a60668/RPC1.29，CI39191684883四作业已成功。

日期：2026-10-13（Asia/Shanghai）。状态：**新版完整目标已激活，开发继续**。

本基线最初在2026-10-13扩大到76项，2026-10-21按用户授权精简为单工作站59项。旧MVP/P1/P2/HOLD及76项动态保留为历史；当前范围以本文任务表及`SCOPE_SINGLE_WORKSTATION.md`为准。分期表示保留任务的实施顺序，不表示P3–P5可随意跳过。


### 早期恢复点记录（原第三节）


- 目录归档/HDD留存引擎源码已新增，尚无产品入口/未部署。22 codec/14留存真实Linux、96旧回归及实际HDD2MiB+7完整捕获/私有解包/独立磁盘核验与精确清理通过；1GiB+7和两个隔离子进程SIGKILL栅栏通过。当前源与备份同设备，不当作异盘容灾；RPC/计划/持久任务/Worker/审计/UI仍待继续，见[证据](qa/FILE_TREE_ARCHIVES.md)。不签完P2-03/P2-04或全部76项，线上仍b666042/RPC1.29。

- 2026-10-19最新实际运行时 **b666042/RPC1.29**：系统日志进程列已按用户反馈改为名称/辅助PID两行，并通过四CI39183928285、265前端单元/219浏览器、同源码冻结PG、真实管理员20条实际字段对应与手机桌面明暗网页签收；09:32:49 CST覆盖成功、六组件来源一致、宿主和11核心健康，验证资源清理。见[证据](qa/JOURNAL_PROCESS_LAYOUT.md)。这是P0-08的一个布局修复，不签完P0-08或76项；正文搜索等既有证据保持，目录回收/压缩解压、容器文件及其他阶段继续。下方旧运行时为历史。

- 2026-10-19最新实际运行时 **5e131e9/Agent RPC1.29**：字面UTF-8正文搜索已部署并通过真实管理员/普通用户/API/安装态UDS和320/1440明暗网页签收；四CI39088548813、1089 Unit/127 Integration、265前端单元/213 CI浏览器及冻结实际PG1通过/清理，08:13:27 CST更新成功。完整2MiB+7末尾命中、源全字节/inode/元数据不变、CSRF/Origin/匿名拒绝和精确清理通过；QA脚本错误修正及证据边界见[正文搜索签收](qa/FILE_CONTENT_SEARCH.md)。本次仅签正文搜索子范围；目录回收/压缩解压、容器文件/隔离/长期清理和全部76项继续。下方旧“正文搜索缺失/候选”是历史记录。

- 最新实际运行时 **b118454 / Agent RPC1.28**：Linux六组件实际构建来源、镜像标签、回环HTTP和维护页版本卡已签收，四CI39083441049、1070 Unit/126 Integration、250前端单元/203浏览器与冻结实际PG通过/清理；08:57:56 CST更新成功。真实管理员手机/桌面明暗、OIDC完整退出、资产/宿主/11核心健康通过。8d77293跨管理员任务接管继续有效；后续目录回收归档、正文搜索、容器文件和76项不缩减，见[最新来源证据](qa/BUILD_PROVENANCE.md)。下方旧运行时/候选为历史。

- 最新实际 **0defa90/Agent1.28**：当前管理员五类Unknown只读发现/有界分页/绑定验证及独立确认签名基础已部署，四CI39071442629、1032 Unit/123 Integration、229前端/183浏览器和冻结实际PG1执行1通过/清理通过；05:53:36 CST更新成功。不同真实管理员跨Subject/Cookie读元数据、旧接口404、普通清单403、原锁/Root回执不变与精确清理通过，见[证据边界](qa/FILE_TASK_RECOVERY.md)。这不解决完整当前管理员处置：持久计划/Worker/封存/原子当前审计/WebUI及真正原登录丢失验收仍缺；P0-03和完整76项继续，下方旧运行时为历史。

- 最新实际 **1399563/Agent1.28**：目录移动WebUI已覆盖发布和真实身份网页签收；229前端单元/183 CI浏览器/47本地联合、CI39065824243四作业及冻结实际PG1执行1通过/清理，04:34:09 CST更新成功。管理员桌面/320明暗两次完整2MiB+7移动、原计划刷新不重做、普通用户0界面请求和有效CSRF GET/POST403、独立原inode/全字节/回执核验与精确清理通过，见[最终证据](qa/FILE_TREE_MOVE_UI.md)。目录回收/归档、正文搜索、容器文件和原登录丢失后的当前管理员Unknown处置仍缺；下方历史不覆盖此记录，76项保持active。

- 最新实际 **39cb274/Agent1.28** 目录移动后端已部署及真实身份/API/SQL/Worker/UDS验收，997 Unit/122 Integration、四CI39059348941、冻结实际PG1执行1通过/清理和2MiB+7独立完整字节/原inode/元数据/审计与普通403通过；五类旧文件链路回归通过，见[完整证据及失败记录](qa/FILE_TREE_MOVE_BFF.md)。移动WebUI已本地实现但尚未发布，原登录丢失后当前管理员显式处置Unknown仍缺。完整76项保持active，下方运行时为历史。

- 2026-10-17最新实机 **e0efb8e/Agent1.27** 独立目录复制RPC/helper安装签收：CI39031927056四作业及同源码实际PG执行1/通过1/清理通过，更新unit20:39:50 CST成功。最终QA2ae4184探针实际复制2MiB+7与1GiB+7，完整SHA/权限/mtime/登记归属、永久回执/取消栅栏/重放等通过并精确清理；探针自身三处问题修正，未改鉴权。原四类文件真实链路回归通过，11核心healthy且临时身份/活跃写入/锁/终端0；898 Unit/118 Integration、176 Linux/3诊断通过。目录BFF异步计划/持久任务/层级锁owner/人类审计/UI仍缺，容器来源标记缺口保留；完整76项继续。见[证据边界](qa/FILE_TREE_COPY_AGENT.md)，下方为历史。

- 2026-10-17最新实机 **6dd60dd/Agent1.26** 已发布上传/变更/回收共享层级锁：CI39026564309四作业、873 Unit/118 Integration、171 Linux/218前端单元/136浏览器及同源码实际PG通过。实际更新unit19:07:34 CST成功，真实BFF/Worker/UDS/HDD文件流程回归通过/清理，活跃写入/锁/终端0和11核心健康。Python目录复制引擎仍未开放；容器统一构建来源标签/程序集提交号是已发现的P0/P5缺口。见[签收和证据边界](qa/FILE_HIERARCHY_LOCKS.md)，全部76项继续；下方旧运行时为历史。

- 2026-10-17最新实机 **c60a7c7/Agent1.26** 已覆盖小屏长目录点击修复；四作业CI39020311959及136浏览器通过，实际更新unit17:29:48 CST成功终态，程序集/HTTP资产与源码产物对应。真实320px明暗/长子目录/桌面正常点击、无文件写请求通过并清理测试对象；11核心健康。普通文件历史/回收/恢复仍按7ece209的完整实际链路证据，目录操作完整清单内部基础21真实Linux通过但未接新写入口。下方候选/旧运行时描述为历史；继续目录/归档与所有76项，不由此签完整P2-03/P2-04。

- 2026-10-17最新runtime **7ece209/Agent1.26**普通文件历史/回收/不存在与覆盖恢复/单HDD副本清理已覆盖，CI39016011071四作业成功、135联合浏览器、同源码冻结PG实际1通过/清理。真实身份/API/SQL/Worker/UDS/HDD与管理员手机网页/42字节独立下载、普通用户首次刷新请求0及403通过，11核心healthy、临时资源清理；同设备副本不称异盘容灾。实机长目录指针遮挡已有新源码和25生产预览回归，尚未部署，详见[历史回收站最终证据](FILE_HISTORY_RECYCLE.md#实际覆盖与已验证子范围7ece209--agent126)。完整76项不缩减，目录/归档、容器文件、隔离/长期清理与其余阶段继续。

- 2026-10-17最新runtime **d3e49c1/Agent1.24**完成目录与续页节点键命名空间修正并覆盖，CI38980535186四作业成功、204前端单元/14目录树浏览器及完整生产授权门禁通过。更新unit07:32:04 CST成功终态、安装程序集SHA一致，覆盖后真实OIDC/API/UDS/root目录树/搜索、宿主/资产/11核心健康复验通过；活动FilesView HTTP/Gateway与本地已测生产产物SHA全等。服务端与eee26bf无差异，工作站PG与完整真实管理员/普通用户网页证据仍明确归属前版。6类数据库NVMe/备份HDD落点只读核对通过。见[最新签收](qa/FILE_BROWSER.md#最终覆盖d3e49c1--agent124)，下方为历史；窄屏顶栏、列表转义帧预算、编辑/覆盖统一HDD备份、回收站/归档/容器文件及全部76项继续。

- 2026-10-17 runtime **eee26bf/Agent1.24**目录树/递归名称搜索已部署实机签收，四作业CI38973477051成功、同源码冻结PG实际1通过/清理，771 Unit/115 Integration、77 Linux、203前端/86联合浏览器通过。实际更新06:15:23 CST成功；真实管理员90字节上传/搜索定位/类型大小写/下载SHA、普通用户首次刷新无界面请求及真实CSRF下新接口403通过，11核心健康且临时文件/账号清理。QA脚本初次参数重名、浏览器收尾超时及后续QA-only CI取消均保留事实边界，见[最新实机账本](qa/FILE_BROWSER.md#最终运行时签收eee26bf--agent124)。下方未部署说明为历史。下一步UTF-8编辑和覆盖上传统一HDD前置备份、回收站/归档、容器文件及隔离；完整76项继续。

- 2026-10-17 runtime **1e72ac4/Agent1.23**已覆盖并签收新建目录、普通文件复制/移动/权限的BFF/任务/Worker/人类审计与真实管理员网页，普通用户新入口拒绝。CI38962862118四作业成功，同源码真实PG执行1通过/清理；实际更新04:41:25 CST成功结束，最终BFF/宿主/11核心健康通过。下面未部署说明为历史。目录树与名称搜索1.24新源码正在开发验证、未部署；编辑前HDD备份、回收站/归档、容器文件/隔离仍缺，全部76项保持active。见[运行时证据](qa/FILE_MUTATION_WORKFLOW.md#最终运行时签收1e72ac4)与[目录树搜索验收](qa/FILE_BROWSER.md)。

- 2026-10-17 新源码**3e2da89**文件变更BFF/任务/Worker/API/WebUI子范围已实现并验证：757 Unit/115 Integration、61 Linux、200前端单元/24文件浏览器及四作业CI38954590064成功，同源码冻结PG实际执行1通过/清理，含双连接路径锁和真实Job锁等待后租约边界。没有部署此新运行时；最后实机签收仍3f47300/Agent1.22。下一步真实覆盖/新Probe/BFF及生产身份网页验收，再继续目录树/搜索/HDD/回收站/归档等。见[准确证据边界](qa/FILE_MUTATION_WORKFLOW.md)。P1-11/P2-03仅部分实现，全部76项继续。

- 2026-10-16 runtime **`6bf4222`** 文件传输Agent基础实机签收：1.20专用登记目录RPC、受控流式上传/下载、版本/摘要/幂等/前序保留与断线回执；修复systemd/openat2冲突及拒绝时缓冲清理问题，原始失败未抹去。700 Unit/100 Integration、22Linux文件/15容器策略、CI38896550960四作业/同源码冻结真实PG通过且清理；更新unit19:54:10 CST成功。真实UDS完整2MiB+7二进制/权限/覆盖/重放/断线与实际安装helper通过并清理，身份/宿主及容器终端/通知/8资产/宿主回归与11核心healthy，PKCS10.0.13/sudo保留。见 [Agent证据](FILE_TRANSPORT.md#最终安装态agent签收6bf4222)。文件管理API/任务审计/Worker/WebUI、容器文件、回收站/HDD及团队隔离仍缺；P1-11/P2-03只部分实现，完整76项继续。下方为历史。

- 2026-10-16 runtime **`085acef`** 容器终端BFF/共享WebUI已覆盖签收：683 Unit/100 Integration、176前端/35浏览器及CI四作业全绿，新增非超级用户CI原生PG真实升级/目标约束/持久审计执行1通过；工作站同源码冻结PG执行1通过并清理。实际更新unit18:07:25 CST成功，真实OIDC→BFF→UDS→Exec、core/CSRF/Origin/幂等/尺寸/exit/撤销及Host回归通过。真正网页管理员列表入口/UID1000/原生中文IME/读屏/主题/resize/退出与普通用户首次/刷新403、surface0通过，临时账号/容器/助手清理独立核实，全部会话Closed。身份/通知/8资产/宿主/11核心健康、PKCS10.0.13及sudo保留。见 [最终证据和失败记录](qa/CONTAINER_TERMINAL_BFF_UI.md#最终覆盖签收085acef)。下一步文件传输、隔离/长期清理与更广故障；P1-11及76项继续，下方为历史。

- 2026-10-16 源码 `f81324c` 已补容器终端BFF目标/启动实例/shell绑定许可、EF迁移、人类主体审计/撤销与共享WebUI及容器入口；Release、683 Unit/100 Integration、176前端单元/35本地浏览器、类型/lint/构建与全依赖审计通过。6原生PG跳过，Linux驱动策略等待Linux CI；新真实验收脚本已准备但未运行。两个工作站网卡SSH均超时，本轮未部署，最后签收runtime仍a0b0001，不推断当前健康。见 [源码与证据边界](qa/CONTAINER_TERMINAL_BFF_UI.md)。下一步同源码原生PG/覆盖发布/真实BFF与浏览器验收；P1-11及全部76项保持进行中。

- 2026-10-16 runtime `a0b0001` 容器Exec Agent基础签收：专用UDS RPC/启动实例绑定/核心保护与固定驱动，676 Unit/92 Integration、15驱动策略、CI/Release及同源码冻结原生PG通过并清理。同源码真实探针验证临时非root容器UID/尺寸/退出/重放/断线与驱动强杀后Exec清理、PID1不变且验证容器精确删除；两次启动时序失败已修正。实际更新unit16:45:10 CST成功结束，原身份/宿主终端/通知/8资产/UDS/宿主及11核心健康通过，PKCS10.0.13和sudo保留。见 [本轮范围与证据](qa/CONTAINER_TERMINAL_AGENT.md)。容器BFF、UI、文件传输、团队隔离和长期清理等仍缺，完整76项保持active；下方为历史证据。

- 2026-10-16 runtime `1068b27` 最新覆盖签收：PKCS全链路及实际宿主自包含包均10.0.13，SDK10.0.402锁定；CI 38868581495全绿、646 Unit/92 Integration、171前端及3项生产授权浏览器门禁，冻结PG实际通过并清理。受限宿主终端WebUI、真实管理员中文/IME/主题/resize/退出及普通用户初始403已验收，5秒代理静默EOF已修复，实机静默6秒正常Close/持久化/重放通过。实际更新unit15:43:57 CST成功结束，身份/通知/8资产/UDS/宿主回归及11核心健康，sudo保留。见 [UI及失败记录](qa/HOST_TERMINAL_UI.md)、[容器与宿主安全补全](qa/DEPENDENCY_SECURITY_2026_10.md)。P1-11仍部分实现，容器Exec/文件传输/团队隔离/长期清理和全部76项继续；下方为历史证据。

- 2026-10-16 runtime `5bc2551` 签收受限宿主终端BFF后端：登录绑定/一次性30秒票据、持久元数据与事务审计、同源WebSocket、Ready激活/续租/有界关闭握手；642 Unit/92 Integration、Release、冻结原生PG通过并清理。真实OIDC/维护代理/Gateway/UDS/PTy、尺寸/exit/关闭/重放/撤销与普通匿名403/401通过，原任务/通知/资产/宿主回归及11核心健康、sudo保留。见 [失败记录和最终证据](qa/HOST_TERMINAL_BFF.md)。前端、容器Exec/文件传输、租户级隔离与长期清理仍缺，完整P1-11/76项不签完；下方runtime为历史。

- 2026-10-16 runtime `c9568e3` 签收宿主PTY基础：真实UDS鉴权/重放/重复会话、非特权身份/resize/退出码、断线和仅强杀测试driver后的setsid子进程清理、正文不进journal/记录均通过。610 Unit/86 Integration及Release通过，冻结原生PG实际执行通过并清理；覆盖更新unit成功结束、原任务/通知/身份/资产/宿主回归通过，11核心healthy、sudo保留。见 [终端基础证据](qa/HOST_TERMINAL_PTY.md)。BFF会话/前端/容器Exec及文件传输仍缺，不签完整P1-11或全部76项；下方runtime为历史。

- 2026-10-16 当前已签收runtime `1dd3fa5`：通用任务事件流先读状态再查事件、终态历史分批读空、浏览器原生游标续传/去重与订阅清理。591 Unit/86 Integration、146前端单元/27浏览器、Release/类型/lint/构建和冻结PG实际1006事件通过/清理；真实Gateway流中途提交终态/续传/尾部/400/404/401与清理、业务及权限/资产/UDS/宿主回归通过，11核心healthy、sudo保留。见 [证据](qa/JOB_EVENT_STREAM.md)。生产身份UI、完整取消恢复组合与全部76项未完成；下方运行时为历史。

- 2026-10-16 当前已签收runtime `df8f00a`：通知私有配置最终开始前取消复核、租约失效写入隔离、已有开始标记仍保留Unknown、持久确认离线重放和一次终态事件；591 Unit/73 Integration、133前端/23浏览器、Release/类型/lint/构建及冻结原生PG实际执行通过/清理。宿主覆盖更新成功，真实正向链路/已结束任务SSE续传、权限/8资产/UDS/维护入口/11核心健康通过，sudo保留、外部发送0。见 [证据与边界](qa/NOTIFICATION_PRIVATE_BOUNDARIES.md)。仍缺生产身份UI、通用实时SSE终态竞争及厂商端/完整重启演练，全部76项继续；本增量不签完整P4-06。

- 2026-10-16 当前runtime9d5775e已签收重启后维护状态修复：576 Unit/73 Integration、Release、冻结原生执行通过/清理与安装后二进制隔离HTTP测试通过，更新unit实际08:44:30成功结束；前一eabac51私有配置对应实际身份/API/Worker/Agent与8资产/宿主通过，临时资源精确清理、未启用发送。MariaDB启动挂载故障恢复、Docker存储挂载依赖已安装且未重启Docker。见 [当前P0证据](qa/BOOT_MAINTENANCE_STATUS.md)。生产身份UI、更多故障组合和实际重启演练仍未完成，全部76项继续。

- 2026-10-16 私有配置 WebUI 已完成本地实现与验证，20项联合浏览器、562 Unit/73 Integration及Release/类型/lint/前端构建通过；6原生本机跳过。两个已知工作站入口 SSH 超时，未调度本增量部署，不能算真实验收或新运行时签收。见 [UI恢复点](qa/NOTIFICATION_PRIVATE_UI.md)。继续原生/工作站验收及更多故障组合，P4-06与全部76项不缩减。

- 2026-10-14 runtime `39c990b` 验收通知私有配置后端工作流：561 Unit/73 Integration、原生PG事务/恢复、真实 OIDC→API→Valkey→Worker→Agent 应用/移除/永久重放及精确清理通过，渠道未启用/外部发送0，普通匿名新入口拒绝、迁移/健康/UDS/HTTP资源一致，sudo保留。详见 [后端流程](qa/NOTIFICATION_PRIVATE_WORKFLOW.md)。配置 WebUI 与更多故障组合仍缺，76项完整目标不缩减；下方 runtime 为历史。

- 2026-10-14 runtime `71b0b43` 已验收通知私有配置宿主基础、Agent 1.17 与固定原子 helper；553 Unit/73 Integration、18 条真实 Linux 权限/并发/断线恢复及原生依赖回归、真实受限 UDS 拒绝/读取/registry不变、OIDC/资源/最终更新与健康通过。首版只读挂载失败保留并已修正，sudo 保留。见 [宿主基础](qa/NOTIFICATION_PRIVATE_CONFIGURATION.md)；完整 API/UI、跨存储与审计仍缺，76项范围不缩减。项目图谱以 active 记录该模块，不能因宿主基础验收标整个 P4 完成。

- 2026-10-14 重连最新 runtime `fe3e034` 签收显式通知测试/Unknown 确认人工子记录：543 Unit/73 Integration、119 前端、18 联合浏览器及冻结原生 PG/实际签名接收后响应丢失/原历史不变/单子记录/TLS SMTP/清理通过。真实 OIDC 接口与新增权限/确认拒绝、8 HTTP资源相同、update journal 成功结束、维护 healthy、11 核心/宿主服务/动态入口/权限通过，sudo 保留。见 [证据](qa/NOTIFICATION_EXPLICIT_INTENTS.md)；下一步私有值 UI/Agent 写入，P4-06 和 76 项全量目标仍继续，以下 runtime 为历史。

- 2026-10-14 当前runtime6ad5970签收通知Sender子项，538 Unit/73 Integration、119前端、36浏览器及真实独立PG/签名HTTP/TLS SMTP接收、限流/轮换/尝试通过并清理；实际OIDC回归/8资产相同、unit成功终止/两migration落库/11健康。仍缺私有值WebUI/Agent安全写入、测试发送与未知确认重试、厂商批准端验收等；完整76项继续，P4-06未整项签收。见 [证据](qa/NOTIFICATION_SENDER.md)，下方运行时为历史。

- 2026-10-14 最新运行时e5b2cfc签收渠道/路由元数据配置和投递查询API/UI增量：498 Unit/73 Integration、119前端、36浏览器回归+空态4复测、Release/类型/lint/构建；独立原生管理事务/预算/幂等实际通过并清理，真实OIDC/API权限/CSRF/Origin/版本/OpenAPI及8个HTTP资源匹配通过。实际更新unit终态成功、11核心healthy、sudo保留。P0-07/P2-12/P4-06均未整体签完，外部传输/Sender Worker/私有值配置/签名仍缺，76项完整目标继续。见 [最新签收](qa/NOTIFICATION_MANAGEMENT.md#工作站最终签收)，下方运行时为历史。

- 2026-10-14最新runtime4ec6e86签收P0多人限流/额满会话可用性增量：认证后按主体限流、独立会话预算、429等待提示；476 Unit/73 Integration、113前端、30浏览器与实际Cookie同入口额度/权限/退出、原消息Worker回归及7分块HTTP核对通过。更新unit成功、11核心健康、sudo保留，见 [限流证据](qa/API_RATE_LIMITS.md)。不算P0-01/P0-08整体或76项完成；完整通知和其他阶段继续，下面runtime记录为历史。
- 2026-10-14重连最新runtime `ad0e661` 已签收：原bc39f46更新成功已核实，未重发；新源码3个原生套件分别真正执行通过并清理，100005全量消费/多主体有界清理/迁移/晚提交/兼容通过；覆盖发布后unit成功结束、新migration落库、11核心健康及真实身份/API/Worker/前端HTTP资产核对通过。详见 [最终签收](qa/NOTIFICATION_READ_SNAPSHOTS.md#重连后最终签收ad0e661)。完整外部渠道/管理UI等继续，76项范围与未完成状态不变；下方断线记录为历史。
- 2026-10-14 最新恢复点：工作站已断开，`bc39f46` 更新曾确认运行中、最终结果未知；最后已签收 runtime 仍为 `4aa036b`，不推断未签收版本已上线或因断线重发。GLM审查后继续本地成员有界清理/十万条完整消费和文档校正；476 Unit/64 Integration及Release通过、6原生明确跳过，新增源码尚未原生/生产签收。76项全量范围不变，见 [快照增量](qa/NOTIFICATION_READ_SNAPSHOTS.md) 与 [审查结论](qa/PLAN_REVIEW_20261014.md)。
- 2026-10-14 运行时 `4aa036b` 已签收站内消息中心与实时卡片修复：460 Unit/62 Integration、98 前端、42 本机浏览器和 Release 全方案通过；独立原生消息实际通过、真实 Authentik/API/Worker 普通/管理员隔离与读状态/触发恢复、四窗口 78 完整桶数据库比对及部署前端资源核验通过。更新 unit 成功结束、11 核心健康、临时资源清理，sudo 保留。外部渠道、通知投递与长期压力仍缺，P0-07/P2-12/P4-06 及 76 项整体不算完成；见 [消息证据](NOTIFICATIONS.md) 和 [卡片证据](qa/TELEMETRY_CURRENT_CARDS.md)。下方运行时为历史。
- 最新运行时 `3c26e13` 已签收负载/systemd/Docker 健康及独立离线评估；Agent 1.16、438 Unit/62 Integration、74 前端、11 浏览器、隔离原生 PostgreSQL与真实 Authentik/API/Worker 通过。32 核/4 负载/3 单元/11 容器实际对照、临时 Info 规则禁用/恢复，零生产故障制造；11 核心健康、sudo 保留。详见 [健康告警证据](qa/HEALTH_ALERTS.md)。P0-07 仍缺通知和长期压力等，76 项不因此完成。
- 单位修正运行时 `c671b7e` 已发布及真实只读预检验收；412 Unit/62 Integration、74 前端及 50 浏览器通过。真实悬空镜像 0 B，镜像/数据卷/容器清单未变；11 核心健康，Agent 1.15，未执行清理。详见 [单位证据](qa/DISPLAY_UNITS.md)。P0-08/P1-04 的一部分，不视作整项完成。
- 前阶段运行时 `57a94d3` 已签收利用率/状态告警修复和统一原子审计；402 Unit/62 Integration（3 原生本机跳过）、44 前端、47 项独立浏览器回归，独立 PostgreSQL 告警/备份容量与真实 Authentik/API/Worker 验收通过。11 核心健康、user sudo 保留；详见 [告警证据](qa/MONITORING_ALERTS.md)。P0-07 仍部分实现，其他 76 项不因此签收。
- 新版恢复后的运行时 `ad4f0d8`：root journal/清理 schema 修复；Linux helper 38 项与 PID 绑定 8 项通过，原生 SIGKILL/共享卷/失效计划/坏归档验收通过并清理，见 [P0 证据](qa/P0_RECOVERY.md)。以下断线时未签收的记录是历史，不能再当作本轮结果；但主机断电和全部组合仍未验证。
- 较早完整发布历史：`723ba55`，Agent 协议 1.15；源码后续验收脚本提交本身不等同再次部署。
- 上一完整验收：377 后端单元/62 现有集成测试、另行真实隔离 PostgreSQL 容量状态测试、43 前端单元、96 全页浏览器回归和最终 34 项容量/布局回归。不是新版 P0–P5 完成证据。
- 已具备身份、维护入口、任务、核心保护、概览/门户、基础容器/Compose、六类数据库/缓存、应用参数/备份/策略/保留、容量告警等主体；细项仍须按下表补齐。
- 本次新增 helper 测试 Linux 34 项通过；PID 绑定执行器本机 8 项通过。两轮原生共享卷/失效计划拒绝通过，但强杀执行器权限拒绝，**不能记为 SIGKILL 恢复通过**；第三轮原生验收在断线前尚无最终结果，损坏归档原生验收亦未签收。
- 恢复检查已完成：断线遗留 `wdv-backup-46b45bd7244f` 无验证进程、任务全终态、应用未暂停、无活跃专用归档 unit。另核对合成测试标记/Compose 项目和独占挂载后，经 API 卸载并精确删除它自己的保留数据/归档，历史标为 Expired；不触碰其他应用。源码 `8f4d889` 保留了受控补清理脚本。随后开始 P0 恢复类型校验修复；强杀/损坏归档仍以新一轮结果验收。

状态统一：`已验证`、`部分实现`、`待实现`、`待验收`、`外部启用待条件`。下表采用“部分实现/待实现”，不把整个模块因已有少数能力标成完成。后续每个任务记录对应提交、测试、部署和证据链接。
