# 容器历史属性恢复：固定发布引擎候选

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
