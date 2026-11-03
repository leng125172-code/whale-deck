# 递归ACL内部内核候选

2026-11-04，新增内部递归ACL内核，尚未安装/helper/RPC/永久业务/Worker/API/Vue，不算真实身份页面或完整递归ACL交付。沿用有界FD树遍历/聚合回执，仅trusted类型构造选择ACL配置；原chmod/chown输入、散列域、返回结构与安装授权保持。

access使用显式fileEntries/directoryEntries分别定义文件/目录完整ACL；default要求fileEntries显式null，只修改目录（directoryEntries可为空表示清除默认ACL），文件仍清点并验证原access/noChange，不能写默认ACL到文件。每项保存原两类ACL/身份/属性，独立ContainerRecursiveAclMetadata与EntryMetadata/HDD命名空间，最深先执行、原子日志与逐项备份；Partial/Unknown不续写目标，观察恢复可写回裁定和未开始子项Discard围栏，不能称inspect完全无写入。深度32/数量10000/16MiB元数据/300秒预算、链接/跨挂载/特殊模式与扩展安全属性拒绝沿用。

20新专项（19实际隔离Linux文件系统/1生成规则）纳入489项Linux-root文件测试，0跳过；旧递归chmod24/chown16/单ACL22亦分别通过，8范围及严格语法通过。首次17项里1项替换inode夹具未补普通user属性，snapshot以ENODATA提前失败；修正属性/原ACL/属主/mtime，并pin旧FD防inode复用后通过，原失败不改记通过。真实工作站候选六场景（access完成/noop、default设置/清除、写后部分/写前标记两次候选SIGKILL）、HDD及独立核验工具已补，尚未运行。静态编排时的旧名称/loader索引/生成规则残留已在执行前复核纠正，不据语法成功当实机通过。

无协议minor/DB schema、公开接口、按钮或权限安装变化；源与历史同device时不得冒称异盘。来源完整故障矩阵、递归ACL全业务/历史恢复、可恢复删除/长期清理、维护连续性和其余59项继续，主项/阶段不升完成。[当前目标](../GOAL_P0_P5.md)。
