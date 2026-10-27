# 容器历史属性恢复：固定发布引擎候选

2026-10-27。本批是内部引擎候选，尚无签名RPC、永久业务字段/迁移、页面选项或安装态验收。运行时仍为5fd8d3b/minor42；原路径及其他路径内容恢复保持原目标属性策略。不得将内部引擎测试称为产品属性恢复已完成。

新增`whaledeck-container-restore.py`，仅由可信组合调用，无CLI。Stage的`restoreSource`只能包含同CID历史来源的captureId/captureHash/path，不接受UID/GID/mode等任意属性。引擎经现有只读HDD完整验证器核对捕获域、来源路径/摘要、完整内容与大小，然后从原sourceIdentity派生UID/GID、普通权限位和纳秒mtime。当前容器uid_map/gid_map须覆盖原始宿主ID，调用前后复核当前实例；特殊位不静默降级，尚不支持setuid/setgid/sticky、ACL/xattr及历史atime。完整属性范围仍未交付。

专用恢复暂存使用schema4/resourceType ContainerFileRestoration与独立指纹域，属性快照在root私有journal中冻结。普通schema1上传、取消schema2、封存schema3格式不变；旧冻结reader拒绝schema4，迟到旧请求不能接管新恢复。重新观察/正面Staged重放不再次读取历史来源；管理员封存完整保留来源与属性原journal。

属性在私有完整文件上设置和逐字段复核后才进入原子renameat2；精度丢失、未映射所有者、来源/摘要变化拒绝，不覆盖目标。发布前再次校验当前ID映射。原目标及HDD旧内容仍由原完整备份/保留inode机制保护。

首轮专项17例中有1例失败：utime异常前chown/chmod已发生，而旧显式清理只认可root所有者的未完成文件。已增加修改属性前的fsync和持久完整inode/内容证明；中断清理必须核对准确inode、单链接、完整长度/SHA，不凭文件名或属性放宽删除。缺证据或字节变化保留现场。修后专项通过，后增引擎丢失时禁止备份/发布保护。

当前门禁：18个专项、容器联合249项真实Linux-root测试零跳过通过，含属性三系统调用之后与交换前后共5次真实SIGKILL；原上传、取消、封存、历史、只读与既有发布回归全部通过。12宿主安装边界检查通过，Python语法通过。命令日志`/tmp/wd-container-restore-attributes-all-root-2.log`与首轮失败日志保留。这些测试的HDD/namespace是明确夹具；冻结引擎对独占实际Docker容器/物理HDD的验证另行记录，尚未据此签产品链路。

下一步：实际HDD内部门禁；再独立签名协议和能力协商、持久业务的显式属性策略/旧MAC兼容、EF迁移、恢复页面选择、安装与真实权限/正常退出、Worker/Agent和发布故障。重建CID授权、长期历史清理和其他59项继续，17项移出不计完成。
