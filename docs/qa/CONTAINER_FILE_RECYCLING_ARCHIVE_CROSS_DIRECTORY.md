# 归档同源拒绝与跨容器目录围栏：真实整链及原失败

2026-11-07，验证快照 `61faea6cb7b5a8aebc2512f4f9ca8b15915c167b` 已用全新两套自有容器和临时真实身份完成本项整链。此前两轮失败仍保留，不追认、不重放旧Apply/ACK。产品仍f318d9ab/协议1.59，本批无产品或配置修改、无发布，master未改动。

## 本轮真实结果

- 原owner通过可见Vue完成回收、留存清理Ready取消/实际清理、归档Ready取消与目标Ready。对相同sourceIntentId提供新幂等key，经真实认证API取得409/OPERATION_CONFLICT；保存请求开始/响应到达及实际DTO，原Ready全行/版本/DTO相等，新key的archive intent与Job均0。此负向请求明确是API探针，header标记为已提供，不伪称浏览器network观测。
- 原归档在Root Publishing、命名空间完整pending新目标/空stage时绑定原八FD/Invocation/pidfd，页面取消后helper3.146139秒退出，结果Unknown/复制null，Root/claim/目标/HDD保持。随后原owner正常双端退出，另一Subject在新context正常登录，旧owner引用404，实际Jobs19字段显示未知/未证明，无重试按钮。
- 不同容器真实Vue“新建目录”入口仅生成1计划、登记1意图，mode0750，无cancel/upload。归档Unknown的数据库publish barrier保留期间，目录保持Pending/Queued、root_started空、目标及执行journal不存在；Inspect可以建立其原空serialization lock，owner/root:root0600/单link/空文件及跨样本同inode均验证。
- 四次**自然**延期版本2、4、6、8，next_attempt依次为10:49:59.929717、10:50:06.884354、10:50:13.884065、10:50:20.846023（UTC+08）。每个样本重新读完整原Unknown SQL/Root bytes/目标/claim/HDD、原普通文件，15类barrier只允许archive=1、其他及file_path_locks=0，间隔和due时序独立校验。没有改SQL/时钟或手动触发worker。
- 另一管理员“暂不处置”后仍0 ACK、2次管理写（目录plan+register），前后原归档全部相等；四样本验证后才放行唯一真实页面ACK/202。归档Acknowledged/Failed、Outbox2→0，复制null/Root Unknown/原native claim/目标/HDD及receipt不改，native source claim**没有**因数据库barrier释放而被清除。
- 原同一个目录意图在ACK.completed后4.209954秒首次root_started，最终Succeeded/CONTAINER_DIRECTORY_CREATED，lease/barrier清除、pending0、恰1终态审计。原SQL绑定/owner/key/plan/trace与实际native Root/UID1000/GID1000/0750/空目录inode/签名版本逐项相联。UI重新打开同一意图显示成功和实际目录，登记仍恰1，未建立替代任务；本轮管理写恰3（plan/register/ACK），ACK恰1。
- 当前管理员正常双端退出，目录私有查询匿名401；原归档普通9/匿名5拒绝通过。两次退出均实际经过provider和RP callback，BFF401/身份源401或403且最终WhaleDeck登录页。

这里验证的是**全局数据库publish barrier**导致跨容器目录调度延期，不是native source claim直接阻塞另一个容器。source claim的独立保护由原件及归属绑定证明，ACK后仍保留。完整物理副本不证明历史复制成功、目录fsync或Published/Completed持久回执，复制保持null。

## 独立核验、准确收尾与当前状态

独立live在任何删除之前重读原归档SQL/Root/claim/完整目标与HDD、来源拒绝包、四延期样本、目录SQL/Root/实际inode、两容器身份与原普通文件。随后仅按已证明身份清除新空目录和其空private，再持原三把锁/原发布后父版本/nofollow FD精确移除本轮归档target/空stage，保留Root/claim/HDD与原目录历史。两自有容器及夹具准确移除，无递归清理未知项。

final及第三次当前独立核验通过，当前基线等于after；仅允许source/purge/archive及恰一个新directory JSON/lock增量，全部旧目录SQL行/Root历史和旧业务hash保持，2346保护、11核心、21安装模块/f318及user NOPASSWD不变。源码和物理检查不声称抵抗任意root并发替换或原子inode条件unlink。

累计意图78回收/29恢复/65清理/46归档，活动0；Root/HDD202/179、恢复95、目录102、purge90、archive48。源/HDD同device，不签跨物理盘灾备。

