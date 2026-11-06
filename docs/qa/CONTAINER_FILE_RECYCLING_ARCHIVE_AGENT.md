# 归档复制1.59：标准宿主安装与真实签名RPC/HDD分项

2026-11-07，产品 `3f41895b37bf39deae8f6db2ab202f250274edbb`、修正探针 `bcba2fc50f8ce9be4691b5996cc6f8cd3e8e1a96` 已完成标准宿主安装和全新资源正向整链。承接[父级采集器](CONTAINER_FILE_RECYCLING_ARCHIVE_GATE_CANDIDATE.md)，本批只签安装态原生能力；没有永久归档意图/Worker/API/Vue/Jobs或真实Authentik身份页面验收，故障矩阵、长期策略和维护连续性仍待完成，不提升整项/阶段完成率。

## 精确产品构建与标准安装

Agent source协议切到1.59，增加独立docker.files.recycle.archive；16个既有能力版本断言同步，新SDK最小1.59门禁不变。产品3f41895b的CI42256305458已completed/success，12路全成功；安装控制器在任何写入前再次核对精确head/终态/12项。CI查询曾间歇EOF，使用同一GitHub目标的单进程直连复核后通过，没有改系统代理、降低CI门禁或把未知结果当通过。

独立detached worktree固定产品提交，标准publish.sh仅将Git archive公开源挂进锁定digest的SDK镜像，未挂真实.env或整个工作树。两个Release/linux-x64自包含宿主、708发布文件、SHA256SUMS及build-info通过；NETCore/AspNet/Pkcs均10.0.13。publish unit `whaledeck-qa-archive-publish-3f41895b`，Invocation `38f8feed56f2466bb49851fb4ec91efa`，原日志明确success，耗时155.172秒。

标准install.sh/validate.sh均在精确worktree执行；install unit `whaledeck-qa-archive-install-3f41895b`，Invocation `08e604a68bee495084961bd6e54085e3`，原日志success，8.009秒。Agent PID1244695与Maintenance PID1244692均3f41895b、active/NRestarts0；四个应用容器与运行checkout仍9694af1c，不能将宿主安装称为应用覆盖发布。21份安装helper/transport逐字节等于产品提交，root:root/0750、普通单链接；新archive namespace root:root/0700、安装后初始为空。

保护比较仅允许两个宿主PID/构建身份更新，以及Agent sudo规则/resources.json/agent.env的mtime/ctime和能力key的ctime向前；四配置内容SHA、设备/inode/mode/uid/gid/link/size保持，key的mtime亦保持。其他2346保护清单项、开发user NOPASSWD:ALL规则、全部旧Root/HDD/业务逐值相同。新增纯比较器4项负向检查锁定此白名单，不放宽整棵Root。

## 维护采样不接受连续性

安装与标准后置检查期间23样本，22次HTTP200/no-store/healthy-Ready，1次ConnectionError（UTC17:09:03.324204），随后恢复。全部原样保存；不据恢复200或有限采样签瞬时连续性，维护入口连续性仍为剩余目标。本批安装通过指构建/标准入口/运行身份/保护保持，不包含“零中断”。

## 首次实机失败与准确收尾

首批原生门禁 `whaledeck-qa-archive-agent-3f41895b` / Invocation `f36e669c888942988baf79fc5a0b24c6` 未通过。source回收、purge取消/正常清理已完成并核对，归档计划第11拒绝用例（内层source path篡改）因未重算内层purge指纹，先被结构校验返回InvalidArgument，而预期是签名绑定PermissionDenied；19帧后停止，archive操作/claim/journal均未开始，没有把拒绝算成复制。

保留原探针、stdout/stderr/Root/HDD和原失败，资源保持未结状态。独立收尾重新核对本轮Purged来源/Completed purge/Purged marker、三名称缺席、当前完整HDD、旧业务/保护与archive/restore命名空间未变，再仅删除准确空旧private、准确自有容器与已知普通fixture种子；未重建旧inode，HDD及回执保留。收尾初次调用旧plan_frame漏写keyword-only boundaries，TypeError发生在任何清理前；纠正后准确收尾通过并留事实。

探针现将计划/操作变异抽为共享partial代码，并直接链接进单元测试：10项计划和20项操作变异须逐一经过真实Agent结构校验且body hash不同，再测签名绑定。计划同时重算内层purge、调整SHA/历史耦合字段；操作继续重算内外指纹。初次30专项28通过2失败，因构造夹具的purge owner已经等于固定变异值；改为保证不同的hash，不放松结构/签名校验，最终30全通过。

