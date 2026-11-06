# 留存原件清理：签名协议、SDK及受限传输候选

2026-11-06，协议候选 `77b16c0d206a4cc888476b64d68182a73fe4760d`、只读探针修正 `913cfbf993189811620d8c863249a5afbbd46984` 已验证并推送dev。接续[清理内核](CONTAINER_FILE_RECYCLING_PURGE_KERNEL.md)，未安装/启动新Agent、未覆盖生产、未发送实际清理RPC；完整安装态签名RPC/HDD、永久业务及页面验收仍待完成。

## 专用契约与权限边界

新增 `ContainerRecyclePurgeService` 五个独立Unary：Plan、Reserve、Apply、Inspect、Discard。保留先预留再Apply的内核要求，不把预留当作删除完成。协议1.58及 `docker.files.recycle.purge` 能力同时满足才允许SDK发送操作；major不匹配、旧minor或缺能力均拒绝。新能力是代码中的声明，本批安装态仍1.57。

请求使用独立purge服务路径、请求摘要域及capability域，绑定描述符、完整protobuf字节、方法、有效期、单次nonce，以及原来源/新operationId、actor、trace、job和幂等键。通用旧capability不能授权新方法，方法之间也不能借用。Agent校验capability和完整内容摘要；源操作ID不可替代新Job，未知/嵌套未知字段不能被静默丢弃后接受。

命令只接受Originals及完整来源/HDD摘要，不接受归档清理范围或宿主路径。类型化计划保留全部18个原生字段，包含来源完整记录摘要、历史元数据/时间、存储配置摘要/目录身份、留存与归档九字段inode身份、大小/SHA，以及原上下文。SDK在首次await前冻结可变来源字节数组；严格投影、数字范围、root:root/0600/单链接归档及原件绑定检查保持，Python规范指纹黄金值核对通过。

回执限定Reserved、Completed、Aborted、Unknown及自身/来源ID和指纹。Reserved明确false且有自身围栏；Completed明确true且有自身围栏；Aborted明确false且不拥有活动围栏；Unknown不提供purgeApplied值并保持来源保护，不能映射为false。保留其他操作占用围栏的情形，不将其归属冒认给本操作。旧来源的Purged只读回执增加受约束兼容：已验证历史、未保留/未恢复、无当前原件绑定；不是原件仍能恢复的证明。

## 固定驱动及安装声明

新传输只从固定模块加载固定内核/预检/围栏，固定独立purges状态目录，读取既有恢复claim目录；计划与所有动作继续绑定原容器实例，目标消失/重启只允许session缺席的观察或明确拒绝，不重绑定新实例。错误按有界白名单投影，不把原始路径或异常文本传给客户端。

Agent启动器固定helper动作和systemd单元，停止须核对准确Invocation；新动作在Ready及Invocation绑定前不发送任何操作字节，取消不能通过EOF意外提交请求。helper限制五动作、root/0700状态、固定ReadWritePaths、资源上限、Restart=no、130秒上限及control-group停止/BindsTo Agent；恢复claim目录没有新增写权限。安装脚本声明四个新宿主模块及状态目录，未实际执行安装，开发user NOPASSWD授权保持。

## 自动检查与准备失败

最终5554项.NET单元检查通过、741项集成通过（7项原生按既有默认门禁跳过），212项回收相关Linux root检查零跳过；其中6项新传输边界检查另可按普通用户运行。12宿主运行时、14前端CI合同、8范围检查、Python/Shell语法和构建通过。C#测试覆盖五方法单次签名、跨域/篡改/时限、SDK能力拒绝及深拷贝、严格投影/回执三态、嵌套未知字段、Ready前取消和固定停止参数；合成gRPC帧及临时测试不是安装态签名RPC验收。尚未签收本提交的远端CI终态。

准备期保留三类事实：首次签名测试的archiveSha替换值碰巧与夹具相同，改为不同值并加不相等断言；复核发现新动作前缀需加入Ready前禁写路径，已补入并增加五动作取消/停止测试；源码封存的严格Git/工作树字节等同被历史CRLF拒绝，后保存实际字节与Git哈希并仅允许换行等价。63份历史CRLF文件没有被改写。

首次实际只读探针因仍匹配旧恢复错误码而返回RpcException，只保存了类型，原始RpcStatus未捕获，不补造；修正为purge错误码并增加有界阶段/状态诊断后新构建复验。原失败不补签通过。初次源码封存262文件清单 `577fd251c9a394f86455c6e055ec79a61fc1a8adf0d41f572292b795266d3518`；旧探针失败1184文件清单 `54fa63a31b3ae57063592bc83c766bb395d20dba7d62043d70b7b0b2e3de3a17`，均私有保留。

## 实际旧版本只读拒绝

修正探针经标准自包含构建（运行时10.0.13），在本批root私有冻结副本执行。HTTP handler仅允许GetCapabilities，即使遇到新Agent也不会转发操作；使用随机不存在的能力密钥文件路径，实际没有读取部署密钥。当前1.57返回能力后，SDK明确拒绝purge；实际2个能力查询、0个操作帧、capabilityKeyRead=false，probe/sdk/contract修订均为913cfbf9。这里只签旧版本拒绝，不签新版本实际HMAC、root执行、HDD修改或真实身份页面链。

前后生产基线和全部36回收/22独立恢复行摘要完全保持，活动0；2346保护文件、11核心、两宿主及093f44a9部署保持，回收Root72/HDD81、恢复Root59、目录Root98保持。未新建业务表、清理/恢复意图或生产purges状态目录，也没有测试资源/负载删除。

成功私有证据 `deploy/artifacts/container-recycle-purge-rpc-9240a82af45146949fc083263a94464e` 共1186文件（另有清单），清单SHA256 `cc23ea742137d9d76391576d169b9acd159d70a1d308527a1e36ff4aa79aa744`；944份源码及233份自包含构建文件在探针运行前冻结，源码同时保存实际/Git哈希及63份CRLF路径说明，逐文件核对通过。目录root:root/0700、文件root:root/0600，执行后入口也恢复0600；不上传私有源码/证据、资源身份或内部端点至项目图谱。

下一步补完整原生探针与独立证明、标准安装/准确构建来源核验、新协议真实签名RPC/HDD与中断验收，再继续永久业务/共享写入保护/Worker/API/Vue/Jobs、留存策略及清理后的归档恢复。59项/17项移出边界不变；整项0/59、阶段0/6、酒馆/供应商/Live2D功能0%保持，master完整交付未完成。
