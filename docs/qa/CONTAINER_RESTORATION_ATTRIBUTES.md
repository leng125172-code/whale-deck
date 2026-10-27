# 容器历史属性恢复：固定发布引擎候选

最新安装852ca6f已通过发布准备私有副本中断/全局保护/不同管理员真实页面封存/原等待任务继续，以及明确容器删除后的真实10.448秒识别；普通属性与历史证据保留。详见[发布阶段与目标消失验收](CONTAINER_RESTORATION_PUBLICATION.md)。下方其他当前版本为前序批次。

最新安装态2e13376：发布前重新检查完整属性能力，实际排队/Worker进行中/Agent进行中重启三场景及当前网页4次回归通过。完整证据、两次观察器失败和权限拒绝收尾见[进程重启验收](CONTAINER_RESTORATION_RESTARTS.md)。下方其他“当前版本”为各前序批次。

## 当前安装态：d8822c6 的显式策略与真实网页属性恢复通过

2026-10-27，正常 dev 覆盖 `whaledeck-dev-d8822c6.service` 成功退出，3分12.767秒；六组件及实际 HTTP 来源均为 `d8822c678a750b3eef64a4ebe100536e6d4158f7`，minor43与运行库/PKCS10.0.13，权限/UDS/动态入口与HDD策略检查通过。`20261027110751_ContainerRestorationAttributePolicy` 已实际应用。没有自动回滚。

页面入口：容器详情 → 容器文件 → 历史版本。默认不勾选“同时恢复历史普通属性”，仅恢复内容；勾选后，确认框与任务展示“内容与历史普通属性”。计划显式回显 true，选项在确认、登记重试和活动任务中冻结；服务未回显或任务丢失该字段时拒绝继续，不静默退回内容恢复。

真实临时管理员正常可见登录，在1440明色/320暗色各执行原路径与其他路径新建、覆盖，共4次属性恢复，每份2097159字节；真实BFF普通上传生成可信HDD来源，实际停止再启动独占测试容器后恢复原捕获。来源属性为UID1001/GID2002、0640、1700000000123456789纳秒mtime；当前文件先改为UID1000/GID1000、0600、1750000000987654321纳秒mtime，四次恢复分别使用两种不同历史属性。SQL不可变true策略/来源目录、Root schema4/签名来源/派生4属性、完整字节、旧inode与HDD捕获逐项匹配；最终两个路径内容及4属性独立核对。浏览器文件内容请求0；确认取消登记0；正常跨页双端退出和登录态拒绝通过。

证据：`deploy/artifacts/container-restore-browser-5006a0203b434674a9defd6b3225cf87/`（baseline/results/verified/independent-final、8幅实际截图），日志 `/tmp/wd-container-restore-policy-live.log`。实际普通用户5条路由403、匿名401且零新增意图，临时身份精确清理。随后默认内容恢复另4次同等浏览器整链通过，Root仍schema1且无属性来源，证据 `deploy/artifacts/container-restore-browser-cc9f290d1cc84c4392c06c85cfee26fc/`，日志 `/tmp/wd-container-restore-policy-live-legacy.log`。

独立新进程重核8恢复意图与Root/完整2097159字节暂存、精确CID明确不存在及源目录不存在；三类临时身份计数0、11核心healthy、两宿主服务active。永久业务现55意图/40成功/18恢复，其中属性4；活动/Unknown/租约/发布屏障均0。审计、Root与HDD历史保留。首轮独立清理检查只因Docker错误文字大小写不匹配失败，核对实际“no such object”后修观察断言重跑通过；没有改产品状态或弱化不存在性要求。

本批Release零警告错误，2717 Unit、199 Integration（7原生默认跳过）、496前端单元/lint/构建、13历史浏览器夹具通过；冻结0b501f3真实PG1执行/1通过/0跳过，清理通过，下节详述。d8822c6 CI `40501605203` 后端/原生PG/原生Valkey/安装器四路success，完整前端尚运行；此前091e6c7与b9685da已全success，不据此冒称本版全部CI已通过。

**仍未完成**：ACL/xattr/特殊位/历史atime、重建CID授权、长期历史清理及进行中Worker/Agent重启/发布故障等完整组合。此次仅完成普通4属性的显式业务策略/页面与实际链路；完整59项保持active，17项移出不算完成。下方“未部署/待页面”为前序候选历史。


