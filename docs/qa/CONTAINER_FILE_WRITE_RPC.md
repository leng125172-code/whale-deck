# 容器文件写入：独立签名协议与安装态 Agent 验收

日期：2026-10-26。当前单工作站59项范围。最新签收源码、SDK、真实systemd profile和下述安装态Agent子门禁；永久业务任务和人类身份/WebUI仍未接通。

后续136b8a0/minor40已覆盖并签收首字节前持久取消标记、跨调用查询/重放及延迟首帧精确拒绝，2441 Unit/160 Integration（6原生夹具跳过）、166容器Linux-root通过；本批自有载荷/回执为零，11核心healthy。[取消边界及后续业务约束](CONTAINER_FILE_CANCEL_FENCE.md)。下方ac72b41为此前独立写入传输的证据，不重复计数。

## ac72b41 安装态签收

冻结`ac72b419c9d4539f5a9b134de16c799d29615c0b`构建并覆盖安装Agent和MaintenanceHost，minor39、固定helper及源码逐字节核对通过；两宿主程序实际build-info对应该提交，运行库和PKCS均10.0.13。API/Worker/Gateway及八个基础容器未更新，11个核心容器全部healthy。宿主systemd/权限/UDS/受限helper/Docker、当前动态IPv4与回环入口校验通过；本轮没有Whale Deck镜像构建。

同一冻结源码的独立TerminalProbe，通过真实UDS调用正在运行的非root Agent，再由Agent调用限定sudo helper：五方法、完整2,097,159字节stage/publish、原只读RPC全字节读回、Published重放与旧inode/HDD原件证明通过；匿名、旧MAC、改请求体、nonce重放、核心/敏感路径/过期实例、不同owner、错误目标证明及错误方法拒绝通过。另一完整暂存显式Discard/重放及Inspect=Discarded通过。该客户端为受信root QA调用方，不是Authentik人类用户或BFF授权证据；安装态活流中断/撤权组合仍待补，不能用前序profile活取消代替。

独立文件系统观察器核对新内容全字节、UID/GID1000和0640、保留原inode及完整原件、HDD schema2 ContainerFile绑定/全字节；之后只清理本次已证明的容器、目录、私有payload/receipt和HDD捕获。前两次失败现场在复算原始SHAKE内容、完整新内容、根回执/目录及文件身份、当前版本、HDD完整原件、容器API404后精确清理，保留私有基线/失败/复核记录。独立复核本批目录和活跃writer unit为零、根上传回执/payload为零，11核心healthy。

最终Release零警告错误；2411 Unit执行/通过2411，160 Integration执行/通过160，另6专用原生夹具明确跳过；154容器Linux-root执行/通过154，含新增8项清理证明检查和8项stop竞态故障注入。TerminalProbe也零警告错误构建，并加入CI显式编译。旧候选的2410/138计数是前序结果，不重复相加。

### 安装态发现并修复的失败

最初`0b4e235`安装态两次在“不同owner的Inspect应返回PermissionDenied”处收到通用FailedPrecondition，虽然临时发布已完成，仍明确验收失败。细分客户端阶段和独立正向回执测试后，定位到Driver清理期间`--collect`卸载unit的竞态：show读取InvocationID后，stop可能因unit已收集而失败，Dispose异常覆盖原本的权限拒绝。`ac72b41`只允许再次成功查询且InvocationID已空时接受此竞态；存活/替换实例、初次或再次D-Bus查询失败均拒绝，不重复停止新的实例。8项确定性实际helper分支故障注入及修后完整安装态链路均通过；没有放宽owner检查，也没有把错误状态改成成功发布。

准备环境时开发副本缺少Git标记的执行位和私有`.images.env`，按现有Git模式恢复12个脚本的owner执行位，使用锁定示例生成0600镜像配置。第一轮宿主校验误在安装重启进程仍运行时提前发起，返回3；等待原安装句柄实际成功结束后完整重验通过，未据此重启或重装第二遍。客户端草稿的Replayed属性层级编译错误已修正为Upload.Replayed，并通过完整构建。

以下为首轮未安装候选的实现与证据记录；最新安装态以上方为准。

## 实现

