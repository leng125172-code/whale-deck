# ACL 异常处置中的历史恢复来源：候选实现与验收边界

## 状态

2026-11-04，接续 `5db885413bd888c0159abbc767672ccb0928df4f`。本记录随候选实现提交，**尚未部署，未进行新的真实 Authentik→Vue→API/Worker→Agent→文件/HDD整链验收**。既有写后实例变化的实机证据见[前批记录](CONTAINER_ACL_RESTORE_INSTANCE_CHANGE.md)，不会重放这些已完成的真实操作来补候选页面证据。

只读固定六组件 probe 返回运行来源 `b69302b8cb32048038d7dbeed193af205c70fd11`；Agent/维护服务 active，PID分别1772448/1688513。本批没有执行发布、迁移、容器写入、Agent重启或权限配置变更。没有重新签收11核心健康、完整HDD或7配置边界；前批结果仍属于前批。

## 实现和权限

新增 `GET /api/v1/files/container-acls/recovery/{id}/restore-source`，与原恢复owner接口分开。仅当前实时管理员可读取：仓储读取前后再次授权，校验原请求/计划/回执完整性后，读取持久化的不可变来源快照，投影以下7个字段。

| 字段 | 含义 |
|---|---|
| captureId | 登记时选择的旧ACL捕获编号，不是本次执行ID |
| changedAclType | access或default |
| kind | 来源对象为文件或目录 |
| createdAtUnixMs | 历史来源时间，合法0保留 |
| originalMode | 来源普通权限位 |
| accessEntryCount | 原access条目数 |
| defaultEntryCount | 原default条目数，显式0保留 |

合法普通ACL修改返回HTTP200、JSON `null`；不使用204、缺字段或错误响应表示普通任务。丢失意图、损坏来源、MAC不一致直接失败；不把错误降级为null。端点沿用no-store。另一当前管理员可读有限摘要，原owner `/restores/{id}` 对另一Subject或登录仍404；匿名401、普通用户403、实时撤权403。

摘要不输出完整Source、Location、SourceInstanceId、ACL数组、digest、身份或授权hash。它是登记时的历史事实，读取不依赖HDD当前可用；**不能据此推断HDD原件仍存在、当前文件状态、写入或恢复成功**。处置后仍可读取同一摘要以核对原回执。没有新增schema/EF迁移、RPC、capability、sudo、写入权限，普通Intent DTO及Job安全摘要保持原格式。

## 页面和失败恢复

管理员异常处置面板新增紧凑来源卡片，展示捕获编号、类型、时间、原模式和两类条目数，并明确历史含义；处置确认区域再次显示来源编号。普通任务只在端点明确返回null时标为普通ACL修改。Vue文本输出，无HTML插入。

最多50行、4个并发来源读取，整批来源核实后才展示可处置列表。任一来源读取失败或不规范会中止同批请求并显示错误，不显示假空列表、普通任务或可确认行。关闭、换身份、撤权、卸载中止请求并清空引用；请求代次阻止迟到响应重新填充或继续写入。

摘要严格校验字段白名单、编号、类型、时间、模式和条目预算，并与原意图的ACL类型、对象类型、目标条目数及适用模式比对。确认前重新读取逐字段比较；确认后核对原任务与同一摘要，再显示处置回执。摘要出现、消失、改变或不可读均不报成功。响应丢失只查询原任务与原来源，不自动重发POST，不生成新键；原Unknown的Root事实和null确认保持既有语义。

## 候选验证

- Release全解决方案构建：0警告、0错误。
- 后端全量：4368项单元测试通过；490项集成测试通过、7项默认原生依赖测试跳过。新增HTTP用例使用实际MVC/Cookie/CSRF，身份、仓储及历史/Agent为显式夹具；不是新的真实PostgreSQL、Authentik或HDD证据。
- 前端全量：59个文件、1087项单元测试通过。类型检查、生产构建、lint和格式检查通过；新增28项来源解析、不可变比较、50行/4并发、失败中止与退出测试。
- 首组13项生产预览浏览器测试通过，包含320×600明暗来源卡片、普通Unknown兼容、确认前变化、确认后来源消失、503/不规范/403拒绝。扩展后全部ACL与历史恢复页面68项通过（5.8分钟），包括丢失确认响应仅查原任务、关闭后的迟到来源响应不得回填；本机无5080服务的收尾代理ECONNREFUSED保留，不作为生产连通/故障证据。
- 本机5175实际HTML与9份引用入口资源和dist逐字节相同；已查看320明暗来源卡片PNG。页面接口为测试夹具，截图不能当作生产SSO/文件操作证据。
- 59项范围一致性8项通过，1725条本地相对Markdown链接无缺失。目标第1节逐字保持不变，不调整主任务完成率。