## 冻结原生 PostgreSQL 候选通过，待覆盖部署

冻结 `0b501f3160711c82372c2dc6f997a8d5607bda0e` 真实隔离 PostgreSQL 一项执行/一项通过/零跳过，临时数据库与SDK容器清理确认通过。状态 `deploy/artifacts/native-status/dispatch-7db6065b20264fc49cbb225eba066910.json`，日志 `/tmp/wd-container-restore-policy-native.log`。其中从旧恢复来源结构向前升级，实际旧列 INSERT 的恢复记录在新增 false 默认值后 JSON/MAC 不变；无来源 true 被数据库 check 拒绝；属性任务真实持久重取与内容任务取消均执行。此为真实库验证，不等同工作站 API/Worker/网页已部署。

## 显式属性策略与页面候选（尚未部署）

已加入不可变 `RestoreAttributes` 策略：默认只恢复内容，勾选才在短时计划、永久意图/MAC、任务查询与签名 Stage 中冻结历史普通属性。新计划显式回显 true，旧服务忽略该选项时前端拒绝继续；登记重试与轮询不得改变或丢失策略。普通旧记录的 false 字段不参与原 JSON/MAC，新增 EF 迁移默认 false，并约束属性恢复必须具有可信历史来源。计划、登记、Worker 接收前均检查完整 minor43 能力，浏览器没有内容或任意 UID/GID 输入入口。

本机 Release 零警告错误，2717 Unit、199 Integration 通过（7 真实依赖默认跳过）；496 前端单元、lint、生产构建及 13 历史恢复浏览器夹具通过。首轮新增 4 浏览器用例误点 Element Plus 隐藏原生 input，改用用户可见标签点击并核对 checkbox，保留首次失败日志。实际旧库升级/原生 PostgreSQL、工作站覆盖部署及普通属性 UI→SQL→Worker→Root→HDD 整链尚待，本节不能证明安装态已交付。


最新宿主安装态已更新到06b6eed/minor43并通过实际签名属性恢复，两次2097159字节及普通4属性/Root/HDD/原inode核验通过；API/Worker/Gateway仍5fd8d3b，业务属性策略与页面尚待。见[签名RPC实机记录](CONTAINER_RESTORATION_RPC.md)。下方“未安装”对应前序内部引擎候选。


## 最新冻结00f50b7复验通过，产品仍未安装属性能力

20专项/251容器Linux-root联合测试通过，长路径读取及封存回执限额修正已包含；Python语法通过。冻结`00f50b724a6ec14cd453608ceab936fe42a16a5a`的9模块实际Docker/物理HDD再验通过，证据`deploy/artifacts/container-attributes-native-f9b298da017f4f22a3a57b9c7bff8f22/`，日志`/tmp/wd-container-restore-attributes-native-3.log`。真实普通属性恢复/实际测试容器重启/原路径及其他路径完整2097159字节/旧inode与HDD匹配，错误摘要拒绝；新进程独立核对全部冻结SHA、精确CID no-such-object、源/HDD沙箱/bundle均不存在。

在位两个写引擎逐SHA仍为5fd8d3b，新属性模块未安装。生产39意图、活动/Unknown/租约/屏障均0，11核心healthy、宿主服务正常；未改生产业务数据、权限或容器生命周期。本轮只完成内部属性发布基础与实际内部门禁，签名RPC/业务属性策略/迁移/页面及真实业务失败恢复仍待。下方“同版物理HDD待复验”为中间候选历史，不代表最新状态。

## 新增长路径回执边界（尚待同版物理HDD复验）

属性恢复同时保存来源和目标，合法4095字节路径经JSON转义后可超过原16KiB journal预算。新增仅schema4及包裹schema4的封存允许32KiB的独立读取/保存预算；其他容器与宿主回执仍16KiB。读取保留root/0600/单链接/完整有界读取及变化检查。长路径首轮捕获封存端另有16KiB硬限制，已同步修正；失败日志保留，未截断来源或放宽普通记录。20专项/251容器联合Linux-root全部通过，包含两条4095字节转义路径的读取/封存重放、旧记录限额和32KiB以上拒绝。物理HDD内部门禁须按更新后引擎再次核对；下方为6e088f4的前序实测。

## 实际内部门禁：冻结引擎6e088f4通过，未安装

