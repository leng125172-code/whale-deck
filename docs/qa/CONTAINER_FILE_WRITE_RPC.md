# 容器文件写入：独立签名协议与受限传输候选

日期：2026-10-26。当前单工作站59项范围。本记录只签源码、SDK和真实systemd profile子门禁，不签安装态Agent、永久业务任务或人类身份/WebUI。

## 实现

- Agent minor39新增独立能力`docker.files.bound-uploads`及`ContainerFileWriteService`五方法：InspectTarget、StageUpload、Publish、Inspect、Discard。原minor38只读四方法、旧宿主文件RPC/profile保持独立，不把读签名升级成写权限。
- 新签名同时绑定方法、protobuf消息类型和完整消息SHA256；45秒SDK有效期，Agent最长60秒，一次性nonce。新服务拒绝旧的仅方法签名；拦截器校验MAC，服务再次核对实际请求/首帧的哈希。容器ID、原始PID/纳秒启动实例、路径、主体、任务/幂等ID、源/父版本、目标证明及完整载荷长度/摘要均进入绑定。上下文不是本地登录或持久授权替代物。
- InspectTarget冻结当前容器根/命名空间/配置证明与父、叶版本。公共Stage和后续查询/发布/丢弃必须携带原目标证明。后续操作只接受原容器/实例/操作/owner组合；HDD丢失不能被当成“任务不存在”。仅明确`CONTAINER_UPLOAD_NOT_FOUND`转换为空查询结果。
- SDK、Agent流解码和root引擎分别检查32KiB块、连续偏移、长度、完整SHA和commit后EOF；成功还要求准确回执、helper正常输出结束及退出。提前退出、额外输出、错绑历史或旧宿主历史字段不能返回成功。
- 独立固定`container-file-write/stop` helper；枚举动作和固定入口，无任意命令或调用方宿主路径。root-owned可信模块、两槽内核锁、root0700私有状态、限时/内存/进程/FD/输出预算及InvocationID精确停止。仅允许必要capabilities、AF_UNIX、批准应用目录及HDD历史目录；目标仍由ContainerRoot重新解析和保护。
- 安装脚本包含新入口/模块与私有状态目录；本轮没有执行安装，没有构建或发布Whale Deck镜像。尚未新增HTTP写入入口。

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

新Agent/canonical helper安装与真实签名UDS五方法、安装身份权限及取消/断线组合；永久容器上传实体/迁移、Job/Outbox/租约和共享资源锁、当前Authentik管理员复核、审计与历史登记；BFF上传/UTF-8编辑、历史恢复与Unknown当前管理员接管、桌面/手机真实身份验收均待继续。容器目标与宿主登记根可能映射同一物理目录，业务锁须处理这种交叉操作，不能仅以不同RPC或rootId视作隔离。

本子门禁不等于完整容器文件、P2-03/P2-04或全部59项完成；17项授权移出不记完成。下一步安装态五方法验收，再接永久业务和页面。