unit `whaledeck-qa-archive-cross-directory-61faea6c0001` / invocation `8ca71ea57186434c9f0f4ed877dbeee2`，passed journal匹配，实际251.468秒。完整647回收root检查39.232秒、零跳过，8范围/14 CI合同及Node语法通过。该验证版本精确CI42330686454本轮仍在运行，未签完整终态；本轮没有产品部署。

root目录0700/文件0600，逐项权限及manifest核对，文件数另加manifest：

| 证据 | 文件/冻结源码/真图 | manifest SHA256 |
| --- | --- | --- |
| `container-recycle-archive-cross-directory-1f5e069a2b664cbe84c7d6abbe9eb9e6` | 250 / 147 / 14 | `685122cb70da515a803778cf1715ec0df52f0e1e09da7b4121d3792da14f22de` |
| `archive-cross-directory-recheck-prep-81952751522a4bc8af1c3b5ac5779940` | 153 / 147 / 0 | `0ff799743739df74ed58a9d54c0477335e2c7adc1d2b64f0d48f39df120822c0` |

仅签本代表性同源拒绝/全局目录调度围栏整链，不外推全部写入者或并发矩阵。下一步继续成功后claim/重复来源与HDD留存策略、维护连续性、大文件及59项完整交付，17项移出不算完成。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持。

## 以下为此前两轮候选与失败历史（原事实保留）

2026-11-07，本批实现候选门禁并执行两轮新资源检查，取得**同源409且零新意图/Job**实机分项；完整跨目录4次自然退避→ACK→原目录意图成功链仍未签收。最新模型修正 `c7eee03e` 须再用全新资源复验。产品仍f318d9ab/协议1.59，无产品或配置发布，master未改动。

### 两种保护的边界

归档native source claim绑定source ref和归档operation/owner/fingerprint，防止同一来源再次消费；目录调度的跨容器延期由数据库`ContainerRecycleArchiveIntents.PublishBarrier`控制。`ContainerDirectoryRepository.StartedAsync`发现任意归档barrier时返回Busy，dispatcher Defer清lease、6秒后重试；这不是native claim直接阻塞另一个容器目录。

候选采用两个自有容器：原归档在Publishing命名空间已发布后取消并进入Unknown；另一当前管理员以真实Vue目录入口登记同一条目录意图，要求4个自然退避版本/到期时间、目标和执行journal缺席、原归档SQL/Root/claim/目标/HDD保持，15类操作仅归档barrier=1且路径锁0。只有积累证明后才允许唯一ACK，再要求同一目录意图的root_started不早于ACK.completed且最终Succeeded。ACK释放数据库barrier，原native claim仍保留，不能把两者混称。

### 首轮a06ec39a：浏览器拒绝探针未完成

首轮 `container-recycle-archive-cross-directory-9cbe13a10947424f99686a8a43b895a6` 在归档Ready处调用浏览器fetch重复来源。服务端日志确认抛出`CONTAINER_RECYCLE_ARCHIVE_SOURCE_CHANGED`，但客户端未生成完整拒绝包，未进入归档确认或登记目录。原Ready意图自然Expired、execution/root_started/reservation/receipt均空，执行Root及claim均不存在。

没有再次发送请求、调整时间或数据库。核对原QA unit invocation、MainPID、原Node子进程argv/PPid/cgroup及pidfd后，只终止该卡住的Node；Parent正常保留失败。unit `whaledeck-qa-archive-cross-directory-a06ec39a0001` / invocation `90a3e59bbf15467e969bb2cab408ceee`，failed/1，551.593秒。两套夹具按原ID/镜像/标签/StartedAt与精确目录项清理，无API/原生执行重放，原source/purge/HDD及旧目录行/Root历史保持，独立当前验证通过。

`58e87144`将负向探针改为同一认证上下文的真实API请求，显式Origin/CSRF/新Idempotency-Key，15秒时限、禁止跟随重定向、调用前后落脱敏阶段标记。仅声明header已提供，不再声称浏览器network实际观察；目录登记和结果仍走真实UI。首轮具体浏览器等待点未定位，不能声称产品来源拒绝失效或浏览器根因已修复。

### 第二轮58e87144：同源拒绝通过，采样模型拒绝Inspect锁

