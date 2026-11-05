# 独立回收恢复协议1.57、SDK与受限传输候选

2026-11-06，候选a3dabd9e，生产仍58b8174/Agent1.56。本批承接[独立恢复内核](CONTAINER_FILE_RECYCLING_RESTORE_KERNEL.md)，接入独立签名RPC/SDK/受限传输和安装声明。未安装新Agent或执行新恢复RPC，不签永久业务/真实恢复页面整链；原运行实例限定及长期清理等剩余目标保持。

## 独立协议及完整来源绑定

新增ContainerRecycleRestoreService四个Unary：Plan、Apply、Inspect、Discard。候选能力为major1/minor57及docker.files.recycle.restore，两者必须同时满足；原1.56回收及旧Restore接口保留兼容，不能借旧能力调用新服务。

新请求OperationId和Context.JobId必须相同且不等于原Source.OperationId；上下文绑定当前Actor/Trace/幂等键，禁止payload内嵌capability。Source独立携带原OperationId/owner/完整回收计划，Command同时绑定期望HDD摘要；新计划额外绑定父identity/version、目标证明、执行UID/GID、私有目录identity、留存inode完整身份及精确ctime。原回收引用不会被新恢复ID覆盖。

独立Service路径、消息Descriptor、全部protobuf字节与请求SHA进入新域HMAC，expiry/nonce/method/hash全部绑定；新旧域不能互用，四个方法精确允许，旧通用签名拒绝新服务。新SDK在第一个await前深冻结来源字节数组，验证回复命令仍为原来源，并重算完整原生计划指纹；只有精确CONTAINER_RECYCLE_RESTORE_NOT_FOUND映射Inspect null，其他源丢失、超时或故障不被伪装成无记录。

Plan read-only精确拒绝可映射SourceChanged/Busy、TargetExists/Changed、Protected、HistoryUnavailable六类有界异常；同样错误出现在Apply等执行阶段仍保留RPC不确定性，不据异常推断零修改。

## JSON/字节与可空事实

C#与Python使用同一排序、紧凑、UTF-8原生JSON及restore-plan域计算SHA。自定义字符串编码保持Python控制字符小写转义、中文/emoji/U+2028字面UTF-8；固定金样本覆盖零UID/设备、uint64最大值、负纳秒与控制字符，避免数字截断或Unicode重新转义改变指纹。原始属性bytes与Base64、九字段身份在双向映射保持。

RequireKnown把整个消息重新以DiscardUnknownFields解析并比较字节，该解析选项递归传播。直接测试Context、Plan、私有/留存identity、Command、Source、原回收Plan、Location与重复属性各层未知字段，均拒绝。Native JSON同时严格拒绝额外、缺失和重复字段。没有仅靠顶层检查声称嵌套字段安全。

回执分别携带新恢复ID与原Source ID。SourceState、SourceRetained/Restored、RestoredByThisOperation使用optional/Has presence；Unknown必须保留事实缺席，不能转成false。Completed、已claim的Aborted、未claim的Aborted、Unknown分别校验来源状态、HDD摘要及三态事实；BindingCurrent只能是已验证且HDD可用的Completed。缺失已知false、错误ID/源/摘要、错误来源状态或计划均不能变成功。

## 固定传输与最小安装声明

新增固定whaledeck-container-recycle-restore-transport.py，仅接受plan/apply/inspect/discard、私有stdin一行完整JSON及EOF，模块白名单和路径固定；从原来源计划取得ContainerRoot，不接受hostPath、UID、命令或模块路径。目标消失/实例变化只允许观察/围栏的安全分支，通用Docker/socket/HDD故障失败关闭。

新Driver复用“先读取并绑定Ready/InvocationID，再发送操作body并EOF”的规则，预Ready取消不会释放操作。结束/取消只停止该准确systemd Invocation，不用原回收ID或任意PID停止别的任务。固定whaledeck-container-recycle-restores-随机driverID作用域，AF_UNIX、control-group、Agent BindsTo、超时/CPU/内存/任务数及固定ReadWritePaths沿用受限规则；仅增加独立journal/claims目录写范围，不增加CAP_SYS_ADMIN、CAP_CHOWN、通用sudo或Docker Socket。

install/validate新增固定transport root:root/0750声明，独立状态/claims目录0700沿用上批声明。候选Agent注册新服务和能力、SDK注册接口，但未注册恢复Worker/HTTP/Vue/数据库消费者。安装语法/固定参数负测通过，不以声明代替实际安装。

## 检查、真实旧版本拒绝及修正记录

新增77项签名/策略/SDK测试通过；最终全量5369 Unit零失败/零跳过，705 Integration通过、7个原生依赖测试默认跳过（712总数），585原生文件测试及54回收/恢复组、93现有验收工具、8范围通过。Release测试项目与专属探针构建零警告错误，Shell/Python语法通过；CI发现入口沿用现有原生组，新候选远端CI终态待核验。

RPC测试的HTTP/2/grpc framing、签名/篡改与回复校验实际执行，但Agent响应/能力是显式handler夹具；Native组使用真实临时文件/journal/HDD格式与明确运行实例/设备证明替身，不是物理HDD或安装态恢复。

另对当前真实Agent运行只读旧版本探针：读取major1/minor56，SDK明确Unimplemented拒绝新恢复；实际仅2个GetCapabilities帧，operationFrames=0、能力密钥未读取。探针handler即使遇到意外新Agent也禁止转发任何操作。probe/SDK/Contracts二进制BuildIdentity均等于冻结a3dabd9ed1cc9c60fdd7fb272c90f2f572dd3463，相关DLL SHA保留。这里只签旧版本能力拒绝，不签新1.57 RPC执行。

首轮构建CA1720参数名包含类型名、CA1725接口/override参数名不一致已修正；测试夹具初稿CS0029把bytes赋为string、计划原生字段数误写11（实际10）均修正。原失败构建/断言日志封存；没有通过关闭分析器或放宽JSON字段校验取得通过。

## 生产保持与封存

冻结后专属进程再次执行54项原生组、真实旧Agent只读拒绝，生产前后及另一进程重读均与上批已签基线一致：2346保护文件、11核心healthy、回收Root28/HDD31、目录Root96及旧来源/恢复记录保持。生产/两宿主仍58b8174，新恢复namespace未安装；未调用新签名恢复、创建资源、修改身份或发布/回滚。

证据deploy/artifacts/container-recycle-restore-protocol-5f2f3895a7dc46418ca989c54f5b106f，共222文件、198份冻结源码，EVIDENCE_SHA256.json摘要9acc7c704eb8819ee9c0879a40f869e387223e765be1d361f9876d44fb59f5e7，逐文件重算通过。unit whaledeck-qa-recycle-restore-rpc-eba92a9b62db、Invocation314a7718a6a14b62ab570ecbb2bdb23c准确Result=success/ExecMainStatus=0。

下一步补齐完整原生探针及安装态签名RPC/实际HDD分项，再C#独立永久编排、来源/确认/回执MAC、真实PG/共享写入保护、Worker/API/Vue与真实身份整链；显式新实例目标重绑定、HDD重建/完整目录恢复、全部故障与长期清理仍待。整体25%–35%粗估、整项0/59、完整阶段0/6、AI子计划0%及旧代码量快照保持，17项移出不算完成。