全量5801单元零跳过、782集成通过/7默认原生跳过；585容器文件、453回收Linux root零跳过、14CI合同、8范围与4安装比较检查通过。修正探针按标准Git archive流程重发完整自包含包与checksum，没有修改生产产品或重放旧写入。

## 全新资源正向签名整链

复验unit `whaledeck-qa-archive-agent-bcba2fc5`，Invocation `acba5612f1af46eaa5f1e8440b0513b1`，原日志passed/Deactivated successfully，106.945秒。新容器为固定镜像、非root/只读根、无网络和固定预算，新1,048,583字节文件含1000:1000/0640/精确mtime/user属性；全部source/purge/archive ID均新建，不重放首批操作。

source计划/Apply/Inspect达到Trashed并逐次核Root原字节/实际留存inode/当前HDD；purge18计划/26操作拒绝、取消与正常清理通过，原件/留存/pending缺席、HDD数据完整、旧来源/历史Purged。随后archive22计划/31操作拒绝达到精确预期，取消ID Reserve/Inspect→Discard/迟到Apply/Inspect均保持未复制/Aborted，claim释放且无stage/目标副作用。

另一新archive ID重新计划/Reserve/Inspect后实际Apply达到Completed；目标是与原已删及HDD均不同的新inode，完整字节/属性、UID/GID/mode/单链接/大小/mtime与原件一致，Root的prepared/published身份、Root0700空stage及精确归属claim对应。Inspect/重复Apply/Discard仍同一Completed/同一新inode，未再次写。原source/purge/marker/HDD生命周期和字节一直保持Purged，复制结果仅归新操作。

第34帧前缀明确closed=false；独立进程核原probe PID/start tick/cmdline仍活，重读实际目标、当前容器实例、Root/claim/全部旧记录/当前HDD、配置目录身份及源码/安装/包散列后才放行QA收尾。父级重新绑定parent/leaf FD和准确空stage，仅移除已证明的新副本与目录；之后同一签名Inspect报告历史Completed/archiveCopied=true、bindingCurrent=false，Root/claim/HDD不变。最终36帧含12归档回执、4来源回执，精确fixture移除后独立final和第三次当前HDD/记录重读均通过；humanUi=false，不冒充真实身份页面。

## 保持与证据

53回收/29恢复/17清理永久意图完整行及活动0保持，没有创建永久归档业务。回收Root124→130、HDD127→131、purgeRoot34→42只增加首失败与新成功两套来源/四个新purge的限定记录；archive初始0→4仅新成功的取消/复制两个ID各json/lock；restoreRoot80→81仅成功来源的一份archive claim。目录Root98、2346保护、11核心及四应用9694af1c身份保持。源与HDD同device2049，不称跨物理盘灾备。

所有私有证据目录Root0700、文件0600，已安装/opt可执行权限不受封存影响；探针包另在Root私有发布目录保留可执行入口。

| 批次 | 文件数（另加manifest） | manifest SHA256 |
|---|---:|---|
| container-recycle-archive-install-585e912326724398bca3733bd0f988a6 | 62，25改动源码及构建/安装/采样/保持 | 749180c04243d070174ef1cb67c5205ccfdf2ed2a15785c155c2da3f94edf87f |
| 首失败 container-recycle-archive-agent-95b468db6329408694650bf9dc4337db | 197，原失败/准确处置，不签整体成功 | e679654293174ba47f7eea7b0051ad21aa24aa9e0df9a366f7cd94105701f4c0 |
| 新成功 container-recycle-archive-agent-0c3046cb2e1441408fbb1285f1ba0a40 | 279，124验证闭包/6探针契约来源与原件/live-final-third | a2001a564868e7ed956282ee4af5645b10c23161f1be5b4e4b61f811063435b8 |

产品3f41895b的准确CI12路已通过；本批新增QA/文档的远端CI另查，不沿用产品绿灯。继续永久意图/实际PG/共享写入保护/Worker/API/Vue/Jobs、真实身份整链与适用故障矩阵，成功claim/暂存清理、重复来源恢复策略、留存/维护连续性及其余59项。17项移出不算完成；整项0/59、阶段0/6、粗估工程25%–35%、酒馆/Live2D功能0%保持，master完整交付尚未完成。