证据目录：`deploy/artifacts/acl-recovery-source-candidate-20261104/`，原候选PNG：`deploy/artifacts/ui-candidates/acl-recovery-source/`。本地证据不提交到仓库；保存最终日志、源码/截图摘要及构建来源，失败和修正单列。

## 失败及修正

首次脚本工作目录与前缀不一致，以及fixture扩展名误写为.ts，分别在创建/读取前失败；核对.mjs后继续，已保存的修改没有回退或重复真实操作。前端旧编译目标不支持replaceAll，改为正则替换；Vitest beforeEach误返回mock函数，被当作清理回调执行而产生2项假失败，改为无返回代码块。新增测试补齐mock泛型和错误消息后lint通过。

初轮后端用例直接把测试行改为Acknowledged触发原完整性保护，改成真实走Unknown→明确处置业务流，未放宽校验。HTTP新增另一管理员用例遭测试Directory仅承认固定Subject而403，改为显式可设置的额外测试管理员，默认行为保持；CA1861常量数组检查改为静态只读字段。修正后全量通过。只读运行probe输出为纯SHA，首次按JSON解析失败，改为保留文本，未触及实际部署或状态。

## 未验收和下一步

候选须取得准确CI成功终态，再标准覆盖发布；使用全新自有资源和真实Authentik身份，验证来源卡片、明确处置、原owner接口404、文件/HDD原件与不可变原回执、正常退出、准确清理及独立核验。不能把本轮夹具页面接到前批实盘证据上，声称新增来源UI已经真实签收。

来源丢失/替换等真实组合、其它共享写入与磁盘保护、递归ACL与递归恢复、可恢复删除和长期清理及其余59主任务继续。整体ACL、文件管理或P0–P5阶段均未新增完整签收；17项移出不算完成。


## 接续：四镜像预构建与来源UI实机门禁候选（2026-11-04，未发布）

冻结产品仍为`28062da454fd579d6860391a88816dab0bfca143`，四镜像标准build-images.sh退出0，API/Worker/Migrator/Gateway实际镜像ID、OCI revision和隔离只读无网络CLI内部版本均匹配。运行checkout仅由user干净快进至该准确公开提交；两次GitHub TLS抓取失败后，使用已经远端核对的本机Git对象传输，没有复制私有配置或工作区未提交文件。

发布前与构建后只读核对：11核心healthy，全部ID/镜像/StartedAt保持；两宿主1772448/1688513 active、实际exe与映射DLL核验通过。7配置/能力密钥/sudo规则SHA/uid/gid/mode、可信账本、135份ACL HDD元数据字节SHA和九字段保持，活动Job/终端/锁/屏障/未完成意图为0。无发布、迁移、服务重启、新身份/文件写入或自动回滚。

**六组件总probe在预构建后拒绝，不签其成功。** 三个旧运行镜像ID的image inspect均返回1，候选Migrator镜像也已属于28062da；严格probe返回PLATFORM_REVISION_UNPROVEN。此时分别核对运行容器内CLI/容器自身来源标签与两宿主实际进程，五个运行组件仍为b69302b；四个待发布镜像另行核对为28062da。未重标旧镜像、修改探针或账本来伪造一致。标准发布后仍须总probe六组件一致及安装validator通过，当前不能把预构建当已部署。

