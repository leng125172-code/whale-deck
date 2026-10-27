# 容器历史属性恢复：minor43签名Stage候选

## 当前宿主安装态：06b6eed/minor43签名属性恢复已实测

`whaledeck-host-06b6eed.service`正常0/SUCCESS，46.880秒。Agent及MaintenanceHost实际来源`06b6eed2387351561c56335fe6d145e7f3c76fcb`、运行库/PKCS10.0.13；新模块/固定helper逐字节匹配冻结提交，宿主权限/UDS/动态入口检查通过。API/Worker/Gateway仍5fd8d3b，不声称六组件同版或已开放业务属性恢复。

同提交冻结self-contained原生客户端在实际安装态独立签名：真实BFF普通上传先生成历史UID1001/GID2002、0640和1700000000123456789纳秒mtime的捕获；实际仅重启独占测试容器后，ReadSource严格读完2097159字节与末帧/EOF，再按新签名Stage/Publish/Inspect恢复原路径及另一新路径，各2097159字节。来源字段篡改使MAC失效、Stage签名重放拒绝、有效签名错误捕获摘要返回Aborted。Python独立核对schema4属性快照/Root完整暂存、最终字节/4属性、被覆盖inode/完整HDD、副本来源元数据与数据指纹保持不变。

证据`deploy/artifacts/container-restoration-agent-9cf95887b0504471b4452bea1f9e45f3/`，日志`/tmp/wd-container-restore-rpc-installed-2.log`。初轮验证误传相对客户端路径和错误版本值，在入口路径校验时失败，尚未创建测试容器；临时身份已清理。更正为实际完整版本和绝对路径后通过。首次发布客户端脚本因仓库100644模式不能直接执行，改用bash正常冻结发布，无修改全局权限。

最终新进程独立核对精确CID no-such-object与源目录不存在、三类临时身份0、11核心healthy、两宿主服务active。生产40意图25成功，既有恢复6，活动/Unknown/租约/发布屏障0。新增仅1个实际普通上传Job；两次原生属性发布明确不生成产品业务Job，已独立SQL验证这两ID不在业务意图中，Root/HDD和初始Job/审计按设计保留。

Release零警告错误、2704 Unit/199 Integration（7原生默认跳过）、251 Linux-root和12宿主检查通过。06b6eed CI后端/真实PG/Valkey成功，前端仍运行；安装器被前一批元数据nextStep遗漏“17项移出不算完成”的范围约束挡住，已补回准确范围并通过8项范围测试，后续CI仍待终态。没有放宽测试或恢复已移出范围。下方“尚未安装/尚未执行”是本批前序候选记录。

下一步是业务意图显式属性策略、计划/旧MAC兼容、EF迁移、Worker分流及页面选择，再验证实际管理员/普通与退出/进行中撤销/重启故障；ACL/xattr/特殊位、重建CID映射和长期清理仍缺。本子门禁不签整个属性恢复或59项完成。


2026-10-27。本批协议/固定helper/SDK/安装配置已实现，尚未安装或执行新的签名UDS实测。生产仍5fd8d3b/minor42。业务永久意图的显式属性策略、计划/旧MAC兼容、EF迁移、前端选项和真实业务恢复链尚待，不能将本协议子项计为完整属性恢复。

protobuf ContainerUploadHeader新增可选字段10 restore_source，只有capture_id/capture_hash/path。原header字段及普通上传的Root JSON完全保持；属性来源与目标路径独立，请求全字节指纹及单次capability包括来源全部字段。SDK发内容前必须协商major1/minor43和docker.files.restore-attributes及已有两项历史能力；低版、未知major和缺少能力拒绝，不枚举来源内容。Agent重复校验来源语法，然后仅投影固定字段至helper；不接受任意UID/GID/mode。

固定helper加载root所有的新属性模块并从完整验证的HDD捕获派生属性，使用schema4和前序完整发布/持久取消/封存保护。新来源错误经过固定白名单映射为稳定的权限/冲突/前提错误，不回传任意异常。安装脚本与宿主校验增加新模块，Agent声明minor43独立能力。

本地门禁：Release零警告错误；2704 Unit/199 Integration通过（7专用原生夹具默认跳过），251 Linux-root和12宿主安装边界通过，Python/Shell语法通过。首轮6个旧能力断言仍固定42导致失败，明确更新为43；42拒绝新属性能力的降级用例原样保留。旧普通header仍10个Root字段，无新来源字段；无业务schema或原意图MAC变更。

新增冻结原生客户端与验收脚本：实际BFF普通上传生成旧属性捕获，只重启自有测试容器，然后独立签名ReadSource/Stage/Publish/Inspect，验证来源字段改动不匹配MAC、签名重放拒绝、有效签名错误捕获摘要拒绝，以及原路径和其他路径各2097159字节的属性恢复；Python独立核对Root/HDD/旧inode和4属性。此脚本尚未执行，不冒称已有真实结果。验证保留原始正常上传Job/审计、Root和HDD，精确清理自有容器/源，不进行生产数据写入或自动回滚。

下一步冻结客户端、仅安装宿主组件并实测。应用容器与Vue不因协议子项提前更新；之后接持久业务属性策略与页面，继续真实权限/退出/Worker/Agent故障、ACL/xattr/特殊位、重建身份和长期清理及59项总目标。