- Agent minor39新增独立能力`docker.files.bound-uploads`及`ContainerFileWriteService`五方法：InspectTarget、StageUpload、Publish、Inspect、Discard。原minor38只读四方法、旧宿主文件RPC/profile保持独立，不把读签名升级成写权限。
- 新签名同时绑定方法、protobuf消息类型和完整消息SHA256；45秒SDK有效期，Agent最长60秒，一次性nonce。新服务拒绝旧的仅方法签名；拦截器校验MAC，服务再次核对实际请求/首帧的哈希。容器ID、原始PID/纳秒启动实例、路径、主体、任务/幂等ID、源/父版本、目标证明及完整载荷长度/摘要均进入绑定。上下文不是本地登录或持久授权替代物。
- InspectTarget冻结当前容器根/命名空间/配置证明与父、叶版本。公共Stage和后续查询/发布/丢弃必须携带原目标证明。后续操作只接受原容器/实例/操作/owner组合；HDD丢失不能被当成“任务不存在”。仅明确`CONTAINER_UPLOAD_NOT_FOUND`转换为空查询结果。
- SDK、Agent流解码和root引擎分别检查32KiB块、连续偏移、长度、完整SHA和commit后EOF；成功还要求准确回执、helper正常输出结束及退出。提前退出、额外输出、错绑历史或旧宿主历史字段不能返回成功。
- 独立固定`container-file-write/stop` helper；枚举动作和固定入口，无任意命令或调用方宿主路径。root-owned可信模块、两槽内核锁、root0700私有状态、限时/内存/进程/FD/输出预算及InvocationID精确停止。仅允许必要capabilities、AF_UNIX、批准应用目录及HDD历史目录；目标仍由ContainerRoot重新解析和保护。
- 安装脚本包含新入口/模块与私有状态目录；首轮候选没有执行安装，后续ac72b41安装见上方。没有新增HTTP写入入口。

新协议的`x-whaledeck-request-sha256`为大写SHA256：UTF-8编码`whaledeck-container-file-request-v1\n`、protobuf完整消息类型名、`\n`，再接该消息ToByteArray。MAC为HMAC-SHA256，输入UTF-8编码`whaledeck-container-file-capability-v1\n<expires>.<nonce>.<full-method>.<request-hash>`，大写十六进制；capability头值保持`<expires>.<nonce>.<mac>`。新消息无map；不对原始实例/Unicode重新格式化，Stage绑定首帧Header并另验完整内容。

## 实际验证

- Release solution构建零警告/零错误。最新TRX：2410 Unit全部执行通过；Integration执行160/通过160，另6项原生依赖夹具明确跳过，不充当实际数据库证据。
- 新增102项.NET测试：43签名/拦截器、19真实Unix Socket SDK传输、40首帧/编解码/错误/回执策略。SDK夹具为合成Agent，不冒充实际root helper或Authentik。
- 容器Python联合138项在Linux root全部执行通过，含11项新传输检查；真实SIGKILL验证私有槽释放。宿主安装运行时12、CLI预览5、范围8通过；新Python编译检查加入CI，原容器测试glob自动涵盖新文件。
- `validate_container_file_write_profile.py`在独占非root、无网络、只读根、批准可写绑定的真实容器中，完成target→stage→inspect→publish→重放、已发布拒绝丢弃、错误目标证明、缺失任务和另一任务取消。完整2,097,159字节逐字节校验；新文件UID/GID1000及0640、旧inode保留和HDD旧原件均通过。
- 上述真实profile沿用产品全部systemd沙箱、capability和InvocationID停止参数；仅冻结副本中的入口、私有state及HDD子命名空间路径替换为自有QA目录。不是已安装canonical helper或whaledeck-agent身份的证据。
- 真正保持stdin打开的接收任务：错误InvocationID停止被拒绝且进程继续；正确InvocationID停止后无成功回执，Inspect=Interrupted、Discard=Discarded，partial和目标均不存在。所有自有unit已终止。
- 当前成功批次容器、绑定目录、历史精确清理。前两次检查器失败保留的HDD子目录，在独立核对固定namespace/操作/容器/实例/owner、schema2/Verified、完整大小/SHA、root权限及单链接后，只删除确认的两个文件及空目录。独立复核本批QA目录为零，11个核心容器全部healthy、未重启。

## 失败记录及修正

最初测试草稿有静态数组分配及override参数名称分析器错误，已修正，完整Release与测试通过。真实取消检查最初错误地要求systemd-run返回非零；主动SIGTERM可被systemd视为正常停止，退出码0不构成上传成功。修正为“无回执+unit不活跃+持久Interrupted+显式丢弃清理”，没有修改产品成功条件或放宽精确停止验证；修后真实profile整条通过。

早期profile失败现场先保留；不以测试失败为由递归删除或忽略摘要。真实失败不是产品数据丢失结论，也不隐藏为一次全绿。

## 尚未完成

永久容器上传实体/迁移、Job/Outbox/租约和共享资源锁、当前Authentik管理员复核、审计与历史登记；BFF上传/UTF-8编辑、历史恢复与Unknown当前管理员接管、桌面/手机真实身份及安装态活流取消/断线/撤权组合均待继续。容器目标与宿主登记根可能映射同一物理目录，业务锁须处理这种交叉操作，不能仅以不同RPC或rootId视作隔离。

本子门禁不等于完整容器文件、P2-03/P2-04或全部59项完成；17项授权移出不算完成。下一步接永久业务和页面，继续其余保留任务。