全新资源取得真实409/OPERATION_CONFLICT，保存实际API包，原Ready API DTO和SQL全行/版本相等，新key的archive intent与operation job均0。随后取得Publishing原pending完整目标/空stage与8FD/pidfd证明，原helper在页面取消后3.273945秒退出；Unknown/复制null/Root/claim/HDD保持，另一管理员真实UI登记不同容器目录。

采样器初版误要求目录`.lock`也不存在。实机显示目标不存在、目录journal不存在、目录Pending/root_started空/Queued、原归档Root字节保持；只有该目录operation的空serialization lock存在。只读Inspect可以创建此锁，不能据此推导写目录已开始。因此Parent在第一轮采样即失败，4次退避和成功续跑未签；未执行原门禁页面ACK。

失败 `container-recycle-archive-cross-directory-49f6c1255ac94731b208e6f51f68fe82` 原件封存。另以新临时管理员通过产品API对固定原Unknown执行恰一次ACK/202，前后实际DTO的id/version/execution/receipt和SQL相联，Root/claim/目标/HDD保持。随后原等待目录因夹具已自然结束收敛Failed/Aborted、目标缺席；这不是要求的Succeeded续跑，不补签UI。原目录journal与lock作为历史保留。

API-only准确收尾持原三锁/发布后父版本/nofollow FD和完整snapshot，只删除本轮target/空stage/原空private与两套夹具；Root/claim/HDD保持，旧目录行和历史不改，独立当前SQL/HDD/物理缺席验证通过。处置原门禁始终originalGatePassed=false。另一次处置准备因工作树源码hash与HEAD不同在任何API请求前拒绝，未授权重复ACK；形成干净快照后才执行唯一请求。

### c7eee03e修正与检查

不放宽目标、journal或SQL root_started缺席要求。若Inspect锁存在，必须经原operation锁读取器验证root:root/0600/单link/空文件并记录device/inode；从首次出现后，后续样本不允许换锁或消失。仅在ACK后证明原目录intent完成才记成功；独立live仍必须早于两个夹具的物理清理。

最新完整647项回收root检查38.891秒、零跳过，11专项及8范围/14 CI合同/Python和Node语法通过。构造测试不替代上述待复验的4次真实调度与Succeeded结果。准确c7eee03e完整CI终态待核验；本批无部署。

当前累计意图77回收/29恢复/63清理/44归档，活动0；Root/HDD199/177、恢复94、目录100、purge88、archive46。旧业务、2346保护、11核心、21安装模块/f318及user NOPASSWD保持。源/HDD同device，不签跨物理盘灾备。

### 证据

root目录0700/文件0600，文件数另加manifest：

| 证据 | 文件 | manifest SHA256 |
| --- | ---: | --- |
| `archive-cross-directory-prep-69eac817f2824292aad7b14c30c7b8a0`（147源码） | 150 | `4f9e831e8f4801ea4508c4ef94f8c3cbb00aa1a245bfe25017119b703bc53804` |
| `container-recycle-archive-cross-directory-9cbe13a10947424f99686a8a43b895a6`（首轮失败） | 197 | `9604c4fbd735fa0d669294462300dd10e88630e5b076aefcc73e9edefae3d31a` |
| `archive-cross-directory-failed-cleanup-7d9760c79e644127bd1feb57e2962ae6` | 9 | `d86ac3134a341e0eee187b320455cfb1247de852b2865c5cdc40f78f6e6f2b5d` |
| `archive-cross-directory-bounded-prep-c76ce094991d49079b1fec716cbeec76`（147源码） | 149 | `a6ccf8afa06227778b190da187730563dc18f288d4e541e72e0fad2983afb831` |
| `container-recycle-archive-cross-directory-49f6c1255ac94731b208e6f51f68fe82`（第二轮失败） | 224 | `acec5a1cc021a16c30cedd4ea4200ba4eb15629c5a25410d0c95bd3ac93c2401` |
| `archive-peer-unknown-disposal-403fa1a650524872bb7235998545a4d7`（147源码） | 174 | `18842b0061e182c096e0cc7107d2667b90668efabdb3fb602097655becc24afd` |

下一步用全新资源复验Inspect锁模型、4次自然目录退避、唯一ACK后的同意图成功续跑和准确收尾；原请求/Apply/ACK不重放。继续成功后claim/重复来源/HDD留存策略、维护连续性、大文件及59项完整交付，17项移出不算完成。整项0/59、阶段0/6、工程粗估25%–35%、酒馆/Live2D功能0%保持。
