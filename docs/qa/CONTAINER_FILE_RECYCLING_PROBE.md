# 回收原生探针与独立证明候选

2026-11-05，承接[1.56协议/SDK/受限传输](CONTAINER_FILE_RECYCLING_RPC.md)。新增独立交互探针及wire/Root/Trash-HDD证明，冻结探针`b7fc8e334c59d4d55860f8bc8382a47d989a1870`、最新Python证明`aa8a39b206279f0cdf802dafe10b84abbdf95dbf`。两版探针按标准脚本从Git archive和固定digest SDK自包含发布；最终探针三份源码与最新验证提交逐字节一致。协议源码3725aa752316ba933a850376786119e528248815的CI41997949781已completed/success、12路全部成功，最新两提交的src/deploy/host与该协议提交无差异。

## 探针约束

首行严格绑定产品/探针revision、容器ID/实例及不同的核心ID，校验程序集SourceRevisionId；仅固定`work/recycle-rpc.bin`、最多64帧和有界输入。能力必须major1/minor≥56且包含docker.files.recycle，之后才读取32字节密钥；finally清零，不输出签名、key或异常原文。

仅接受本进程Plan实际返回并登记的完整计划，不只比较fingerprint；首次operation只能由Apply/Discard/拒绝测试登记，重用ID必须全计划一致。Restore继续引用原Root记录，但创建独立新job和幂等键，再次Restore保留该job；结果输出动作、原operation与实际job，Unknown的两个源事实必须保持缺失。

规划拒绝矩阵定义17项：无令牌、其他/通用域、过期/过远到期、错方法、内容/上下文变化、格式、核心/只读/保护路径、旧版本及nonce重用。操作拒绝矩阵定义14项，包含字段篡改/跨域/错方法、恢复复用旧job、Apply使用新job及确切原记录缺席。它们是将来的安装态检查，不由本批能力拒绝推导已经执行。

## 独立证明

Python侧严格验证完整字段、整数与canonical类型/范围、普通文件/link/mode/size、属性UTF8顺序/大小/Base64、固定路径、源version及回收域fingerprint。Root记录的native计划也经过同一严格规则，不能重算hash后接受字符串数值或布尔owner。RPC receipt JSON拒绝重复键/缺字段，Restore新job和其他动作原job分别约束；Unknown缺字段规范化为None而非false。

Root/HDD证明绑定operation/owner/完整计划、原inode元数据、专属ContainerFileTrash/Trash、状态/历史摘要、外部实读的数据SHA/大小。覆盖备份不能混入，已验证历史和当前可用性分开；证明函数明确`physicalNamespaceChecked=false`，活namespace、完整文件、权限、取消及清理由拥有资源的门禁另查，不凭一份成功RPC签整链。

14项独立测试通过、零跳过。部分用例实际操作临时Linux文件并生成真实回收/恢复格式记录，namespace/liveness/mount/udev仍为显式夹具；不算安装态Agent/HDD。探针Release构建0警告/0错误，最终自包含包`terminal-probe.SUH8cQ`的SHA256SUMS逐项通过。

## 当前1.55的只读拒绝

实际运行版本仍34153d2。只向新探针发送首行，使用明确合成且未创建的资源header，后续操作帧0。旧Agent返回能力后，新探针在capabilities阶段以专属`CONTAINER_RECYCLE_CAPABILITY_UNAVAILABLE`拒绝，stdout为空、InvalidOperationException且rpcStatus/fileCode为空；专属码只在收到能力响应并判断不支持后设置。当前控制流没有达到密钥读取，不声称做过系统调用追踪。

初版只有phase/type诊断，不足以把连接故障与版本拒绝严格区分；原回执保留且不按最终门禁接受。增加专属拒绝码、负向校验后另行发布并只读复验，最终证明仅引用strict-old-version-refusal.json。没有创建真实测试容器、文件、恢复job或执行Plan/Apply/Restore，生产前后基线相同；不能把这个预期拒绝当成1.56签名RPC成功。

## 封存与下一步

私有目录`container-recycle-probe-8b99ae45e1b8436392e1c0db09600208`封存23文件，清单摘要`35adee5d69eef280705efe85261e434ea7d5d5817f9ad125656067ccd76486ae`。含两次冻结发布unit/journal、各自包来源摘要、严格拒绝与初版分类纠正、生产基线、协议CI12路成功回执、验证源码和范围说明。初次CI查询使用短SHA未返回记录，改为完整SHA后核实成功，未将空列表判成失败或未运行。

下一步把探针接入全新自有资源的安装态父门禁，逐步核验原计划/Root/HDD/实际文件与恢复新job，再按成功CI版本标准安装Agent并验证签名RPC、拒绝/取消/兼容性。当前未安装1.56，未签helper实际取消、业务Worker/API/Vue或真实身份整链；探针进程本身中断后的旧操作收养也未提供，不能任意恢复旧记录。59项、阶段完整签收和整体25%–35%粗估保持。

收尾：探针/证明代码已推送dev，范围8项、2116本地Markdown链接/缺失0、diff及23文件封存再核验通过，Git索引仍归用户所有。项目图谱事件whale-deck.container-recycle.aa8a39b2.probe-proof-old-version取得projectId/key/eventId匹配、duplicate=false回执，pending移至synced、阶段active。协议3725aa7的12路CI成功不等于最新探针提交的CI终态，后者本批未签收。
