# 归档复制：三阶段原生探针与独立证明候选

2026-11-07，候选 `a50d72f6fdafeb7514a63d75a6753461bf37aa0a` 已编译、自包含发布，并在真实旧安装版证明密钥读取前拒绝。新增三阶段逐帧探针、独立 wire/journal 证明和35帧语法，尚未接完整安装态父级采集器/逐步独立物理验收，也未切换1.59、标准安装或签收正向归档 RPC/HDD。承接[执行候选](CONTAINER_FILE_RECYCLING_ARCHIVE_EXECUTION_CANDIDATE.md)，不以本批替代完整归档恢复或主任务完成。

## 三阶段放行

探针先核固定 header、当前编译 revision 与非核心 QA container/instance，再向真实 Agent 获取 capabilities。只有 major1/minor>=59、source/restore/purge/archive 能力齐全，才读取32字节签名 key；旧版退出时 stdout 必须空，独立证明确认零操作帧。key 在 finally 清零，不输出请求头、令牌或异常原文；动作先经白名单才进入可输出的 phase。

来源阶段沿用固定 work/recycle-rpc.bin 的规划/回收/观察；purge 阶段沿用 Originals 规划、独立预留、取消/正常清理及观察，不提供一条命令串行自动执行整链。每个 RPC 回执后等待父级下一包，父级需要核对旧来源/HDD和当前物理命名空间，未知回执不能继续。

新 archive-plan 要求缓存的原 purge 请求实际 Inspect 为 Completed/purgeApplied=true/owned fence，且同一来源仍 Purged、历史验证可用，才组成完整新 ArchiveCopy 命令。返回计划必须与命令、目标、原已删身份、HDD身份/配置/大小/摘要完全匹配，指纹使用 archive 独立域，支持已知旧私有目录存在或缺席。

新归档动作仅允许已缓存的精确批准计划，新ID不得复用 source/purge；新操作必须从 reserve/discard/reject开始，Apply 还需已有 Reserved 或已观察终态。状态缓存不允许新请求以 Apply 隐式进入，原 Unknown 的旧 Apply 也只交给原生观察规则，不自动复制。父级最终必须另核新 inode、字节/属性、Root/claim与当前 HDD，再批准重复动作和精确 QA 收尾。

## 签名与边界矩阵

source、purge、archive 三套服务/hash/payload域独立选择，不能把 Archive 包签成 Purge。Archive 计划22项拒绝包含缺签名、错误域/过期未来/跨方法、嵌套 purge/source/归档/历史及actor/trace篡改、非法模式/旧ID/历史和nonce重放。Archive 操作31项拒绝包含20项完整主体/计划变异、四错域、错方法、两个旧ID、错Job、私有缺席、未预留Apply与无签名。

操作绑定负例会同时重算内层 purge 与外层 archive 指纹，并保持结构合法；因此应测到签名绑定，不能由早期格式错误代替。修改 operationId 时 JobId 同步改变，两个旧ID与错误Job另用正确签名验证 InvalidArgument。未预留的负向观察可创建有界空锁，但不能创建业务 journal、claim或负载副作用；安装态父级仍须真实核对。

## 独立证明的准确范围

独立 Python 模块不导入产品内核。按严格字段集/重复键、optional presence、Unicode/精确64位整数，转换 Protobuf JSON 为 native 投影，核对19字段计划、原 purge/source、三ID与独立指纹。Unknown archiveCopied缺席不等于false；历史Completed可以保留，当前绑定只有确实持有本操作claim且为Completed时才可能true。

journal join 要求调用方传入实际清理后的 source/purge/marker/history/data快照及当前存储目录/配置证明；逐值保持这些旧事实，并核对分域摘要、归属claim、新 inode/准备与发布身份。它返回 physicalNamespaceChecked=false，不能用自洽 JSON 或35帧语法替代活体容器/HDD/目标/暂存目录独立读取。

35帧语法固定连接原source回收、purge取消/成功和archive取消/新复制，绑定五个不同ID、11条purge回执、11条archive回执及结束后仍Purged的旧来源。缺帧/重复/换序/错Job/旧ID/错误拒绝数、Unknown冒充成功或缺活体绑定均拒绝。15项新专项包含临时实际回收/清理/新 inode复制后的记录join；命名空间/HDD硬件证明仍是明确替身，不冒充安装态正向RPC。

## 原 CI、构建与采集失败

准确上批53ff012a的CI42245407649最终为failure，installer job127986207715中585项容器文件检查有2失败，其余11路成功，未覆盖发布。原日志已直接从该job取得，原失败源码从精确Git提交冻结；并发采集到的已修正工作副本另存，不标成原源码。

两个旧测试把自己的case一直截到后面的permissions分支，因新增归档case的专用CHOWN误判旧回收/恢复扩大权限。现在仅截取各自独立动作与stop case，继续要求旧规则无CHOWN/SYS_ADMIN，并明确不含archive分支；未改实际旧规则。按CI完整范围复验585项通过。

初次新探针编译有两个CS0136：归档分支局部operation/receipt与外层purge变量重名。原源码及事实保存，改为独立局部名后构建零warning/error；未保存的完整构建日志不重建。

日志复验器设置私有证据umask077时将它继承给子测试，导致4个默认按022创建夹具的旧用例失败；记录完整输出。仅子测试切回CI默认022，私有目录/文件仍0700/0600，最终585容器文件、438回收检查、14 CI合同和8范围均零跳过通过。错误的中间verified/manifest保留并明确标为未接受，不作为验收签收。

## 自包含发布和真实旧版拒绝

使用既有publish-terminal-probe.sh，从固定提交git archive输入锁定digest的SDK镜像，不把含私有环境的工作树挂入构建容器；linux-x64自包含发布及SHA256SUMS校验通过。探针AssemblyInformationalVersion与probeRevision匹配，148份相关源码（112份Python闭包及探针/Contracts）冻结。

真实1.58 Agent返回source/restore/purge=true、archive=false；新探针在capabilities阶段返回精确ARCHIVE_CAPABILITY_UNAVAILABLE、InvalidOperationException，stdout空、operationFramesSent=0。独立capability_refusal明确确认在密钥读取之前拒绝；它不能被socket故障/其他错误或旧purge拒绝替代。没有读取真实key、创建新容器/历史或调用归档操作方法。

前后53回收/29恢复/17清理永久意图完整行、活动0，回收Root124/HDD127/恢复Root80/目录Root98/purgeRoot34、2346保护、11核心及9694af1c宿主/应用身份保持。生产未安装，新能力未晋升；本批新CI仍需按新提交确认，不能把旧CI失败或本地通过记成远端绿灯。

最终证据 `container-recycle-archive-probe-candidate-0e71968f27d341969a968f31020d46e6` 共178文件另加manifest，摘要 `1964965c0ee60f6580db10c45ae34c61319b7a7897fbc40a4ab6856ed7fd4314`。所有私有目录Root0700/文件0600；二进制另在Root私有发布目录，完整包清单与入口散列固定。私有资源引用不上项目图谱。

继续安装态父级采集器、逐步骤Root/实际HDD/命名空间独立证明及失败保留/精确收尾，随后能力版本切换、标准安装与正向完整RPC。永久业务/共享保护/Worker/API/Vue/Jobs、真实身份页面、故障/成功claim与暂存清理、留存/维护连续性及完整59项仍待完成；17项移出不算完成，整项0/59、阶段0/6、工程量粗估25%–35%、酒馆/Live2D功能0%保持。
