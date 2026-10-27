# 容器历史属性恢复：minor43签名Stage候选

2026-10-27。本批协议/固定helper/SDK/安装配置已实现，尚未安装或执行新的签名UDS实测。生产仍5fd8d3b/minor42。业务永久意图的显式属性策略、计划/旧MAC兼容、EF迁移、前端选项和真实业务恢复链尚待，不能将本协议子项计为完整属性恢复。

protobuf ContainerUploadHeader新增可选字段10 restore_source，只有capture_id/capture_hash/path。原header字段及普通上传的Root JSON完全保持；属性来源与目标路径独立，请求全字节指纹及单次capability包括来源全部字段。SDK发内容前必须协商major1/minor43和docker.files.restore-attributes及已有两项历史能力；低版、未知major和缺少能力拒绝，不枚举来源内容。Agent重复校验来源语法，然后仅投影固定字段至helper；不接受任意UID/GID/mode。

固定helper加载root所有的新属性模块并从完整验证的HDD捕获派生属性，使用schema4和前序完整发布/持久取消/封存保护。新来源错误经过固定白名单映射为稳定的权限/冲突/前提错误，不回传任意异常。安装脚本与宿主校验增加新模块，Agent声明minor43独立能力。

本地门禁：Release零警告错误；2704 Unit/199 Integration通过（7专用原生夹具默认跳过），251 Linux-root和12宿主安装边界通过，Python/Shell语法通过。首轮6个旧能力断言仍固定42导致失败，明确更新为43；42拒绝新属性能力的降级用例原样保留。旧普通header仍10个Root字段，无新来源字段；无业务schema或原意图MAC变更。

新增冻结原生客户端与验收脚本：实际BFF普通上传生成旧属性捕获，只重启自有测试容器，然后独立签名ReadSource/Stage/Publish/Inspect，验证来源字段改动不匹配MAC、签名重放拒绝、有效签名错误捕获摘要拒绝，以及原路径和其他路径各2097159字节的属性恢复；Python独立核对Root/HDD/旧inode和4属性。此脚本尚未执行，不冒称已有真实结果。验证保留原始正常上传Job/审计、Root和HDD，精确清理自有容器/源，不进行生产数据写入或自动回滚。

下一步冻结客户端、仅安装宿主组件并实测。应用容器与Vue不因协议子项提前更新；之后接持久业务属性策略与页面，继续真实权限/退出/Worker/Agent故障、ACL/xattr/特殊位、重建身份和长期清理及59项总目标。