实机工具新增显式`source-ui`参数：`validate_container_acl_restore_unknown.py <product-sha> source-ui`仍只执行原profile7目标移除；显式`post-instance source-ui`才是原profile8单个自有容器重启。新增`recoverySourceUiVersion=1`和private proofVersion7，原profile7/proof5、profile8/proof6及普通Unknown proof2/3/4保持独立、可读，不改Root故障权限或枚举。未知、混合、颠倒参数和普通ACL请求来源UI均在动作前拒绝。

新证明要求页面实际发出的3次摘要GET（加载、确认前、确认后），均200/no-store，7字段逐值/逐类型等于原已验证捕获来源；卡片全部值/历史说明、捕获编号/时间、确认区同源编号在320×600暗色视口内且无横向溢出，保存两张新增真实截图。来源不规范、增字段、缺失/替换、错误状态、缺no-store、旧证明冒签和确认区不一致均拒绝。保留另一管理员owner恢复接口404、原Root Unknown/null确认、HDD与来源不改、明确ack一次、等待目录自然延期后释放、正常双端退出和准确清理/独立复核。另要求全新普通Authentik身份HTTP403、独立匿名401、普通身份正常HTTP双端退出，不能把它说成普通用户Vue页面验收。

本轮126项相关检查全部通过且无跳过：22普通Unknown、14恢复Unknown、28恢复来源、44 Linux-root范围、10恢复浏览器证明、8范围；Python警告即错误编译、JS语法及git diff通过。首轮2个新测试因夹具未放入完整restoreSource而KeyError，修正夹具后通过；没有放宽产品或证明验证。边界脚本首次把固定history.lock当元数据拒绝，已改为显式核对其root/0600普通文件并排除计数；未知条目仍拒绝。以上均是候选工具和只读部署准备，**新source-ui实机门禁尚未执行**，不拼接旧9张实盘图冒充来源卡片。

准确产品CI run41681017703目前backend/native-postgres/native-valkey/installer成功，frontend仍在运行，不签完整CI终态。下一步待准确成功后唯一标准覆盖，再用本批冻结工具和新资源进行真实来源UI整链。当前MD/阶段仍部分实现，59主项完成率不升。证据：`deploy/artifacts/acl-recovery-source-release-28062da/`，包含发布前/构建后只读边界、四镜像来源、构建日志及126检查；凭据只在原私有环境，不进仓库或日志。


## 接续：确认前后真实网络顺序补强（2026-11-04，未发布）

实机前只读复核发现，cf2df2d候选虽然要求首次1次、最终3次摘要GET且值一致，但累计数无法独立排除“第二次GET被错误挪到确认后”。尚无source-ui实机批次，不把旧计数候选写成完整顺序验收。

现新增页面实际request/response事件序列，限定准确原任务的摘要GET及处置POST：加载GET请求/响应→确认前GET请求/响应→原ack请求/成功响应→确认后GET请求/响应。流程与独立只读证明均要求完整8事件精确顺序，累计3次值/类型及200/no-store仍逐项核对；把ack挪到第二次读取完成前、把第三次读取挪到ack成功响应前、重复/缺事件或三个GET全部放到ack前的反例均拒绝。没有修改产品28062da、权限或Root动作，旧真实proof5/6不要求新增来源字段。新source-ui证明仍只是候选，待准确CI与部署后新资源实测。

本轮128项相关检查无跳过通过（22普通Unknown、16恢复Unknown、28恢复来源、44 Linux-root范围、10恢复浏览器证明、8范围），JS语法/Python警告即错误编译及git diff通过。没有重跑前批4368/490/1087/68项产品测试来虚增本批结果。

另外保存了仅用于本次准确发布unit的只读观察器源码与SHA：9个状态分类检查通过，并以真实/maintenance/status验证200、healthy/Ready/no-store，确认whaledeck-dev-publish-28062da.service尚不存在。观察器不启动、停止或重发unit，不写生产状态/账本；计划在发布前进入就绪态，再按唯一Invocation记录全窗口HTTP样本与真实终态。特别拒绝把activating时的Result=success/ExecMainStatus=0判为结束；Invocation变化、unit提前消失和非终态也不能记通过。**本批观察器尚未运行发布窗口，不签持续可用**；隔离分类检查不算部署或维护故障实机验收。