冻结9个引擎文件SHA逐一核对通过；独占无网络、非root容器配置及真实物理HDD沙箱实测通过。仅停止/启动该测试容器后读取旧捕获，将原始内容与UID1001/GID2002、0640及1700000000123456789纳秒mtime恢复到原路径，再恢复到另一新路径；每份2097159字节。独立Root schema4/属性快照、原被覆盖inode/完整HDD副本、来源哈希和最终字节/全部4项普通属性匹配，错误捕获摘要拒绝。

第一次实际验证通过，证据`container-attributes-native-2ec63b64362a430abc756283389994a3`。为补足验证后独立核对的目标身份，观察器增加明确CID/源目录基线后再次实测通过：`deploy/artifacts/container-attributes-native-6848f52361b54fd488acb9f3a166b96c/`，含baseline/verified/journals/independent-final；完整日志`/tmp/wd-container-restore-attributes-native-2.log`。第二次仍为相同9个冻结引擎SHA，仅观察器补身份记录。新进程独立核对精确CID明确不存在，源/HDD沙箱/候选bundle均不存在，模块SHA仍匹配。

没有发布产品或安装新helper/RPC；在位上传/发布引擎SHA与5fd8d3b逐一匹配，新属性模块不存在于安装目录。11核心healthy，Agent/维护服务active。生产意图总数仍39，活动/Unknown/租约/屏障0；不生成产品恢复任务/SQL数据。当前5fd8d3b CI四路success、前端完整组仍运行，未据此签完整CI成功。

下面的候选说明与本机测试仍适用；其中“实际HDD待验证”由本节通过结果取代。完整业务/页面属性恢复尚未实现，继续签名协议、能力协商与持久属性策略。


2026-10-27。本批是内部引擎候选，尚无签名RPC、永久业务字段/迁移、页面选项或安装态验收。运行时仍为5fd8d3b/minor42；原路径及其他路径内容恢复保持原目标属性策略。不得将内部引擎测试称为产品属性恢复已完成。

新增`whaledeck-container-restore.py`，仅由可信组合调用，无CLI。Stage的`restoreSource`只能包含同CID历史来源的captureId/captureHash/path，不接受UID/GID/mode等任意属性。引擎经现有只读HDD完整验证器核对捕获域、来源路径/摘要、完整内容与大小，然后从原sourceIdentity派生UID/GID、普通权限位和纳秒mtime。当前容器uid_map/gid_map须覆盖原始宿主ID，调用前后复核当前实例；特殊位不静默降级，尚不支持setuid/setgid/sticky、ACL/xattr及历史atime。完整属性范围仍未交付。

专用恢复暂存使用schema4/resourceType ContainerFileRestoration与独立指纹域，属性快照在root私有journal中冻结。普通schema1上传、取消schema2、封存schema3格式不变；旧冻结reader拒绝schema4，迟到旧请求不能接管新恢复。重新观察/正面Staged重放不再次读取历史来源；管理员封存完整保留来源与属性原journal。

属性在私有完整文件上设置和逐字段复核后才进入原子renameat2；精度丢失、未映射所有者、来源/摘要变化拒绝，不覆盖目标。发布前再次校验当前ID映射。原目标及HDD旧内容仍由原完整备份/保留inode机制保护。

首轮专项17例中有1例失败：utime异常前chown/chmod已发生，而旧显式清理只认可root所有者的未完成文件。已增加修改属性前的fsync和持久完整inode/内容证明；中断清理必须核对准确inode、单链接、完整长度/SHA，不凭文件名或属性放宽删除。缺证据或字节变化保留现场。修后专项通过，后增引擎丢失时禁止备份/发布保护。

当前门禁：18个专项、容器联合249项真实Linux-root测试零跳过通过，含属性三系统调用之后与交换前后共5次真实SIGKILL；原上传、取消、封存、历史、只读与既有发布回归全部通过。12宿主安装边界检查通过，Python语法通过。命令日志`/tmp/wd-container-restore-attributes-all-root-2.log`与首轮失败日志保留。这些测试的HDD/namespace是明确夹具；冻结引擎对独占实际Docker容器/物理HDD的验证另行记录，尚未据此签产品链路。

下一步：实际HDD内部门禁；再独立签名协议和能力协商、持久业务的显式属性策略/旧MAC兼容、EF迁移、恢复页面选择、安装与真实权限/正常退出、Worker/Agent和发布故障。重建CID授权、长期历史清理和其他59项继续，17项移出不计完成。
