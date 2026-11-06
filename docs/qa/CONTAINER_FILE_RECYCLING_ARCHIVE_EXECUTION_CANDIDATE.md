# 归档复制：Agent 执行适配与固定 Root 传输候选

2026-11-06，执行候选 fd6a73ea 与校验工具修订 `838345cb1085e7f347120bfbe5686735db73e27d` 已编译、测试并通过真实 systemd 权限分项。承接[协议/SDK](CONTAINER_FILE_RECYCLING_ARCHIVE_RPC_SDK_CANDIDATE.md)，源码新增 gRPC 映射、Runner、固定传输与安装/后置校验声明；没有执行标准安装、覆盖生产或正向签名 RPC/HDD。Agent 源码和运行态均保持协议1.58，不宣告归档 capability；SDK 的1.59门禁保留，待原生探针和安装验收后再切换。

## 执行链与帧边界

五个 Unary 均经既有 Archive Policy 验 known fields、完整计划/context/三ID和请求摘要绑定，再交给独立 Runner。Plan 使用 command 投影，其余动作固定 operationId/owner/plan；Root 不能从调用方选择模块、目录、系统命令或动作。Runner 120秒、原生110秒/单元130秒预算，单个结果后还需 EOF/成功退出，不接受尾随输出。

新增精确 container-recycle-archive 动作分类与 Driver 标记，启动固定 privileged 子命令，停止只匹配原随机 Driver ID 与 InvocationID。Archive 与既有 recycle 系列一样，在 driver Ready/原 Invocation 绑定之前不写任何操作字节；取消不 flush 缓冲正文，Ready 后一次写入并明确 EOF。驱动 Ready 只表示本次进程身份已绑定，不等于业务/安装就绪。

完整 archive 计划比旧响应更深，因此仅 Archive 的结果 JSON 允许16层，其他驱动保持8层；总行仍限64KiB。Root 输入也限定16层、单行64KiB，递归过深、重复键、超限、未知结构与提交后额外数据均拒绝；纳秒整数保持精确。错误仅投影固定 code，不输出原始路径或任意异常消息。

依赖丢失与本操作缺席分开：原 source/purge 日志缺失返回 SOURCE_NOT_FOUND/FailedPrecondition，不再使用本操作私有 NOT_FOUND；SDK 不能将前者当成“尚未发生”。只有自己的 journal 确实缺席且其他依赖正常时，精确 ARCHIVE_NOT_FOUND 才可映射 null。临时真实 journal/归档测试覆盖两类依赖文件删除，保留保护并拒绝误判。

## 固定权限与安装声明

Root 传输只加载16个固定依赖模块；文件须 root 所有、普通单链接、不可组/其他写且大小有界，拒绝符号链接、硬链接、非Root所有和任意模块路径。新 journal 固定为 whaledeck-container-recycle-archives，复用既有 source/purge 与 restore claims，不改其解析规则。

新 privileged 单元仅将归档 journal、既有 restore/claims 子目录及应用目录设为可写；原 source/purge journal 和 HDD 不加入 ReadWritePaths。需要 CAP_CHOWN 复制原 UID/GID，因此仅归档专用单元加入它，保留 DAC/FOWNER/SYS_PTRACE 并禁止 SYS_ADMIN。ProtectSystem=strict、ProtectHome、PrivateTmp/Devices、NoNewPrivileges、限制地址族/namespace、资源预算、Restart=no/control-group 与 BindsTo Agent 保持。

安装脚本新增 Root 0700 namespace 及4个 Root 0750 模块（plan/fences/copies/transport），后置验证检查其 owner/mode、非符号链接、单链接和 claims 目录。这些是源码声明，本批未运行 installer，也未扩大正式 sudo 规则。

## 测试与失败记录

5771项单元零跳过通过；782项集成通过，7项默认原生依赖跳过，未计通过。178项关联 Linux root 零跳过，包括13项传输专项；另14 CI合同、8范围、3 shell及4 Python语法通过。传输专项含实际临时归档复制的新 inode/五动作、依赖丢失、实际模块文件权限/链接/大小，以及输入帧与只读安装合同；命名空间/硬件证明在这些临时用例中仍为明确替身。

首轮 Archive专项119项中118通过、1失败；深度8正确拒绝了完整新计划，但测试错误要求异常必须精确为 JsonException，实际是其 JsonReaderException 子类。改用允许该派生类型的断言，未放松深度；原输出和源文件保留。曾提交的文件末尾空行检查告警已用独立格式提交修正，没有改写历史。

## 真实 systemd 权限分项

测试父程序从 Git 冻结的实际 privileged case 提取所有 --property 参数，仅把三个可写路径一一翻译到新的自有临时目录；其余 CAP、保护、预算、Agent BindsTo 等属性不改。没有运行新全局 privileged 命令或触碰生产数据。探针仅使用固定已知测试字节与文件，不读取凭据、Docker对象或真实备份。

首次将夹具放在/run，本单元下它没有形成预期只读保护；探针截断自己的 source 测试文件后按约失败，另外两只读测试文件与应用目录未动，未接受为权限通过。保存原探针、启动器、profile、stdout/原journal后，按准确名字/内容/身份收尾，未删除未知对象；失败不是生产来源或 HDD 被写。

仅将测试夹具位置修正为/var/lib，与真实状态路径的保护条件对齐，不放宽任何生产权限。新 unit `whaledeck-qa-archive-sandbox-cff4da4d683e4cd4a4a470915c28b0df` 由冻结 profile 驱动，实际 source/purge/HDD 三组写打开均 EROFS 拒绝，原字节保持；应用新文件写入、chown1000:1000、0640/user属性和 fsync成功，新 journal/claim可写，CAP_SYS_ADMIN不在边界集。原日志/receipt成功，之后准确移除所有测试文件/目录。该证明不替代新传输在真实 HDD/容器中的正向签名 RPC。

## 保持与继续

生产仍9694af1c；53回收/29恢复/17清理永久意图完整行、活动0，回收Root124/HDD127/恢复Root80/目录Root98/purgeRoot34、2346保护、11核心及宿主/应用身份前后相同。未创建生产归档 namespace/来源历史或安装态能力。准确上批3af55628的CI42240372371已12路全success，本批新CI仍需按新提交核对。

证据 `container-recycle-archive-execution-candidate-d40b77bf458d4b1bbdbe76a7c11f0e5b` 共44文件另加manifest，摘要 `1b66d5028c17171edd1b8ceb947a381a46311d6d367af2713b1037a3c93ef709`，14改动源码及原失败/实际profile/收尾/保持记录封存，Root0700目录/0600文件保持。

继续原生完整探针、1.59能力版本切换与标准安装、正向签名RPC/HDD，再接永久业务/Worker/API/Vue/Jobs与真实身份整链。完整故障、成功claim/暂存清理、留存/维护连续性及59项仍待完成；17项移出不算完成，整项0/59、阶段0/6、粗估工程25%–35%、酒馆/Live2D功能0%保持。