产品CI run41681017703仍4成功、frontend的容器布局回归进行中；运行仍b69302b、四候选镜像28062da，未触发发布或真实登录/文件写入。证据继续在root私有`deploy/artifacts/acl-recovery-source-release-28062da/`，新增checks-128-order.log、publication-observer-source.py及只读预检回执；旧126检查、预构建与失败记录不覆盖。待完整CI成功后标准覆盖及真实来源UI/文件HDD整链，再来源异常、递归ACL/恢复、删除等59项；17项移出不算完成。


## 接续：两宿主预构建与来源故障缺口（2026-11-04，未安装）

对冻结28062da另行执行标准`deploy/host/publish.sh`，退出0，仅输出至`/data/GitRepos/whale-deck/deploy/artifacts/source-ui-host-prebuild-28062da/`，**未调用install.sh或publish-and-install.sh**。Agent/MaintenanceHost实际自包含CLI内部来源均28062da，SHA256SUMS严格核对通过；标准运行库校验器确认两者Microsoft.NETCore.App/Microsoft.AspNetCore.App及PKCS均10.0.13。结合前批四镜像，六个候选产物分别已核对；这不是六组件实际安装一致，总probe也未改写为成功。

新来源故障矩阵明确Queue/原key、plan、confirm、首次apply及已有原回执的边界。新增同digest但完整来源时间戳被替换的三边界拒绝、IO读取暂不可用后三处原身份/执行ID/期限继续，以及Completed/Unknown/Aborted优先对账；相关业务121项全通过（净增8），没有扩大权限或修改产品代码。**身份、仓储、来源及Agent均为显式夹具，无新增真实PG、Authentik或HDD来源丢失/替换证据**。源失联不等于源已变更，已有原回执不因源不可读重做；Discard原ID可留Aborted记录，不能误记为没有Root调用。详见[事实、预期与待实机矩阵](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)。

预构建后继续核对运行五组件b69302b、11核心healthy及原ID/镜像/StartedAt、宿主1772448/1688513、7受保护配置/账本/135HDD九字段字节保持，所有活动项0；无发布/迁移/服务重启或真实来源故障。产品CI四路成功，前端已进入最后的生产权限/文件/终端回归组，仍未终态成功，发布unit尚未启动。后续先完成来源UI部署/实机，再执行新来源故障矩阵，不以这些夹具测试替代。

原件仍在`deploy/artifacts/acl-recovery-source-release-28062da/`，新增host-prebuild.log、host-prebuild-provenance.json及after-host-prebuild.json，原预构建、126/128检查和失败记录保持；新业务测试日志另存。目标第1节与59项不变，17项移出不算完成。


## 后续：准确CI成功与发布前接续

2026-11-04，准确产品28062da的CI41681017703已完成，backend/frontend/installer/native-postgres/native-valkey全部completed/success，完整JSON回执保存后逐headSha与五个名称核对。前述各时点“前端仍运行”保留历史事实。四镜像与两宿主候选仍冻结28062da；尚未启动标准发布时不能提前签已部署或六组件实际一致。

原串行前端持续较久，后续CI候选改为原41文件的6个独立分片，保留单一frontend汇总及全部检查，覆盖与失败门禁见[CI分片证明](FRONTEND_CI_SHARDS.md)。本地原409项末组额外完整回归正在运行；新分片只收集749项/六片无重无漏，不冒充执行通过。来源故障另新增2个原deadline到期用例，相关业务123通过，详见[来源矩阵接续](CONTAINER_ACL_RESTORE_SOURCE_FAILURES.md)。这些改动没有修改产品28062da，下一步唯一标准覆盖及新资源真实来源UI整链。


## 实际后续（2026-11-04）

以上候选/等待/失败时点原文保持。随后唯一标准覆盖28062da成功、六组件/安装核验通过，新资源真实来源卡片/8网络事件/权限/跨管理员处置及文件HDD、独立复核和准确清理通过；另以新管理员只读验证普通200 JSON null/恢复7字段/owner404。维护入口159样本中1次ConnectionError，不能签发布全窗口连续；本地409预览回归通过仍非实机。见[实际发布、原件及未解决边界](CONTAINER_ACL_RECOVERY_SOURCE_REAL_UI.md)。
